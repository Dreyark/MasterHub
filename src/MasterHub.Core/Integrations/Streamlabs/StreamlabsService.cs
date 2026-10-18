using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.Streamlabs;

/// <summary>
/// Implementacja klienta lokalnego API Streamlabs Desktop (JSON-RPC 2.0).
/// Obsługuje połączenie WebSocket na porcie 59650 (ws://127.0.0.1:59650/api/websocket z autoryzacją TcpServerService.auth)
/// oraz lokalny fallback Named Pipe (\\.\pipe\slobs), a także wstrzykiwany handler RPC do testów jednostkowych.
/// </summary>
public sealed class StreamlabsService : IStreamlabsService
{
    public const int DefaultPort = 59650;
    public const string DefaultHost = "127.0.0.1";
    private const string PipeName = "slobs";

    private readonly Func<string, string, object?[], CancellationToken, Task<JsonElement?>>? _customRpcHandler;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement?>> _pendingRequests = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _webSocket;
    private NamedPipeClientStream? _pipeStream;
    private StreamWriter? _pipeWriter;
    private StreamReader? _pipeReader;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoopTask;

    private long _nextRequestId;
    private bool _isConnected;
    private string _host = DefaultHost;
    private int _port = DefaultPort;
    private string? _activeSceneId;
    private string? _activeSceneName;
    private List<StreamlabsSceneInfo> _scenes = new();
    private bool _disposed;

    public bool IsConnected => _isConnected;
    public string Host => _host;
    public int Port => _port;
    public string? ActiveSceneId => _activeSceneId;
    public string? ActiveSceneName => _activeSceneName;
    public IReadOnlyList<StreamlabsSceneInfo> Scenes => _scenes;

    public event Action<bool>? ConnectionStateChanged;
    public event Action<string>? ActiveSceneChanged;
    public event Action<string>? LogMessage;

    public StreamlabsService(Func<string, string, object?[], CancellationToken, Task<JsonElement?>>? customRpcHandler = null)
    {
        _customRpcHandler = customRpcHandler;
    }

    public async Task<bool> ConnectAsync(
        string host = DefaultHost,
        int port = DefaultPort,
        string? apiToken = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        await DisconnectAsync().ConfigureAwait(false);

        _host = string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim();
        _port = port is > 0 and <= 65535 ? port : DefaultPort;
        string trimmedToken = (apiToken ?? string.Empty).Trim();

        // Tryb testowy z wstrzykniętym handlerem RPC
        if (_customRpcHandler != null)
        {
            try
            {
                if (!string.IsNullOrEmpty(trimmedToken))
                {
                    var authRes = await SendRpcRequestAsync("TcpServerService", "auth", new object?[] { trimmedToken }, cancellationToken).ConfigureAwait(false);
                    if (authRes.HasValue && authRes.Value.ValueKind == JsonValueKind.False)
                    {
                        LogMessage?.Invoke("[Streamlabs] Błąd autoryzacji tokenu API Streamlabs Desktop.");
                        return false;
                    }
                }

                SetConnectedState(true);
                await RefreshScenesAndActiveStateAsync(cancellationToken).ConfigureAwait(false);
                LogMessage?.Invoke($"[Streamlabs] Połączono ze Streamlabs Desktop ({_host}:{_port}). Aktywna scena: {_activeSceneName ?? "brak"}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[Streamlabs] Błąd połączenia: {ex.Message}");
                SetConnectedState(false);
                return false;
            }
        }

        // 1. Próba połączenia przez WebSocket na porcie 59650 (ws://{host}:{port}/api/websocket)
        bool connectedViaWebSocket = await TryConnectWebSocketAsync(_host, _port, trimmedToken, cancellationToken).ConfigureAwait(false);
        if (connectedViaWebSocket)
        {
            await RefreshScenesAndActiveStateAsync(cancellationToken).ConfigureAwait(false);
            await SubscribeToSceneEventsAsync(cancellationToken).ConfigureAwait(false);
            LogMessage?.Invoke($"[Streamlabs] Połączono przez WebSocket ({_host}:{_port}). Sceny: {_scenes.Count}, Aktywna: {_activeSceneName ?? "-"}");
            return true;
        }

        // 2. Fallback: Lokalne połączenie Named Pipe (\\.\pipe\slobs) na komputerze lokalnym
        if (IsLocalHost(_host))
        {
            bool connectedViaPipe = await TryConnectNamedPipeAsync(trimmedToken, cancellationToken).ConfigureAwait(false);
            if (connectedViaPipe)
            {
                await RefreshScenesAndActiveStateAsync(cancellationToken).ConfigureAwait(false);
                await SubscribeToSceneEventsAsync(cancellationToken).ConfigureAwait(false);
                LogMessage?.Invoke($"[Streamlabs] Połączono przez lokalny potok Named Pipe (\\\\.\\pipe\\{PipeName}). Sceny: {_scenes.Count}, Aktywna: {_activeSceneName ?? "-"}");
                return true;
            }
        }

        LogMessage?.Invoke($"[Streamlabs] Nie udało się połączyć ze Streamlabs Desktop ({_host}:{_port}). Upewnij się, że aplikacja jest uruchomiona.");
        await DisconnectAsync().ConfigureAwait(false);
        return false;
    }

