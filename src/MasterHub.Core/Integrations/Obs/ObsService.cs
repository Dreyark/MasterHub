namespace MasterHub.Core.Integrations.Obs;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// W pełni asynchroniczny klient OBS WebSocket v5 (port domyślny 4455) oparty na standardowym ClientWebSocket (.NET BCL).
/// Obsługuje protokół handshake OBS v5 (Hello -> Identify -> Identified) z haszowaniem SHA-256,
/// kontrolę streamu i nagrywania (Toggle/Start/Stop) oraz asynchroniczne powiadomienia o zdarzeniach.
/// </summary>
public sealed class ObsService : IObsService
{
    private static readonly JsonElement s_emptyObject = JsonDocument.Parse("{}").RootElement;

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement?>> _pendingRequests = new();

    private bool _isConnected;
    private bool _isDisposed;
    private bool _isStreaming;
    private bool _isRecording;
    private bool _isVirtualCamActive;
    private bool _isReplayBufferActive;

    public bool IsConnected => _isConnected && _webSocket?.State == WebSocketState.Open;
    public string Host { get; private set; } = "localhost";
    public int Port { get; private set; } = 4455;
    public string? ObsWebSocketVersion { get; private set; }
    public string? LastError { get; private set; }

    public bool IsStreaming
    {
        get => _isStreaming;
        private set
        {
            if (_isStreaming != value)
            {
                _isStreaming = value;
                StreamStateChanged?.Invoke(value);
            }
        }
    }

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (_isRecording != value)
            {
                _isRecording = value;
                RecordStateChanged?.Invoke(value);
            }
        }
    }

    public bool IsVirtualCamActive
    {
        get => _isVirtualCamActive;
        private set
        {
            if (_isVirtualCamActive != value)
            {
                _isVirtualCamActive = value;
                VirtualCamStateChanged?.Invoke(value);
            }
        }
    }

    public bool IsReplayBufferActive
    {
        get => _isReplayBufferActive;
        private set
        {
            if (_isReplayBufferActive != value)
            {
                _isReplayBufferActive = value;
                ReplayBufferStateChanged?.Invoke(value);
            }
        }
    }

    private string? _currentProgramSceneName;
    public string? CurrentProgramSceneName
    {
        get => _currentProgramSceneName;
        private set
        {
            if (_currentProgramSceneName != value)
            {
                _currentProgramSceneName = value;
                if (!string.IsNullOrEmpty(value))
                {
                    CurrentProgramSceneChanged?.Invoke(value);
                }
            }
        }
    }

    private bool _isStudioModeActive;
    public bool IsStudioModeActive
    {
        get => _isStudioModeActive;
        private set
        {
            if (_isStudioModeActive != value)
            {
                _isStudioModeActive = value;
                StudioModeStateChanged?.Invoke(value);
            }
        }
    }

    public event Action<bool>? ConnectionStateChanged;
    public event Action<bool>? StreamStateChanged;
    public event Action<bool>? RecordStateChanged;
    public event Action<bool>? VirtualCamStateChanged;
    public event Action<bool>? ReplayBufferStateChanged;
    public event Action<string>? ReplayBufferSaved;
    public event Action<string>? CurrentProgramSceneChanged;
    public event Action<bool>? StudioModeStateChanged;
    public event Action<string, JsonElement>? EventReceived;

    /// <summary>
    /// Oblicza skrót uwierzytelniania protokołu OBS WebSocket v5 wg oficjalnej specyfikacji:
    /// 1. secretHash = Base64(SHA256(password + salt))
    /// 2. authResponse = Base64(SHA256(secretHash + challenge))
    /// </summary>
    public static string ComputeAuthResponse(string password, string salt, string challenge)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        ArgumentNullException.ThrowIfNull(challenge);

        byte[] secretHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(password + salt));
        string secretHashBase64 = Convert.ToBase64String(secretHashBytes);

        byte[] authBytes = SHA256.HashData(Encoding.UTF8.GetBytes(secretHashBase64 + challenge));
        return Convert.ToBase64String(authBytes);
    }

    /// <inheritdoc />
    public async Task<bool> ConnectAsync(
        string host = "localhost",
        int port = 4455,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                return true;
            }

            await DisconnectInternalAsync();

            string cleanHost = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
            if (cleanHost.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            {
                cleanHost = cleanHost[5..];
            }
            else if (cleanHost.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                cleanHost = cleanHost[6..];
            }
            else if (cleanHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                cleanHost = cleanHost[7..];
            }

            if (cleanHost.Contains(':'))
            {
                var parts = cleanHost.Split(':');
                cleanHost = parts[0];
                if (int.TryParse(parts[1], out int parsedPort) && parsedPort > 0)
                {
                    port = parsedPort;
                }
            }

            Host = cleanHost;
            Port = port > 0 ? port : 4455;

            _cts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            var ct = linkedCts.Token;

            var tcsIdentified = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            LastError = null;
            try
            {
                _webSocket = new ClientWebSocket();
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);

                var uri = new Uri($"ws://{Host}:{Port}");
                using var connectTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var connectLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, connectTimeoutCts.Token);
                await _webSocket.ConnectAsync(uri, connectLinked.Token);

                _receiveTask = Task.Run(() => ReceiveLoopAsync(password, tcsIdentified, _cts.Token), _cts.Token);

                using var handshakeTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                using var timeoutRegistration = handshakeTimeoutCts.Token.Register(() => tcsIdentified.TrySetResult(false));
                using var callerCancellationRegistration = ct.Register(() => tcsIdentified.TrySetCanceled(ct));

                bool success = await tcsIdentified.Task;
                if (!success)
                {
                    if (string.IsNullOrEmpty(LastError))
                    {
                        LastError = "Nie otrzymano potwierdzenia autoryzacji (Identified) od serwera OBS.";
                    }
                    await DisconnectInternalAsync();
                    return false;
                }

                _isConnected = true;
                LastError = null;
                ConnectionStateChanged?.Invoke(true);

                // Asynchroniczne odpytanie o aktualny stan streamu, nagrywania, kamery, bufora i sceny w OBS
                _ = RefreshStreamStatusAsync(CancellationToken.None);
                _ = RefreshRecordStatusAsync(CancellationToken.None);
                _ = RefreshVirtualCamStatusAsync(CancellationToken.None);
                _ = RefreshReplayBufferStatusAsync(CancellationToken.None);
                _ = GetCurrentProgramSceneAsync(CancellationToken.None);
                _ = GetStudioModeEnabledAsync(CancellationToken.None);

                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
            {
                LastError = $"Przekroczono limit czasu połączenia z {Host}:{Port} (brak odpowiedzi).";
                await DisconnectInternalAsync();
                return false;
            }
            catch (System.Net.WebSockets.WebSocketException wsex)
            {
                LastError = $"Błąd połączenia WebSocket: {wsex.Message} (sprawdź adres {Host}:{Port}).";
                await DisconnectInternalAsync();
                return false;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(LastError))
                {
                    LastError = $"Błąd połączenia: {ex.Message}";
                }
                await DisconnectInternalAsync();
                return false;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            await DisconnectInternalAsync();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task DisconnectInternalAsync()
    {
        bool wasConnected = _isConnected;
        _isConnected = false;

        IsStreaming = false;
        IsRecording = false;
        IsVirtualCamActive = false;
        IsReplayBufferActive = false;
        CurrentProgramSceneName = null;
        IsStudioModeActive = false;

        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // Ignorujemy błędy unieważnienia tokena
        }

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.CloseReceived)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", closeCts.Token);
                }
            }
            catch
            {
                // Ignorujemy błędy zamykania gniazda
            }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
            }
        }

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch
            {
                // Ignorujemy wyjątki zakończenia wątku odbioru
            }
            _receiveTask = null;
        }

        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingRequests.Clear();

        _cts?.Dispose();
        _cts = null;

        if (wasConnected)
        {
            ConnectionStateChanged?.Invoke(false);
        }
    }

    /// <inheritdoc />
    public async Task<JsonElement?> SendRequestAsync(string requestType, object? requestData = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!IsConnected || _webSocket == null)
        {
            return null;
        }

        string requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = tcs;

        try
        {
            object dObject = requestData != null
                ? new { requestType, requestId, requestData }
                : new { requestType, requestId };

            var payload = new
            {
                op = 6, // OpCode 6: Request
                d = dObject
            };

            byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);

            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                if (_webSocket.State != WebSocketState.Open)
                {
                    return null;
                }
                await _webSocket.SendAsync(new ArraySegment<byte>(jsonBytes), WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                _sendLock.Release();
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using var reg = linked.Token.Register(() => tcs.TrySetResult(null));

            return await tcs.Task;
        }
        catch
        {
            return null;
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ToggleStreamAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("ToggleStream", null, cancellationToken);
        if (response != null && response.Value.TryGetProperty("outputActive", out var activeProp))
        {
            IsStreaming = activeProp.GetBoolean();
            return true;
        }
        return response != null;
    }

    /// <inheritdoc />
    public async Task<bool> StartStreamAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("StartStream", null, cancellationToken);
        if (response != null)
        {
            IsStreaming = true;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> StopStreamAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("StopStream", null, cancellationToken);
        if (response != null)
        {
            IsStreaming = false;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> ToggleRecordAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("ToggleRecord", null, cancellationToken);
        if (response != null && response.Value.TryGetProperty("outputActive", out var activeProp))
        {
            IsRecording = activeProp.GetBoolean();
            return true;
        }
        return response != null;
    }

    /// <inheritdoc />
    public async Task<bool> StartRecordAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("StartRecord", null, cancellationToken);
        if (response != null)
        {
            IsRecording = true;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> StopRecordAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("StopRecord", null, cancellationToken);
        if (response != null)
        {
            IsRecording = false;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> RefreshStreamStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetStreamStatus", null, cancellationToken);
        if (response != null && response.Value.TryGetProperty("outputActive", out var activeProp))
        {
            IsStreaming = activeProp.GetBoolean();
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> RefreshRecordStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetRecordStatus", null, cancellationToken);
        if (response != null && response.Value.TryGetProperty("outputActive", out var activeProp))
        {
            IsRecording = activeProp.GetBoolean();
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> ToggleVirtualCamAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("ToggleVirtualCam", null, cancellationToken);
        if (response != null && response.Value.TryGetProperty("outputActive", out var activeProp))
        {
            IsVirtualCamActive = activeProp.GetBoolean();
            return true;
        }
        return response != null;
    }

    /// <inheritdoc />
    public async Task<bool> StartVirtualCamAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("StartVirtualCam", null, cancellationToken);
        if (response != null)
        {
            IsVirtualCamActive = true;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> StopVirtualCamAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("StopVirtualCam", null, cancellationToken);
        if (response != null)
        {
            IsVirtualCamActive = false;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> RefreshVirtualCamStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetVirtualCamStatus", null, cancellationToken);
        if (response != null && response.Value.TryGetProperty("outputActive", out var activeProp))
        {
            IsVirtualCamActive = activeProp.GetBoolean();
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> ToggleReplayBufferAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("ToggleReplayBuffer", null, cancellationToken);
        if (response != null && response.Value.TryGetProperty("outputActive", out var activeProp))
        {
            IsReplayBufferActive = activeProp.GetBoolean();
            return true;
        }
        return response != null;
    }

    /// <inheritdoc />
    public async Task<bool> StartReplayBufferAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("StartReplayBuffer", null, cancellationToken);
        if (response != null)
        {
            IsReplayBufferActive = true;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> StopReplayBufferAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("StopReplayBuffer", null, cancellationToken);
        if (response != null)
        {
            IsReplayBufferActive = false;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> SaveReplayBufferAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("SaveReplayBuffer", null, cancellationToken);
        return response != null;
    }

    /// <inheritdoc />
    public async Task<bool> RefreshReplayBufferStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetReplayBufferStatus", null, cancellationToken);
        if (response != null && response.Value.TryGetProperty("outputActive", out var activeProp))
        {
            IsReplayBufferActive = activeProp.GetBoolean();
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> SetCurrentProgramSceneAsync(string sceneName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return false;
        var response = await SendRequestAsync("SetCurrentProgramScene", new { sceneName }, cancellationToken);
        if (response != null)
        {
            CurrentProgramSceneName = sceneName;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<string?> GetCurrentProgramSceneAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetCurrentProgramScene", null, cancellationToken);
        if (response != null && response.Value.TryGetProperty("currentProgramSceneName", out var sceneProp))
        {
            string? name = sceneProp.GetString();
            CurrentProgramSceneName = name;
            return name;
        }
        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetSceneListAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetSceneList", null, cancellationToken);
        if (response != null && response.Value.TryGetProperty("scenes", out var scenesProp) && scenesProp.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in scenesProp.EnumerateArray())
            {
                if (item.TryGetProperty("sceneName", out var nameProp))
                {
                    string? sName = nameProp.GetString();
                    if (!string.IsNullOrEmpty(sName))
                    {
                        list.Add(sName);
                    }
                }
            }
            if (response.Value.TryGetProperty("currentProgramSceneName", out var curSceneProp))
            {
                CurrentProgramSceneName = curSceneProp.GetString();
            }
            return list;
        }
        return Array.Empty<string>();
    }

    /// <inheritdoc />
    public async Task<bool> ToggleStudioModeAsync(CancellationToken cancellationToken = default)
    {
        bool target = !IsStudioModeActive;
        return await SetStudioModeEnabledAsync(target, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SetStudioModeEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("SetStudioModeEnabled", new { studioModeEnabled = enabled }, cancellationToken);
        if (response != null)
        {
            IsStudioModeActive = enabled;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> GetStudioModeEnabledAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("GetStudioModeEnabled", null, cancellationToken);
        if (response != null && response.Value.TryGetProperty("studioModeEnabled", out var smProp))
        {
            bool enabled = smProp.GetBoolean();
            IsStudioModeActive = enabled;
            return enabled;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> TriggerStudioModeTransitionAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("TriggerStudioModeTransition", null, cancellationToken);
        return response != null;
    }

    private async Task ReceiveLoopAsync(string? password, TaskCompletionSource<bool> tcsIdentified, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (_webSocket?.CloseStatus == (WebSocketCloseStatus)4009)
                    {
                        LastError = "Błędne hasło! Serwer OBS odrzucił uwierzytelnienie (kod 4009: Authentication failed).";
                    }
                    else if (_webSocket?.CloseStatus == (WebSocketCloseStatus)4008)
                    {
                        LastError = "Serwer OBS wymaga hasła (kod 4008: Authentication required), a hasło nie zostało podane.";
                    }
                    else if (_webSocket?.CloseStatus != null)
                    {
                        LastError = $"Serwer OBS zamknął połączenie (kod {(int)_webSocket.CloseStatus}: {_webSocket.CloseStatusDescription}).";
                    }
                    break;
                }

                ms.Position = 0;
                using var doc = await JsonDocument.ParseAsync(ms, cancellationToken: ct);
                var root = doc.RootElement;

                if (!root.TryGetProperty("op", out var opProp))
                {
                    continue;
                }

                int op = opProp.GetInt32();
                var d = root.TryGetProperty("d", out var dProp) ? dProp : default;

                switch (op)
                {
                    case 0: // OpCode 0: Hello
                        await HandleHelloAsync(d, password, tcsIdentified, ct);
                        break;

                    case 2: // OpCode 2: Identified
                        tcsIdentified.TrySetResult(true);
                        break;

                    case 5: // OpCode 5: Event
                        if (d.ValueKind == JsonValueKind.Object && d.TryGetProperty("eventType", out var eventTypeProp))
                        {
                            string? eventType = eventTypeProp.GetString();
                            if (!string.IsNullOrEmpty(eventType) && d.TryGetProperty("eventData", out var eventDataProp))
                            {
                                if (string.Equals(eventType, "StreamStateChanged", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (eventDataProp.TryGetProperty("outputActive", out var activeProp))
                                    {
                                        IsStreaming = activeProp.GetBoolean();
                                    }
                                }
                                else if (string.Equals(eventType, "RecordStateChanged", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (eventDataProp.TryGetProperty("outputActive", out var activeProp))
                                    {
                                        IsRecording = activeProp.GetBoolean();
                                    }
                                }
                                else if (string.Equals(eventType, "VirtualCamStateChanged", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(eventType, "VirtualcamStateChanged", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (eventDataProp.TryGetProperty("outputActive", out var activeProp))
                                    {
                                        IsVirtualCamActive = activeProp.GetBoolean();
                                    }
                                }
                                else if (string.Equals(eventType, "ReplayBufferStateChanged", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (eventDataProp.TryGetProperty("outputActive", out var activeProp))
                                    {
                                        IsReplayBufferActive = activeProp.GetBoolean();
                                    }
                                }
                                else if (string.Equals(eventType, "ReplayBufferSaved", StringComparison.OrdinalIgnoreCase))
                                {
                                    string savedPath = eventDataProp.TryGetProperty("savedReplayPath", out var pathProp) ? pathProp.GetString() ?? "" : "";
                                    ReplayBufferSaved?.Invoke(savedPath);
                                }
                                else if (string.Equals(eventType, "CurrentProgramSceneChanged", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (eventDataProp.TryGetProperty("sceneName", out var sceneNameProp))
                                    {
                                        CurrentProgramSceneName = sceneNameProp.GetString();
                                    }
                                }
                                else if (string.Equals(eventType, "StudioModeStateChanged", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (eventDataProp.TryGetProperty("studioModeEnabled", out var smProp))
                                    {
                                        IsStudioModeActive = smProp.GetBoolean();
                                    }
                                }

                                EventReceived?.Invoke(eventType, eventDataProp.Clone());
                            }
                        }
                        break;

                    case 7: // OpCode 7: RequestResponse
                        if (d.ValueKind == JsonValueKind.Object && d.TryGetProperty("requestId", out var reqIdProp))
                        {
                            string? respReqId = reqIdProp.GetString();
                            if (!string.IsNullOrEmpty(respReqId) && _pendingRequests.TryGetValue(respReqId, out var pendingTcs))
                            {
                                bool isSuccess = true;
                                if (d.TryGetProperty("requestStatus", out var statusProp) &&
                                    statusProp.TryGetProperty("result", out var resultProp))
                                {
                                    isSuccess = resultProp.GetBoolean();
                                }

                                if (isSuccess)
                                {
                                    if (d.TryGetProperty("responseData", out var respDataProp))
                                    {
                                        pendingTcs.TrySetResult(respDataProp.Clone());
                                    }
                                    else
                                    {
                                        pendingTcs.TrySetResult(s_emptyObject);
                                    }
                                }
                                else
                                {
                                    pendingTcs.TrySetResult(null);
                                }
                            }
                        }
                        break;
                }
            }
        }
        catch
        {
            // Zakończenie pętli odbioru lub błąd połączenia
        }
        finally
        {
            tcsIdentified.TrySetResult(false);

            bool wasConnected = _isConnected;
            _isConnected = false;

            IsStreaming = false;
            IsRecording = false;
            IsVirtualCamActive = false;
            IsReplayBufferActive = false;
            CurrentProgramSceneName = null;
            IsStudioModeActive = false;

            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetCanceled();
            }
            _pendingRequests.Clear();

            if (wasConnected)
            {
                ConnectionStateChanged?.Invoke(false);
            }
        }
    }

    private async Task HandleHelloAsync(JsonElement d, string? password, TaskCompletionSource<bool> tcsIdentified, CancellationToken ct)
    {
        if (d.ValueKind != JsonValueKind.Object)
        {
            tcsIdentified.TrySetResult(false);
            return;
        }

        if (d.TryGetProperty("obsWebSocketVersion", out var verProp))
        {
            ObsWebSocketVersion = verProp.GetString();
        }

        string? authString = null;
        if (d.TryGetProperty("authentication", out var authProp) && authProp.ValueKind == JsonValueKind.Object)
        {
            string? challenge = authProp.TryGetProperty("challenge", out var cProp) ? cProp.GetString() : null;
            string? salt = authProp.TryGetProperty("salt", out var sProp) ? sProp.GetString() : null;

            if (string.IsNullOrEmpty(challenge) || string.IsNullOrEmpty(salt))
            {
                tcsIdentified.TrySetResult(false);
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                LastError = "Serwer OBS wymaga hasła, a pole hasła w konfiguracji MasterHub jest puste.";
                tcsIdentified.TrySetResult(false);
                return;
            }

            authString = ComputeAuthResponse(password, salt, challenge);
        }

        object identifyPayload = authString != null
            ? new
            {
                op = 1,
                d = new
                {
                    rpcVersion = 1,
                    authentication = authString,
                    eventSubscriptions = 2047
                }
            }
            : new
            {
                op = 1,
                d = new
                {
                    rpcVersion = 1,
                    eventSubscriptions = 2047
                }
            };

        byte[] identifyBytes = JsonSerializer.SerializeToUtf8Bytes(identifyPayload);

        await _sendLock.WaitAsync(ct);
        try
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.SendAsync(new ArraySegment<byte>(identifyBytes), WebSocketMessageType.Text, true, ct);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        DisconnectInternalAsync().GetAwaiter().GetResult();
        _connectionLock.Dispose();
        _sendLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await DisconnectInternalAsync();
        _connectionLock.Dispose();
        _sendLock.Dispose();
    }
}
