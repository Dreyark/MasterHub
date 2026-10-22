using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.StreamDeck;

/// <summary>
/// Serwer WebSocket loopback symulujący środowisko uruchomieniowe Elgato Stream Deck.
/// Umożliwia wtyczkom Stream Deck dwukierunkową komunikację z aplikacją MasterHub.
/// </summary>
public sealed class StreamDeckWebSocketServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, WebSocket> _pluginSockets = new();
    private readonly ConcurrentDictionary<string, string> _contextToPluginMap = new();
    private readonly ConcurrentDictionary<string, string> _contextToActionMap = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<object>> _pendingMessagesByPlugin = new();
    private readonly ConcurrentDictionary<string, JsonElement> _keySettings = new();
    private readonly ConcurrentDictionary<string, JsonElement> _globalSettings = new();

    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _isDisposed;

    public int Port { get; private set; }
    public bool IsRunning => _listener.IsListening;

    // Zdarzenia przychodzące od wtyczek Stream Deck
    public event Action<string, string>? PluginRegistered; // (pluginUuid, registerEvent)
    public event Action<string, string, string?>? SetImageRequested; // (context, imageString, pluginUuid)
    public event Action<string, string?>? SetTitleRequested; // (context, title)
    public event Action<string, int>? SetStateRequested; // (context, state)
    public event Action<string>? ShowAlertRequested; // (context)
    public event Action<string>? ShowOkRequested; // (context)
    public event Action<string>? OpenUrlRequested; // (url)
    public event Action<string, string>? LogMessageReceived; // (pluginUuid, message)
    public event Action<string, string>? SettingsUpdated; // (context, settingsJson)

    public StreamDeckWebSocketServer(int preferredPort = 28123)
    {
        Port = preferredPort;
    }

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        bool started = false;
        int attempts = 0;

        while (!started && attempts < 10)
        {
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                started = true;
            }
            catch
            {
                Port++;
                attempts++;
            }
        }

        if (!started)
        {
            throw new InvalidOperationException("Nie udało się uruchomić serwera WebSocket Stream Deck na porcie loopback.");
        }

        _listenTask = Task.Run(AcceptClientsAsync);
    }

    private async Task AcceptClientsAsync()
    {
        while (_listener.IsListening && _cts != null && !_cts.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    _ = ProcessWebSocketRequestAsync(context);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    private async Task ProcessWebSocketRequestAsync(HttpListenerContext httpContext)
    {
        WebSocket? webSocket = null;
        string? associatedPluginUuid = null;

        try
        {
            var wsContext = await httpContext.AcceptWebSocketAsync(null);
            webSocket = wsContext.WebSocket;

            var buffer = new byte[64 * 1024];
            using var ms = new MemoryStream();

            while (webSocket.State == WebSocketState.Open && _cts != null && !_cts.IsCancellationRequested)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    associatedPluginUuid = HandleIncomingMessage(json, webSocket, associatedPluginUuid);
                }
            }
        }
        catch { }
        finally
        {
            if (associatedPluginUuid != null)
            {
                _pluginSockets.TryRemove(associatedPluginUuid, out _);
            }
            webSocket?.Dispose();
        }
    }

    private string? HandleIncomingMessage(string json, WebSocket socket, string? currentUuid)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("event", out var eventProp))
            {
                string eventName = eventProp.GetString() ?? "";

                // Rejestracja wtyczki (pierwszy komunikat po nawiązaniu połączenia)
                if (root.TryGetProperty("uuid", out var uuidProp))
                {
                    string uuid = uuidProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(uuid))
                    {
                        currentUuid = uuid;
                        _pluginSockets[uuid] = socket;
                        PluginRegistered?.Invoke(uuid, eventName);
                        _ = FlushPendingMessagesAsync(uuid);
                    }
                }

                string context = root.TryGetProperty("context", out var ctxProp) ? (ctxProp.GetString() ?? "") : "";
                if (!string.IsNullOrEmpty(context) && currentUuid != null)
                {
                    _contextToPluginMap[context] = currentUuid;
                }

                switch (eventName)
                {
                    case "setImage":
                        if (root.TryGetProperty("payload", out var imgPayload) &&
                            imgPayload.TryGetProperty("image", out var imgData))
                        {
                            string? imgStr = imgData.GetString();
                            if (!string.IsNullOrEmpty(imgStr))
                            {
                                SetImageRequested?.Invoke(context, imgStr, currentUuid);
                            }
                        }
                        break;

                    case "setTitle":
                        if (root.TryGetProperty("payload", out var titlePayload) &&
                            titlePayload.TryGetProperty("title", out var titleData))
                        {
                            SetTitleRequested?.Invoke(context, titleData.GetString());
                        }
                        break;

                    case "setState":
                        if (root.TryGetProperty("payload", out var statePayload) &&
                            statePayload.TryGetProperty("state", out var stateData))
                        {
                            SetStateRequested?.Invoke(context, stateData.GetInt32());
                        }
                        break;

                    case "showAlert":
                        ShowAlertRequested?.Invoke(context);
                        break;

                    case "showOk":
                        ShowOkRequested?.Invoke(context);
                        break;

                    case "openUrl":
                        if (root.TryGetProperty("payload", out var urlPayload) &&
                            urlPayload.TryGetProperty("url", out var urlData))
                        {
                            OpenUrlRequested?.Invoke(urlData.GetString() ?? "");
                        }
                        break;

                    case "logMessage":
                        if (root.TryGetProperty("payload", out var logPayload) &&
                            logPayload.TryGetProperty("message", out var msgData))
                        {
                            LogMessageReceived?.Invoke(currentUuid ?? "unknown", msgData.GetString() ?? "");
                        }
                        break;

                    case "setSettings":
                        if (root.TryGetProperty("payload", out var setSettingsPayload))
                        {
                            _keySettings[context] = setSettingsPayload.Clone();
                            SettingsUpdated?.Invoke(context, setSettingsPayload.GetRawText());
                        }
                        break;

                    case "getSettings":
                        _ = SendSettingsAsync(context, currentUuid);
                        break;

                    case "setGlobalSettings":
                        if (currentUuid != null && root.TryGetProperty("payload", out var setGlobalPayload))
                        {
                            _globalSettings[currentUuid] = setGlobalPayload.Clone();
                        }
                        break;

                    case "getGlobalSettings":
                        _ = SendGlobalSettingsAsync(currentUuid);
                        break;
                }
            }
        }
        catch { }

        return currentUuid;
    }

    public bool IsPluginConnected(string pluginUuid)
    {
        return _pluginSockets.TryGetValue(pluginUuid, out var socket) && socket.State == WebSocketState.Open;
    }

    public async Task SendEventAsync(string pluginUuid, object payload)
    {
        if (_pluginSockets.TryGetValue(pluginUuid, out var socket) && socket.State == WebSocketState.Open)
        {
            try
            {
                string json = JsonSerializer.Serialize(payload);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }
        else
        {
            _pendingMessagesByPlugin.GetOrAdd(pluginUuid, _ => new ConcurrentQueue<object>()).Enqueue(payload);
        }
    }

    private async Task FlushPendingMessagesAsync(string pluginUuid)
    {
        if (!_pendingMessagesByPlugin.TryGetValue(pluginUuid, out var queue) || queue.IsEmpty)
            return;

        if (_pluginSockets.TryGetValue(pluginUuid, out var socket) && socket.State == WebSocketState.Open)
        {
            while (queue.TryDequeue(out var payload))
            {
                try
                {
                    string json = JsonSerializer.Serialize(payload);
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch { }
            }
        }
    }

    public async Task SendSettingsAsync(string context, string? pluginUuid)
    {
        if (string.IsNullOrEmpty(pluginUuid) && !_contextToPluginMap.TryGetValue(context, out pluginUuid))
            return;

        _keySettings.TryGetValue(context, out var settings);
        _contextToActionMap.TryGetValue(context, out var actionUuid);

        var msg = new
        {
            @event = "didReceiveSettings",
            action = actionUuid ?? "",
            context = context,
            device = "masterhub-keydeck-15",
            payload = new
            {
                controller = "Keypad",
                settings = settings.ValueKind != JsonValueKind.Undefined ? (object)settings : new { },
                coordinates = new { column = 0, row = 0 },
                state = 0,
                isInMultiAction = false
            }
        };

        await SendEventAsync(pluginUuid, msg);
    }

    public async Task SendGlobalSettingsAsync(string? pluginUuid)
    {
        if (string.IsNullOrEmpty(pluginUuid)) return;

        _globalSettings.TryGetValue(pluginUuid, out var global);

        var msg = new
        {
            @event = "didReceiveGlobalSettings",
            payload = new
            {
                settings = global.ValueKind != JsonValueKind.Undefined ? (object)global : new { }
            }
        };

        await SendEventAsync(pluginUuid, msg);
    }

    public void AssociateContext(string context, string pluginUuid, string? actionUuid = null, string? initialSettingsJson = null)
    {
        _contextToPluginMap[context] = pluginUuid;
        if (!string.IsNullOrEmpty(actionUuid))
        {
            _contextToActionMap[context] = actionUuid;
        }
        if (!string.IsNullOrEmpty(initialSettingsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(initialSettingsJson);
                _keySettings[context] = doc.RootElement.Clone();
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _cts?.Cancel();
            _listener.Stop();
            _listener.Close();

            foreach (var ws in _pluginSockets.Values)
            {
                try { ws.Dispose(); } catch { }
            }
            _pluginSockets.Clear();
        }
        catch { }
    }
}
