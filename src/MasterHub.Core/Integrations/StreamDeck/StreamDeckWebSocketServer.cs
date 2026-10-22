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
                if (_piSockets.TryRemove(associatedPluginUuid, out _))
                {
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

                // Rejestracja wtyczki lub Property Inspectora
                if (root.TryGetProperty("uuid", out var uuidProp))
                {
                    string uuid = uuidProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(uuid))
                    {
                        currentUuid = uuid;
                        if (eventName == "registerPropertyInspector")
                        {
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
                            _pluginSockets[uuid] = socket;
                            PluginRegistered?.Invoke(uuid, eventName);
                            _ = FlushPendingMessagesAsync(uuid);
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
        if (socket.State == WebSocketState.Open)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }
    }

    public async Task SendRawJsonToPluginAsync(string pluginUuid, string json)
    {
        if (_pluginSockets.TryGetValue(pluginUuid, out var socket) && socket.State == WebSocketState.Open)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }
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

            foreach (var ws in _piSockets.Values)
            {
                try { ws.Dispose(); } catch { }
            }
            _piSockets.Clear();
        }
        catch { }
    }
}
