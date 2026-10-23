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
    private readonly ConcurrentDictionary<string, WebSocket> _piSockets = new();
    private readonly ConcurrentDictionary<string, string> _contextToPluginMap = new();
    private readonly ConcurrentDictionary<string, string> _contextToActionMap = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<object>> _pendingMessagesByPlugin = new();
    private readonly ConcurrentDictionary<string, JsonElement> _keySettings = new();
    private readonly ConcurrentDictionary<string, JsonElement> _globalSettings = new();
    private readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _socketSendLocks = new();
    private readonly string _globalSettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MasterHub", "streamdeck_global_settings.json");

    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _isDisposed;

    public int Port { get; private set; }
    public bool IsRunning => _listener.IsListening;
    public Func<string, string?>? PluginFolderResolver { get; set; }

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
    public event Action<string, string>? GlobalSettingsUpdated; // (pluginUuid, globalSettingsJson)
    public event Action<string>? SwitchToProfileRequested; // (profileName)
    public event Action<string, string>? SetFeedbackRequested; // (context, payloadJson)

    public StreamDeckWebSocketServer(int preferredPort = 28123)
    {
        Port = preferredPort;
        LoadGlobalSettingsFromDisk();
    }

    private void LoadGlobalSettingsFromDisk()
    {
        try
        {
            if (File.Exists(_globalSettingsFilePath))
            {
                string json = File.ReadAllText(_globalSettingsFilePath);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    _globalSettings[prop.Name] = prop.Value.Clone();
                }
            }
        }
        catch { }
    }

    private void SaveGlobalSettingsToDisk()
    {
        try
        {
            var dir = Path.GetDirectoryName(_globalSettingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var dict = new Dictionary<string, object>();
            foreach (var kvp in _globalSettings)
            {
                dict[kvp.Key] = kvp.Value;
            }

            string json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_globalSettingsFilePath, json);
        }
        catch { }
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
                if (_cts == null || _cts.IsCancellationRequested || context.Request.Url?.AbsolutePath == "/__shutdown")
                {
                    try { context.Response.Close(); } catch { }
                    break;
                }
                if (context.Request.IsWebSocketRequest)
                {
                    _ = ProcessWebSocketRequestAsync(context);
                }
                else
                {
                    _ = ProcessHttpRequestAsync(context);
                }
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    private async Task ProcessHttpRequestAsync(HttpListenerContext httpContext)
    {
        try
        {
            var request = httpContext.Request;
            var response = httpContext.Response;

            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "*");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            string rawUrl = request.Url?.AbsolutePath ?? "";
            if (rawUrl.StartsWith("/plugin/", StringComparison.OrdinalIgnoreCase))
            {
                string remainder = rawUrl.Substring("/plugin/".Length);
                int slashIdx = remainder.IndexOf('/');
                if (slashIdx > 0)
                {
                    string pluginUuid = remainder[..slashIdx];
                    string relPath = remainder[(slashIdx + 1)..].Replace('/', Path.DirectorySeparatorChar);

                    string? pluginFolder = PluginFolderResolver?.Invoke(pluginUuid);
                    if (!string.IsNullOrEmpty(pluginFolder) && Directory.Exists(pluginFolder))
                    {
                        string fullPath = Path.GetFullPath(Path.Combine(pluginFolder, relPath));
                        string fullPluginFolder = Path.GetFullPath(pluginFolder);

                        if (fullPath.StartsWith(fullPluginFolder, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
                        {
                            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
                            if (ext is ".html" or ".htm")
                            {
                                string html = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);

                                string context = request.QueryString["context"] ?? "";
                                string actionUuid = request.QueryString["action"] ?? "";

                                if (!string.IsNullOrEmpty(context))
                                {
                                    string settingsRaw = _keySettings.TryGetValue(context, out var s) ? s.GetRawText() : "{}";
                                    JsonElement settingsElem;
                                    try { using var d = JsonDocument.Parse(settingsRaw); settingsElem = d.RootElement.Clone(); }
                                    catch { using var d = JsonDocument.Parse("{}"); settingsElem = d.RootElement.Clone(); }

                                    var appInfo = new
                                    {
                                        application = new
                                        {
                                            font = "Segoe UI",
                                            language = "en",
                                            platform = "windows",
                                            platformVersion = Environment.OSVersion.Version.ToString(),
                                            version = "6.5.0"
                                        },
                                        plugin = new
                                        {
                                            uuid = pluginUuid,
                                            version = "1.0.0"
                                        },
                                        devicePixelRatio = 2,
                                        devices = new[]
                                        {
                                            new
                                            {
                                                id = "masterhub-keydeck-15",
                                                name = "MasterHub Keydeck 15",
                                                size = new { columns = 5, rows = 3 },
                                                type = 0
                                            },
                                            new
                                            {
                                                id = "masterhub-keydeck-plus",
                                                name = "MasterHub Keydeck Plus",
                                                size = new { columns = 4, rows = 2 },
                                                type = 7
                                            }
                                        }
                                    };

                                    int col = 0, row = 0;
                                    if (context.StartsWith("key_") && int.TryParse(context.AsSpan(4), out int kIdx))
                                    {
                                        col = kIdx % 5;
                                        row = kIdx / 5;
                                    }

                                    var actionInfo = new
                                    {
                                        action = actionUuid,
                                        context = context,
                                        device = "masterhub-keydeck-15",
                                        payload = new
                                        {
                                            settings = settingsElem,
                                            coordinates = new { column = col, row = row },
                                            isInMultiAction = false
                                        }
                                    };

                                    string appInfoJson = JsonSerializer.Serialize(appInfo);
                                    string actionInfoJson = JsonSerializer.Serialize(actionInfo);

                                    string injectScript = $@"<script>
(function() {{
    var port = {Port};
    var context = ""{context}"";
    var regEvent = ""registerPropertyInspector"";
    var info = {JsonSerializer.Serialize(appInfoJson)};
    var actionInfo = {JsonSerializer.Serialize(actionInfoJson)};
    function initPi() {{
        if (typeof window.connectElgatoStreamDeckSocket === 'function') {{
            window.connectElgatoStreamDeckSocket(port, context, regEvent, info, actionInfo);
        }} else {{
            setTimeout(initPi, 100);
        }}
    }}
    if (document.readyState === 'complete' || document.readyState === 'interactive') {{
        setTimeout(initPi, 50);
    }} else {{
        window.addEventListener('DOMContentLoaded', initPi);
        setTimeout(initPi, 250);
    }}
}})();
</script>";
                                    if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
                                    {
                                        int idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                                        html = html.Insert(idx, injectScript + "\n");
                                    }
                                    else
                                    {
                                        html += "\n" + injectScript;
                                    }
                                }

                                byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
                                response.ContentType = "text/html; charset=utf-8";
                                response.ContentLength64 = htmlBytes.Length;
                                await response.OutputStream.WriteAsync(htmlBytes);
                                response.Close();
                                return;
                            }

                            string mime = ext switch
                            {
                                ".js" or ".mjs" => "application/javascript; charset=utf-8",
                                ".css" => "text/css; charset=utf-8",
                                ".json" => "application/json; charset=utf-8",
                                ".png" => "image/png",
                                ".jpg" or ".jpeg" => "image/jpeg",
                                ".gif" => "image/gif",
                                ".svg" => "image/svg+xml",
                                ".woff" => "font/woff",
                                ".woff2" => "font/woff2",
                                ".ttf" => "font/ttf",
                                _ => "application/octet-stream"
                            };

                            byte[] fileBytes = await File.ReadAllBytesAsync(fullPath);
                            response.ContentType = mime;
                            response.ContentLength64 = fileBytes.Length;
                            await response.OutputStream.WriteAsync(fileBytes);
                            response.Close();
                            return;
                        }
                    }
                }
            }

            response.StatusCode = 404;
            response.Close();
        }
        catch
        {
            try
            {
                httpContext.Response.StatusCode = 500;
                httpContext.Response.Close();
            }
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
                        var sendLock = _socketSendLocks.GetOrAdd(webSocket, _ => new SemaphoreSlim(1, 1));
                        if (await sendLock.WaitAsync(200).ConfigureAwait(false))
                        {
                            try
                            {
                                if (webSocket.State == WebSocketState.CloseReceived)
                                {
                                    await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).ConfigureAwait(false);
                                }
                            }
                            catch { }
                            finally
                            {
                                sendLock.Release();
                            }
                        }
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
                if (_pluginSockets.TryGetValue(associatedPluginUuid, out var curPl) && curPl == webSocket)
                {
                    _pluginSockets.TryRemove(associatedPluginUuid, out _);
                }
                if (_piSockets.TryGetValue(associatedPluginUuid, out var curPi) && curPi == webSocket)
                {
                    _piSockets.TryRemove(associatedPluginUuid, out _);
                    if (_contextToPluginMap.TryGetValue(associatedPluginUuid, out var plUuid))
                    {
                        _contextToActionMap.TryGetValue(associatedPluginUuid, out var actUuid);
                        _ = SendEventAsync(plUuid, new
                        {
                            @event = "propertyInspectorDidDisappear",
                            action = actUuid ?? "",
                            context = associatedPluginUuid,
                            device = "masterhub-keydeck-15"
                        });
                    }
                }
            }
            if (webSocket != null && _socketSendLocks.TryRemove(webSocket, out var sl))
            {
                sl.Dispose();
            }
            try { webSocket?.Dispose(); } catch { }
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

                // Rejestracja wtyczki lub Property Inspectora
                if (root.TryGetProperty("uuid", out var uuidProp))
                {
                    string uuid = uuidProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(uuid))
                    {
                        currentUuid = uuid;
                        if (eventName == "registerPropertyInspector")
                        {
                            if (_piSockets.TryGetValue(uuid, out var oldPi) && oldPi != socket)
                            {
                                try
                                {
                                    if (oldPi.State == WebSocketState.Open)
                                        oldPi.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Replaced", CancellationToken.None).GetAwaiter().GetResult();
                                }
                                catch { }
                                try { oldPi.Dispose(); } catch { }
                            }
                            _piSockets[uuid] = socket;
                            if (_contextToPluginMap.TryGetValue(uuid, out var plUuid))
                            {
                                _contextToActionMap.TryGetValue(uuid, out var actUuid);
                                _ = SendEventAsync(plUuid, new
                                {
                                    @event = "propertyInspectorDidAppear",
                                    action = actUuid ?? "",
                                    context = uuid,
                                    device = "masterhub-keydeck-15"
                                });
                            }
                        }
                        else
                        {
                            if (_pluginSockets.TryGetValue(uuid, out var oldPl) && oldPl != socket)
                            {
                                try
                                {
                                    if (oldPl.State == WebSocketState.Open)
                                        oldPl.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Replaced", CancellationToken.None).GetAwaiter().GetResult();
                                }
                                catch { }
                                try { oldPl.Dispose(); } catch { }
                            }
                            _pluginSockets[uuid] = socket;
                            PluginRegistered?.Invoke(uuid, eventName);
                            _ = FlushPendingMessagesAsync(uuid);
                            _ = SendDeviceDidConnectAsync(uuid, "masterhub-keydeck-15", "MasterHub Keydeck 15", 0, 5, 3);
                            _ = SendDeviceDidConnectAsync(uuid, "masterhub-keydeck-plus", "MasterHub Keydeck Plus", 7, 4, 2);
                        }
                    }
                }

                string context = root.TryGetProperty("context", out var ctxProp) ? (ctxProp.GetString() ?? "") : "";
                if (!string.IsNullOrEmpty(context) && !string.IsNullOrEmpty(currentUuid) && !_piSockets.ContainsKey(currentUuid) && currentUuid != context)
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
                            string rawJson = setSettingsPayload.GetRawText();
                            SettingsUpdated?.Invoke(context, rawJson);

                            if (_contextToPluginMap.TryGetValue(context, out var plUuid))
                            {
                                _contextToActionMap.TryGetValue(context, out var actUuid);
                                int col = 0, row = 0;
                                if (context.StartsWith("key_") && int.TryParse(context.AsSpan(4), out int kIdx))
                                {
                                    col = kIdx % 5;
                                    row = kIdx / 5;
                                }

                                var didRecv = new
                                {
                                    @event = "didReceiveSettings",
                                    action = actUuid ?? "",
                                    context = context,
                                    device = "masterhub-keydeck-15",
                                    payload = new
                                    {
                                        settings = setSettingsPayload,
                                        coordinates = new { column = col, row = row },
                                        isInMultiAction = false
                                    }
                                };
                                _ = SendEventAsync(plUuid, didRecv);
                            }
                        }
                        break;

                    case "getSettings":
                        _ = SendSettingsAsync(context, currentUuid);
                        break;

                    case "setGlobalSettings":
                        if (root.TryGetProperty("payload", out var setGlobalPayload))
                        {
                            string? targetPlugin = null;
                            if (!string.IsNullOrEmpty(context))
                            {
                                _contextToPluginMap.TryGetValue(context, out targetPlugin);
                            }
                            if (string.IsNullOrEmpty(targetPlugin) && !string.IsNullOrEmpty(currentUuid))
                            {
                                if (_pluginSockets.ContainsKey(currentUuid))
                                {
                                    targetPlugin = currentUuid;
                                }
                                else
                                {
                                    _contextToPluginMap.TryGetValue(currentUuid, out targetPlugin);
                                }
                            }

                            if (!string.IsNullOrEmpty(targetPlugin))
                            {
                                _globalSettings[targetPlugin] = setGlobalPayload.Clone();
                                SaveGlobalSettingsToDisk();
                                GlobalSettingsUpdated?.Invoke(targetPlugin, setGlobalPayload.GetRawText());

                                var didRecvGlob = new
                                {
                                    @event = "didReceiveGlobalSettings",
                                    payload = new
                                    {
                                        settings = setGlobalPayload
                                    }
                                };
                                _ = SendEventAsync(targetPlugin, didRecvGlob);

                                foreach (var (ctxKey, piSocket) in _piSockets)
                                {
                                    if (_contextToPluginMap.TryGetValue(ctxKey, out var assoc) && assoc == targetPlugin && piSocket.State == WebSocketState.Open)
                                    {
                                        _ = SendRawJsonAsync(piSocket, JsonSerializer.Serialize(didRecvGlob));
                                    }
                                }
                            }
                        }
                        break;

                    case "getGlobalSettings":
                        {
                            string? targetPlugin = null;
                            if (!string.IsNullOrEmpty(context))
                            {
                                _contextToPluginMap.TryGetValue(context, out targetPlugin);
                            }
                            if (string.IsNullOrEmpty(targetPlugin) && !string.IsNullOrEmpty(currentUuid))
                            {
                                if (_pluginSockets.ContainsKey(currentUuid))
                                {
                                    targetPlugin = currentUuid;
                                }
                                else
                                {
                                    _contextToPluginMap.TryGetValue(currentUuid, out targetPlugin);
                                }
                            }
                            if (string.IsNullOrEmpty(targetPlugin) && !string.IsNullOrEmpty(context))
                            {
                                targetPlugin = context;
                            }
                            _ = SendGlobalSettingsToSocketAsync(socket, targetPlugin);
                        }
                        break;

                    case "sendToPlugin":
                        {
                            string? targetPlugin = null;
                            if (!string.IsNullOrEmpty(context))
                            {
                                _contextToPluginMap.TryGetValue(context, out targetPlugin);
                            }
                            if (string.IsNullOrEmpty(targetPlugin) && !string.IsNullOrEmpty(currentUuid))
                            {
                                if (_pluginSockets.ContainsKey(currentUuid))
                                {
                                    targetPlugin = currentUuid;
                                }
                                else
                                {
                                    _contextToPluginMap.TryGetValue(currentUuid, out targetPlugin);
                                }
                            }

                            if (!string.IsNullOrEmpty(targetPlugin))
                            {
                                _ = SendRawJsonToPluginAsync(targetPlugin, json);
                            }
                        }
                        break;

                    case "sendToPropertyInspector":
                        if (!string.IsNullOrEmpty(context) && _piSockets.TryGetValue(context, out var piSock) && piSock.State == WebSocketState.Open)
                        {
                            _ = SendRawJsonAsync(piSock, json);
                        }
                        break;

                    case "switchToProfile":
                        if (root.TryGetProperty("payload", out var profilePayload) &&
                            profilePayload.TryGetProperty("profile", out var profProp))
                        {
                            string? profName = profProp.GetString();
                            if (!string.IsNullOrEmpty(profName))
                            {
                                SwitchToProfileRequested?.Invoke(profName);
                            }
                        }
                        break;

                    case "setFeedback":
                        if (root.TryGetProperty("payload", out var fbPayload))
                        {
                            SetFeedbackRequested?.Invoke(context, fbPayload.GetRawText());
                        }
                        break;

                    case "setFeedbackLayout":
                        if (root.TryGetProperty("payload", out var fblPayload))
                        {
                            SetFeedbackRequested?.Invoke(context, fblPayload.GetRawText());
                        }
                        break;

                    case "setTriggerDescription":
                        // Dynamic trigger descriptions for rotary dials/encoders
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
            string json = JsonSerializer.Serialize(payload);
            await SendRawJsonAsync(socket, json);
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
                string json = JsonSerializer.Serialize(payload);
                await SendRawJsonAsync(socket, json);
            }
        }
    }

    public async Task SendSettingsAsync(string context, string? pluginUuid)
    {
        if (string.IsNullOrEmpty(pluginUuid) && !_contextToPluginMap.TryGetValue(context, out pluginUuid))
            return;

        _keySettings.TryGetValue(context, out var settings);
        _contextToActionMap.TryGetValue(context, out var actionUuid);

        int col = 0, row = 0;
        if (context.StartsWith("key_") && int.TryParse(context.AsSpan(4), out int kIdx))
        {
            col = kIdx % 5;
            row = kIdx / 5;
        }

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
                coordinates = new { column = col, row = row },
                state = 0,
                isInMultiAction = false
            }
        };

        await SendEventAsync(pluginUuid, msg);

        foreach (var (ctx, sock) in _piSockets)
        {
            if (_contextToPluginMap.TryGetValue(ctx, out var assoc) && assoc == pluginUuid && sock.State == WebSocketState.Open)
            {
                await SendRawJsonAsync(sock, JsonSerializer.Serialize(msg));
            }
        }
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

        foreach (var (ctx, sock) in _piSockets)
        {
            if (_contextToPluginMap.TryGetValue(ctx, out var assoc) && assoc == pluginUuid && sock.State == WebSocketState.Open)
            {
                await SendRawJsonAsync(sock, JsonSerializer.Serialize(msg));
            }
        }
    }

    public async Task SendRawJsonAsync(WebSocket socket, string json)
    {
        if (socket.State != WebSocketState.Open) return;

        var sendLock = _socketSendLocks.GetOrAdd(socket, _ => new SemaphoreSlim(1, 1));
        await sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch { }
        finally
        {
            sendLock.Release();
        }
    }

    public async Task SendRawJsonToPluginAsync(string pluginUuid, string json)
    {
        if (_pluginSockets.TryGetValue(pluginUuid, out var socket) && socket.State == WebSocketState.Open)
        {
            await SendRawJsonAsync(socket, json);
        }
    }

    public async Task SendDeviceDidConnectAsync(string pluginUuid, string deviceId, string deviceName, int type, int cols, int rows)
    {
        var msg = new
        {
            @event = "deviceDidConnect",
            device = deviceId,
            deviceInfo = new
            {
                name = deviceName,
                type = type,
                size = new { columns = cols, rows = rows }
            }
        };
        await SendEventAsync(pluginUuid, msg);
    }

    public async Task SendDeviceDidDisconnectAsync(string pluginUuid, string deviceId)
    {
        var msg = new
        {
            @event = "deviceDidDisconnect",
            device = deviceId
        };
        await SendEventAsync(pluginUuid, msg);
    }

    public async Task SendSystemDidWakeUpAsync(string pluginUuid)
    {
        var msg = new
        {
            @event = "systemDidWakeUp"
        };
        await SendEventAsync(pluginUuid, msg);
    }

    public async Task SendApplicationDidLaunchAsync(string pluginUuid, string application)
    {
        var msg = new
        {
            @event = "applicationDidLaunch",
            payload = new { application = application }
        };
        await SendEventAsync(pluginUuid, msg);
    }

    public async Task SendApplicationDidTerminateAsync(string pluginUuid, string application)
    {
        var msg = new
        {
            @event = "applicationDidTerminate",
            payload = new { application = application }
        };
        await SendEventAsync(pluginUuid, msg);
    }

    public async Task SendDialRotateAsync(string pluginUuid, string context, string actionUuid, int ticks, bool pressed, object? settings = null)
    {
        int col = 0, row = 0;
        var msg = new
        {
            @event = "dialRotate",
            action = actionUuid,
            context = context,
            device = "masterhub-keydeck-plus",
            payload = new
            {
                settings = settings ?? new { },
                coordinates = new { column = col, row = row },
                ticks = ticks,
                pressed = pressed,
                controller = "Encoder"
            }
        };
        await SendEventAsync(pluginUuid, msg);
    }

    public async Task SendDialPressAsync(string pluginUuid, string context, string actionUuid, bool isDown, object? settings = null)
    {
        int col = 0, row = 0;
        var msg = new
        {
            @event = isDown ? "dialDown" : "dialUp",
            action = actionUuid,
            context = context,
            device = "masterhub-keydeck-plus",
            payload = new
            {
                settings = settings ?? new { },
                coordinates = new { column = col, row = row },
                controller = "Encoder"
            }
        };
        await SendEventAsync(pluginUuid, msg);
    }

    public async Task SendTouchTapAsync(string pluginUuid, string context, string actionUuid, float x, float y, bool hold, object? settings = null)
    {
        int col = 0, row = 0;
        var msg = new
        {
            @event = "touchTap",
            action = actionUuid,
            context = context,
            device = "masterhub-keydeck-plus",
            payload = new
            {
                settings = settings ?? new { },
                coordinates = new { column = col, row = row },
                tapPos = new[] { (int)x, (int)y },
                hold = hold,
                controller = "Encoder"
            }
        };
        await SendEventAsync(pluginUuid, msg);
    }

    public async Task SendTitleParametersDidChangeAsync(string pluginUuid, string context, string actionUuid, string title, string? font = null, int? fontSize = null, string? fontColor = null)
    {
        int col = 0, row = 0;
        if (context.StartsWith("key_") && int.TryParse(context.AsSpan(4), out int kIdx))
        {
            col = kIdx % 5;
            row = kIdx / 5;
        }

        var msg = new
        {
            @event = "titleParametersDidChange",
            action = actionUuid,
            context = context,
            device = "masterhub-keydeck-15",
            payload = new
            {
                coordinates = new { column = col, row = row },
                settings = _keySettings.TryGetValue(context, out var s) ? (object)s : new { },
                state = 0,
                title = title,
                titleParameters = new
                {
                    fontFamily = font ?? "Segoe UI",
                    fontSize = fontSize ?? 12,
                    fontStyle = "Regular",
                    fontUnderline = false,
                    showTitle = !string.IsNullOrEmpty(title),
                    titleAlignment = "middle",
                    titleColor = fontColor ?? "#ffffff"
                }
            }
        };
        await SendEventAsync(pluginUuid, msg);
    }

    public async Task SendGlobalSettingsToSocketAsync(WebSocket socket, string? pluginUuid)
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

        await SendRawJsonAsync(socket, JsonSerializer.Serialize(msg));
    }

    public async Task UpdateKeySettingsAsync(string context, string pluginUuid, string actionUuid, string settingsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            var elem = doc.RootElement.Clone();
            _keySettings[context] = elem;
            _contextToPluginMap[context] = pluginUuid;
            _contextToActionMap[context] = actionUuid;

            int col = 0, row = 0;
            if (context.StartsWith("key_") && int.TryParse(context.AsSpan(4), out int kIdx))
            {
                col = kIdx % 5;
                row = kIdx / 5;
            }

            var didRecv = new
            {
                @event = "didReceiveSettings",
                action = actionUuid,
                context = context,
                device = "masterhub-keydeck-15",
                payload = new
                {
                    settings = elem,
                    coordinates = new { column = col, row = row },
                    isInMultiAction = false
                }
            };

            await SendEventAsync(pluginUuid, didRecv);

            if (_piSockets.TryGetValue(context, out var piSock) && piSock.State == WebSocketState.Open)
            {
                await SendRawJsonAsync(piSock, JsonSerializer.Serialize(didRecv));
            }

            SettingsUpdated?.Invoke(context, settingsJson);
        }
        catch { }
    }

    public async Task UpdateGlobalSettingsAsync(string pluginUuid, string globalSettingsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(globalSettingsJson);
            var elem = doc.RootElement.Clone();
            _globalSettings[pluginUuid] = elem;
            SaveGlobalSettingsToDisk();

            var didRecv = new
            {
                @event = "didReceiveGlobalSettings",
                payload = new
                {
                    settings = elem
                }
            };

            await SendEventAsync(pluginUuid, didRecv);

            foreach (var (ctx, sock) in _piSockets)
            {
                if (_contextToPluginMap.TryGetValue(ctx, out var assoc) && assoc == pluginUuid && sock.State == WebSocketState.Open)
                {
                    await SendRawJsonAsync(sock, JsonSerializer.Serialize(didRecv));
                }
            }

            GlobalSettingsUpdated?.Invoke(pluginUuid, globalSettingsJson);
        }
        catch { }
    }

    public string? GetGlobalSettings(string pluginUuid)
    {
        return _globalSettings.TryGetValue(pluginUuid, out var s) ? s.GetRawText() : null;
    }

    public string? GetKeySettings(string context)
    {
        return _keySettings.TryGetValue(context, out var s) ? s.GetRawText() : null;
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

    public async Task ClosePluginSocketAsync(string pluginUuid)
    {
        if (_pluginSockets.TryRemove(pluginUuid, out var ws))
        {
            try
            {
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                {
                    var sendLock = _socketSendLocks.GetOrAdd(ws, _ => new SemaphoreSlim(1, 1));
                    await sendLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Plugin stopping", closeCts.Token).ConfigureAwait(false);
                    }
                    catch { }
                    finally
                    {
                        sendLock.Release();
                    }
                }
            }
            catch { }
            try { ws.Dispose(); } catch { }
        }
    }

    public async Task ClosePropertyInspectorSocketAsync(string contextOrUuid)
    {
        if (_piSockets.TryRemove(contextOrUuid, out var ws))
        {
            try
            {
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                {
                    var sendLock = _socketSendLocks.GetOrAdd(ws, _ => new SemaphoreSlim(1, 1));
                    await sendLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Inspector stopping", closeCts.Token).ConfigureAwait(false);
                    }
                    catch { }
                    finally
                    {
                        sendLock.Release();
                    }
                }
            }
            catch { }
            try { ws.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            // 1. Zamknij gniazda klientów płynnie przed anulowaniem nasłuchu HTTP
            var allSockets = _pluginSockets.Values.Concat(_piSockets.Values).Distinct().ToList();
            _pluginSockets.Clear();
            _piSockets.Clear();

            foreach (var ws in allSockets)
            {
                try
                {
                    if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                    {
                        var sendLock = _socketSendLocks.GetOrAdd(ws, _ => new SemaphoreSlim(1, 1));
                        if (sendLock.Wait(150))
                        {
                            try
                            {
                                using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
                                ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Host shutdown", closeCts.Token).GetAwaiter().GetResult();
                            }
                            catch { }
                            finally
                            {
                                sendLock.Release();
                            }
                        }
                    }
                }
                catch { }
                try { ws.Dispose(); } catch { }
            }

            // 2. Anuluj pętlę roboczą i zamknij listener
            _cts?.Cancel();

            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
        }
        catch { }
    }
}