    private async Task<bool> TryConnectWebSocketAsync(string host, int port, string apiToken, CancellationToken cancellationToken)
    {
        try
        {
            var ws = new ClientWebSocket();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(3));

            var uri = new Uri($"ws://{host}:{port}/api/websocket");
            await ws.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);

            _webSocket = ws;
            _receiveCts = new CancellationTokenSource();
            _receiveLoopTask = Task.Run(() => WebSocketReceiveLoopAsync(_receiveCts.Token));
            SetConnectedState(true);

            if (!string.IsNullOrEmpty(apiToken))
            {
                var authRes = await SendRpcRequestAsync("TcpServerService", "auth", new object?[] { apiToken }, cancellationToken).ConfigureAwait(false);
                if (authRes.HasValue && authRes.Value.ValueKind == JsonValueKind.False)
                {
                    LogMessage?.Invoke("[Streamlabs] Odrzucono token API w TcpServerService.auth.");
                    await DisconnectAsync().ConfigureAwait(false);
                    return false;
                }
            }

            return true;
        }
        catch
        {
            try { _webSocket?.Dispose(); } catch { }
            _webSocket = null;
            return false;
        }
    }

    private async Task<bool> TryConnectNamedPipeAsync(string apiToken, CancellationToken cancellationToken)
    {
        try
        {
            var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromMilliseconds(1500));

            await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);

            _pipeStream = pipe;
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            _pipeWriter = new StreamWriter(pipe, utf8NoBom) { AutoFlush = true };
            _pipeReader = new StreamReader(pipe, utf8NoBom);

            _receiveCts = new CancellationTokenSource();
            _receiveLoopTask = Task.Run(() => PipeReceiveLoopAsync(_receiveCts.Token));
            SetConnectedState(true);

            if (!string.IsNullOrEmpty(apiToken))
            {
                await SendRpcRequestAsync("TcpServerService", "auth", new object?[] { apiToken }, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch
        {
            try { _pipeStream?.Dispose(); } catch { }
            _pipeStream = null;
            _pipeWriter = null;
            _pipeReader = null;
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _receiveCts?.Cancel();
        }
        catch { }

        foreach (var kv in _pendingRequests)
        {
            kv.Value.TrySetCanceled();
        }
        _pendingRequests.Clear();

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", closeCts.Token).ConfigureAwait(false);
                }
            }
            catch { }
            finally
            {
                try { _webSocket.Dispose(); } catch { }
                _webSocket = null;
            }
        }

        if (_pipeStream != null)
        {
            try { _pipeWriter?.Dispose(); } catch { }
            try { _pipeReader?.Dispose(); } catch { }
            try { _pipeStream.Dispose(); } catch { }
            _pipeWriter = null;
            _pipeReader = null;
            _pipeStream = null;
        }

        if (_receiveLoopTask != null)
        {
            try { await _receiveLoopTask.ConfigureAwait(false); } catch { }
            _receiveLoopTask = null;
        }

        try { _receiveCts?.Dispose(); } catch { }
        _receiveCts = null;

        SetConnectedState(false);
    }

    public async Task<IReadOnlyList<StreamlabsSceneInfo>> GetScenesAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected) return Array.Empty<StreamlabsSceneInfo>();

        var result = await SendRpcRequestAsync("ScenesService", "getScenes", Array.Empty<object?>(), cancellationToken).ConfigureAwait(false);
        if (!result.HasValue || result.Value.ValueKind != JsonValueKind.Array)
        {
            return _scenes;
        }

        var parsed = new List<StreamlabsSceneInfo>();
        foreach (var item in result.Value.EnumerateArray())
        {
            string? id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            string? name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
            {
                parsed.Add(new StreamlabsSceneInfo(id.Trim(), name.Trim()));
            }
        }

        _scenes = parsed;
        return _scenes;
    }

    public async Task<string?> GetActiveSceneAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected) return null;

        var result = await SendRpcRequestAsync("ScenesService", "activeScene", Array.Empty<object?>(), cancellationToken).ConfigureAwait(false);
        if (result.HasValue && result.Value.ValueKind == JsonValueKind.Object)
        {
            string? id = result.Value.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            string? name = result.Value.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
            UpdateActiveSceneInternal(id, name);
            return _activeSceneName;
        }

        return _activeSceneName;
    }

    public async Task<bool> SwitchSceneAsync(string sceneNameOrId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneNameOrId))
        {
            LogMessage?.Invoke("[Streamlabs] Nie podano nazwy ani identyfikatora sceny.");
            return false;
        }

        if (!_isConnected)
        {
            LogMessage?.Invoke("[Streamlabs] Brak aktywnego połączenia ze Streamlabs Desktop.");
            return false;
        }

        string target = sceneNameOrId.Trim();

        // Jeśli lista scen jest pusta lub nie zawiera szukanej sceny, odśwież listę z API
        var matchedScene = FindMatchingScene(target);
        if (matchedScene == null)
        {
            await GetScenesAsync(cancellationToken).ConfigureAwait(false);
            matchedScene = FindMatchingScene(target);
        }

        string sceneIdToActivate = matchedScene?.Id ?? target;
        string displaySceneName = matchedScene?.Name ?? target;

        var response = await SendRpcRequestAsync(
            "ScenesService",
            "makeSceneActive",
            new object?[] { sceneIdToActivate },
            cancellationToken).ConfigureAwait(false);

        if (!response.HasValue)
        {
            LogMessage?.Invoke($"[Streamlabs] Brak odpowiedzi na żądanie zmiany sceny '{displaySceneName}'.");
            return false;
        }

        bool switched = response.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Object => true,
            JsonValueKind.False => false,
            _ => false
        };

        if (switched)
        {
            UpdateActiveSceneInternal(sceneIdToActivate, displaySceneName);
            LogMessage?.Invoke($"[Streamlabs] Przełączono aktywną scenę na: {displaySceneName}");
            return true;
        }

        LogMessage?.Invoke($"[Streamlabs] Streamlabs Desktop odrzucił zmianę sceny na '{displaySceneName}' (ID: {sceneIdToActivate}).");
        return false;
    }

    private StreamlabsSceneInfo? FindMatchingScene(string sceneNameOrId)
    {
        return _scenes.FirstOrDefault(s =>
            string.Equals(s.Name, sceneNameOrId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Id, sceneNameOrId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RefreshScenesAndActiveStateAsync(CancellationToken cancellationToken)
    {
        await GetScenesAsync(cancellationToken).ConfigureAwait(false);
        await GetActiveSceneAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SubscribeToSceneEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SendRpcRequestAsync("ScenesService", "sceneSwitched", Array.Empty<object?>(), cancellationToken).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task<JsonElement?> SendRpcRequestAsync(
        string resource,
        string method,
        object?[] args,
        CancellationToken cancellationToken)
    {
        if (_customRpcHandler != null)
        {
            return await _customRpcHandler(resource, method, args, cancellationToken).ConfigureAwait(false);
        }

        if (!_isConnected) return null;

        long id = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;

        var payloadObj = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = new
            {
                resource,
                args
            }
        };

        string json = JsonSerializer.Serialize(payloadObj);

        try
        {
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
                }
                else if (_pipeWriter != null && _pipeStream is { IsConnected: true })
                {
                    await _pipeWriter.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _pendingRequests.TryRemove(id, out _);
                    return null;
                }
            }
            finally
            {
                _sendLock.Release();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
            using var reg = timeoutCts.Token.Register(() => tcs.TrySetResult(null));

            return await tcs.Task.ConfigureAwait(false);
        }
        catch
        {
            _pendingRequests.TryRemove(id, out _);
            return null;
        }
    }

    private async Task WebSocketReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket is { State: WebSocketState.Open })
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage)
                {
                    string message = sb.ToString();
                    sb.Clear();
                    ProcessIncomingJsonMessage(message);
                }
            }
        }
        catch { }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetConnectedState(false);
            }
        }
    }

    private async Task PipeReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _pipeReader != null && _pipeStream is { IsConnected: true })
            {
                string? line = await _pipeReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    ProcessIncomingJsonMessage(line);
                }
            }
        }
        catch { }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetConnectedState(false);
            }
        }
    }

    private void ProcessIncomingJsonMessage(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // 1. Odpowiedź na żądanie RPC po ID
            if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out long reqId))
            {
                if (_pendingRequests.TryRemove(reqId, out var tcs))
                {
                    if (root.TryGetProperty("error", out var errProp) && errProp.ValueKind != JsonValueKind.Null)
                    {
                        tcs.TrySetResult(null);
                    }
                    else if (root.TryGetProperty("result", out var resProp))
                    {
                        tcs.TrySetResult(resProp.Clone());
                    }
                    else
                    {
                        tcs.TrySetResult(null);
                    }
                }
                return;
            }

            // 2. Zdarzenie strumieniowe JSON-RPC (np. ScenesService.sceneSwitched)
            if (root.TryGetProperty("result", out var eventRes) && eventRes.ValueKind == JsonValueKind.Object)
            {
                string? type = eventRes.TryGetProperty("_type", out var typeProp) ? typeProp.GetString() : null;
                string? resourceId = eventRes.TryGetProperty("resourceId", out var resIdProp) ? resIdProp.GetString() : null;

                if (string.Equals(type, "EVENT", StringComparison.OrdinalIgnoreCase) &&
                    resourceId != null &&
                    resourceId.Contains("sceneSwitched", StringComparison.OrdinalIgnoreCase) &&
                    eventRes.TryGetProperty("data", out var dataProp) &&
                    dataProp.ValueKind == JsonValueKind.Object)
                {
                    string? id = dataProp.TryGetProperty("id", out var sId) ? sId.GetString() : null;
                    string? name = dataProp.TryGetProperty("name", out var sName) ? sName.GetString() : null;
                    UpdateActiveSceneInternal(id, name);
                }
            }
        }
        catch { }
    }

    private void UpdateActiveSceneInternal(string? id, string? name)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            _activeSceneId = id.Trim();
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            string trimmedName = name.Trim();
            bool changed = !string.Equals(_activeSceneName, trimmedName, StringComparison.Ordinal);
            _activeSceneName = trimmedName;
            if (changed)
            {
                ActiveSceneChanged?.Invoke(trimmedName);
            }
        }
    }

    private void SetConnectedState(bool connected)
    {
        if (_isConnected != connected)
        {
            _isConnected = connected;
            ConnectionStateChanged?.Invoke(connected);
        }
    }

    private static bool IsLocalHost(string host)
    {
        return string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { DisconnectAsync().GetAwaiter().GetResult(); } catch { }
        _sendLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }
}
