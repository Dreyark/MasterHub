using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Profiles;
using SIPSorcery.Net;

namespace MasterHub.Core.Integrations.JetKvm;

public class JetKvmService : IJetKvmService
{
    private JetKvmSettingsModel _settings = new();
    private HttpClient? _httpClient;
    private CookieContainer _cookieContainer = new();
    private string? _authToken;
    private readonly object _lock = new();

    private Timer? _pollTimer;
    private bool _isPolling;
    private int _rpcRequestId = 0;

    // WebRTC DataChannel RPC
    private RTCPeerConnection? _peerConnection;
    private RTCDataChannel? _rpcDataChannel;
    private ClientWebSocket? _signalingWs;
    private CancellationTokenSource? _signalingCts;
    private readonly SemaphoreSlim _rtcLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingRpc = new();
    private DateTime _nextJetKvmPollAllowedUtc = DateTime.MinValue;

    public bool IsConnected { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public bool LastPowerState { get; private set; }
    public bool LastHddState { get; private set; }
    public string? DeviceId { get; private set; }
    public string? AuthMode { get; private set; }
    public JetKvmPowerState CurrentState => new(LastPowerState, LastHddState, IsConnected);

    public event Action<bool>? ConnectionStateChanged;
    public event Action<JetKvmPowerState>? PowerStateChanged;

    public JetKvmService()
    {
    }

    public void Configure(JetKvmSettingsModel settings)
    {
        lock (_lock)
        {
            _settings = settings ?? new JetKvmSettingsModel();
            CloseWebRtc();
            RebuildHttpClient();

            bool isConfigured = !string.IsNullOrWhiteSpace(_settings.Host) &&
                                (!_settings.Host.Equals("192.168.1.100", StringComparison.OrdinalIgnoreCase) ||
                                 !string.IsNullOrEmpty(_settings.LocalPassword) ||
                                 !string.IsNullOrEmpty(_settings.AuthToken));

            if (_settings.AutoConnect && isConfigured)
            {
                StartMonitoring();
            }
            else
            {
                StopMonitoring();
            }
        }
    }

    private void RebuildHttpClient()
    {
        _httpClient?.Dispose();
        _cookieContainer = new CookieContainer();
        _authToken = null;
        IsAuthenticated = false;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            CookieContainer = _cookieContainer,
            UseCookies = true
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    private Uri BuildBaseUri()
    {
        string host = _settings.Host?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "192.168.1.100";
        }

        bool hasScheme = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         host.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        if (hasScheme)
        {
            return new Uri(host.TrimEnd('/'));
        }

        string scheme = _settings.UseHttps ? "https" : "http";
        int port = _settings.Port > 0 ? _settings.Port : (_settings.UseHttps ? 443 : 80);

        if (host.Contains(':'))
        {
            return new Uri($"{scheme}://{host.TrimEnd('/')}");
        }

        return new Uri($"{scheme}://{host}:{port}");
    }

    private async Task<(bool Success, string Message)> AuthenticateAsync(bool forceReauth = false)
    {
        if (!forceReauth && IsAuthenticated && (!string.IsNullOrEmpty(_authToken) || AuthMode == "noPassword"))
        {
            return (true, "OK");
        }

        if (_httpClient == null) RebuildHttpClient();
        var baseUri = BuildBaseUri();

        // KROK 1: Sprawdź dostępność hosta przez publiczny endpoint GET /device/status
        try
        {
            using var statusReq = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/device/status"));
            using var statusResp = await _httpClient!.SendAsync(statusReq);
            if (!statusResp.IsSuccessStatusCode)
            {
                return (false, $"Urządzenie pod adresem {baseUri} zwróciło kod błędu {(int)statusResp.StatusCode} ({statusResp.ReasonPhrase}). Upewnij się, że podany adres wskazuje na JetKVM.");
            }
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Nie można połączyć się z urządzeniem ({baseUri.Authority}). Sprawdź czy JetKVM jest włączony i podłączony do sieci LAN oraz czy IP i Port są poprawne. ({ex.Message})");
        }
        catch (TaskCanceledException)
        {
            return (false, $"Przekroczono limit czasu oczekiwania na połączenie z {baseUri.Authority}. Sprawdź połączenie z siecią.");
        }
        catch (Exception ex)
        {
            return (false, $"Błąd połączenia z {baseUri.Authority}: {ex.Message}");
        }

        // KROK 2: Sprawdź autoryzację i tryb urządzenia (GET /device)
        try
        {
            using var devReq = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/device"));
            AttachAuth(devReq);
            using var devResp = await _httpClient!.SendAsync(devReq);

            if (devResp.IsSuccessStatusCode)
            {
                string devJson = await devResp.Content.ReadAsStringAsync();
                ParseDeviceDetails(devJson);
                IsAuthenticated = true;
                return (true, "OK");
            }

            if (devResp.StatusCode == HttpStatusCode.Unauthorized)
            {
                IsAuthenticated = false;
                _authToken = null;

                // Urządzenie wymaga hasła logowania
                string password = _settings.LocalPassword?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(password))
                {
                    return (false, "Urządzenie JetKVM jest zabezpieczone hasłem. Wpisz hasło urządzenia w polu poniżej i spróbuj ponownie.");
                }

                // KROK 3: Zaloguj się przez POST /auth/login-local
                var loginPayload = JsonSerializer.Serialize(new { password });
                using var loginReq = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/auth/login-local"));
                loginReq.Content = new StringContent(loginPayload, Encoding.UTF8, "application/json");

                using var loginResp = await _httpClient!.SendAsync(loginReq);
                if (loginResp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return (false, "Niepoprawne hasło do JetKVM (Błąd 401). Wpisz prawidłowe hasło logowania do urządzenia.");
                }
                if (loginResp.StatusCode == (HttpStatusCode)429)
                {
                    return (false, "Zbyt wiele nieudanych prób logowania (Rate limit JetKVM). Odczekaj chwilę przed kolejną próbą.");
                }
                if (!loginResp.IsSuccessStatusCode)
                {
                    return (false, $"Błąd logowania JetKVM: {(int)loginResp.StatusCode} {loginResp.ReasonPhrase}");
                }

                // Odczytaj Set-Cookie: authToken=...
                ExtractAuthTokenFromResponse(loginResp, baseUri);

                // Pobierz szczegóły urządzenia po zalogowaniu
                using var devAuthReq = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/device"));
                AttachAuth(devAuthReq);
                using var devAuthResp = await _httpClient!.SendAsync(devAuthReq);
                if (devAuthResp.IsSuccessStatusCode)
                {
                    string devJson = await devAuthResp.Content.ReadAsStringAsync();
                    ParseDeviceDetails(devJson);
                }

                IsAuthenticated = true;
                return (true, "OK");
            }

            return (false, $"Nieoczekiwana odpowiedź /device: {(int)devResp.StatusCode} {devResp.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            return (false, $"Błąd autoryzacji: {ex.Message}");
        }
    }

    private void ExtractAuthTokenFromResponse(HttpResponseMessage response, Uri baseUri)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var c in cookies)
            {
                var match = Regex.Match(c, @"authToken=([^;]+)");
                if (match.Success)
                {
                    _authToken = match.Groups[1].Value;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(_authToken))
        {
            var cookieCollection = _cookieContainer.GetCookies(baseUri);
            var tokenCookie = cookieCollection["authToken"];
            if (tokenCookie != null)
            {
                _authToken = tokenCookie.Value;
            }
        }
    }

    private void AttachAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_authToken))
        {
            request.Headers.Add("Cookie", $"authToken={_authToken}");
        }
    }

    private void ParseDeviceDetails(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("deviceId", out var didProp))
            {
                DeviceId = didProp.GetString();
            }
            if (doc.RootElement.TryGetProperty("authMode", out var amProp))
            {
                AuthMode = amProp.GetString();
            }
        }
        catch { }
    }

    public async Task<bool> TestConnectionAsync()
    {
        var (ok, _) = await TestConnectionDetailedAsync();
        return ok;
    }

    public async Task<(bool Success, string Message)> TestConnectionDetailedAsync()
    {
        var authRes = await AuthenticateAsync(forceReauth: true);
        if (!authRes.Success)
        {
            UpdateState(false, LastPowerState, LastHddState);
            return authRes;
        }

        UpdateState(true, LastPowerState, LastHddState);

        // Próba odpytania stanu ATX przez kanał WebRTC RPC
        string atxInfo = "";
        try
        {
            var (atxOk, pwr, hdd) = await QueryAtxStateViaRpcAsync();
            if (atxOk)
            {
                UpdateState(true, pwr, hdd);
                atxInfo = $" • Zasilanie: {(pwr ? "WŁĄCZONE (ON)" : "WYŁĄCZONE (OFF)")}{(hdd ? ", Dysk: Aktywny" : "")}";
            }
            else
            {
                atxInfo = " • Urządzenie aktywne (Moduł zasilania ATX w trybie oczekiwania)";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JetKvmService] Informacja ATX: {ex.Message}");
            atxInfo = " • Gotowy do sterowania";
        }

        string idPart = !string.IsNullOrEmpty(DeviceId) ? $" (ID: {DeviceId})" : "";
        string modePart = !string.IsNullOrEmpty(AuthMode) ? $" [Tryb: {AuthMode}]" : "";
        return (true, $"Połączono z JetKVM!{idPart}{modePart}{atxInfo}");
    }

    public Task DisconnectAsync()
    {
        lock (_lock)
        {
            StopMonitoring();
            CloseWebRtc();
            _authToken = null;
            IsAuthenticated = false;
            UpdateState(false, LastPowerState, LastHddState);
        }
        return Task.CompletedTask;
    }

    public async Task<JetKvmPowerState> RefreshStateAsync()
    {
        try
        {
            var authRes = await AuthenticateAsync();
            if (!authRes.Success)
            {
                UpdateState(false, LastPowerState, LastHddState);
                return new JetKvmPowerState(LastPowerState, LastHddState, false, authRes.Message);
            }

            UpdateState(true, LastPowerState, LastHddState);

            var (atxOk, power, hdd) = await QueryAtxStateViaRpcAsync();
            if (atxOk)
            {
                UpdateState(true, power, hdd);
            }

            return CurrentState;
        }
        catch (Exception ex)
        {
            UpdateState(false, LastPowerState, LastHddState);
            return new JetKvmPowerState(LastPowerState, LastHddState, false, ex.Message);
        }
    }

    public async Task<bool> PowerShortPressAsync()
    {
        var (ok, _, err) = await CallRpcAsync("setATXPowerAction", new { action = "power-short" }, 4000);
        if (ok)
        {
            TriggerFastStateRefresh();
            return true;
        }
        Debug.WriteLine($"[JetKvmService] Błąd PowerShortPress: {err}");
        return false;
    }

    public async Task<bool> PowerLongPressAsync()
    {
        var (ok, _, err) = await CallRpcAsync("setATXPowerAction", new { action = "power-long" }, 4000);
        if (ok)
        {
            TriggerFastStateRefresh();
            return true;
        }
        Debug.WriteLine($"[JetKvmService] Błąd PowerLongPress: {err}");
        return false;
    }

    public async Task<bool> ResetAsync()
    {
        var (ok, _, err) = await CallRpcAsync("setATXPowerAction", new { action = "reset" }, 4000);
        if (ok)
        {
            TriggerFastStateRefresh();
            return true;
        }
        Debug.WriteLine($"[JetKvmService] Błąd Reset: {err}");
        return false;
    }

    private async Task<(bool Success, bool Power, bool Hdd)> QueryAtxStateViaRpcAsync()
    {
        var (ok, res, err) = await CallRpcAsync("getATXState", null, 3000);
        if (ok && res.HasValue && res.Value.ValueKind == JsonValueKind.Object)
        {
            bool p = false;
            if (res.Value.TryGetProperty("power", out var pp))
            {
                p = ParseBoolValue(pp);
            }
            else if (res.Value.TryGetProperty("Power", out var ppUpper))
            {
                p = ParseBoolValue(ppUpper);
            }
            else if (res.Value.TryGetProperty("powerLed", out var pLed))
            {
                p = ParseBoolValue(pLed);
            }
            else if (res.Value.TryGetProperty("power_led", out var pLedSnake))
            {
                p = ParseBoolValue(pLedSnake);
            }

            bool h = false;
            if (res.Value.TryGetProperty("hdd", out var hp))
            {
                h = ParseBoolValue(hp);
            }
            else if (res.Value.TryGetProperty("HDD", out var hpUpper))
            {
                h = ParseBoolValue(hpUpper);
            }
            else if (res.Value.TryGetProperty("hddLed", out var hLed))
            {
                h = ParseBoolValue(hLed);
            }

            return (true, p, h);
        }

        Debug.WriteLine($"[JetKvmService] Błąd QueryAtxStateViaRpc: {err}");
        return (false, LastPowerState, LastHddState);
    }

    private static bool ParseBoolValue(JsonElement elem)
    {
        return elem.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => elem.GetInt32() != 0,
            JsonValueKind.String => elem.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ||
                                    elem.GetString() == "1" ||
                                    elem.GetString()?.Equals("on", StringComparison.OrdinalIgnoreCase) == true,
            _ => false
        };
    }

    private async Task<bool> EnsureWebRtcConnectedAsync()
    {
        if (_peerConnection != null && _rpcDataChannel != null && _rpcDataChannel.readyState == RTCDataChannelState.open)
        {
            return true;
        }

        await _rtcLock.WaitAsync();
        try
        {
            if (_peerConnection != null && _rpcDataChannel != null && _rpcDataChannel.readyState == RTCDataChannelState.open)
            {
                return true;
            }

            CloseWebRtc();

            if (_httpClient == null) RebuildHttpClient();
            var authRes = await AuthenticateAsync();
            if (!authRes.Success)
            {
                return false;
            }

            var baseUri = BuildBaseUri();

            // Metoda 1: Nowoczesne JetKVM (v0.4+) – sygnalizacja WebSocket na /webrtc/signaling/client
            bool wsOk = await TryConnectWebRtcViaWebSocketAsync(baseUri);
            if (wsOk)
            {
                return true;
            }

            // Metoda 2: Starsze firmware JetKVM – HTTP POST na /webrtc/session
            bool httpOk = await TryConnectWebRtcViaHttpFallbackAsync(baseUri);
            if (httpOk)
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JetKvmService] Błąd łączenia WebRTC: {ex.Message}");
            return false;
        }
        finally
        {
            _rtcLock.Release();
        }
    }

    private async Task<bool> TryConnectWebRtcViaWebSocketAsync(Uri baseUri)
    {
        ClientWebSocket? ws = null;
        CancellationTokenSource? cts = null;
        RTCPeerConnection? pc = null;

        try
        {
            cts = new CancellationTokenSource();
            ws = new ClientWebSocket();
            ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            ws.Options.Cookies = _cookieContainer;

            string cookieHeader = _cookieContainer.GetCookieHeader(baseUri);
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                try { ws.Options.SetRequestHeader("Cookie", cookieHeader); } catch { }
            }
            else if (!string.IsNullOrEmpty(_authToken))
            {
                try { ws.Options.SetRequestHeader("Cookie", $"authToken={_authToken}"); } catch { }
            }

            string wsScheme = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            var wsUri = new Uri($"{wsScheme}://{baseUri.Authority}/webrtc/signaling/client");

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            connectCts.CancelAfter(3500);
            await ws.ConnectAsync(wsUri, connectCts.Token);

            pc = new RTCPeerConnection(new RTCConfiguration());
            var openTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var answerTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            void HookDc(RTCDataChannel channel)
            {
                channel.onopen += () =>
                {
                    _rpcDataChannel = channel;
                    openTcs.TrySetResult(true);
                };
                channel.onclose += () =>
                {
                    if (_rpcDataChannel == channel) _rpcDataChannel = null;
                };
                channel.onmessage += (dataChannel, protocol, data) =>
                {
                    try
                    {
                        string text = Encoding.UTF8.GetString(data);
                        using var doc = JsonDocument.Parse(text);
                        if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out int id))
                        {
                            if (_pendingRpc.TryRemove(id, out var tcs))
                            {
                                tcs.TrySetResult(doc.RootElement.Clone());
                            }
                        }
                    }
                    catch { }
                };
            }

            var dc = await pc.createDataChannel("rpc");
            HookDc(dc);

            pc.ondatachannel += (remoteDc) =>
            {
                if (remoteDc.label == "rpc")
                {
                    HookDc(remoteDc);
                }
            };

            pc.onconnectionstatechange += (state) =>
            {
                if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed)
                {
                    _rpcDataChannel = null;
                    foreach (var kvp in _pendingRpc)
                    {
                        kvp.Value.TrySetCanceled();
                    }
                    _pendingRpc.Clear();
                }
            };

            pc.onicecandidate += (candidate) =>
            {
                if (candidate != null && !string.IsNullOrEmpty(candidate.candidate))
                {
                    try
                    {
                        var candMsg = JsonSerializer.Serialize(new
                        {
                            type = "new-ice-candidate",
                            data = new
                            {
                                candidate = candidate.candidate,
                                sdpMid = candidate.sdpMid,
                                sdpMLineIndex = candidate.sdpMLineIndex
                            }
                        });
                        _ = SendWsTextAsync(ws, candMsg);
                    }
                    catch { }
                }
            };

            _ = ReceiveWsSignalingLoopAsync(ws, pc, answerTcs, cts.Token);

            var offer = pc.createOffer(null);
            await pc.setLocalDescription(offer);

            if (pc.iceGatheringState != RTCIceGatheringState.complete)
            {
                var iceTcs = new TaskCompletionSource<bool>();
                pc.onicegatheringstatechange += (state) =>
                {
                    if (state == RTCIceGatheringState.complete) iceTcs.TrySetResult(true);
                };
                await Task.WhenAny(iceTcs.Task, Task.Delay(300));
            }

            var offerObj = new
            {
                type = pc.localDescription.type.ToString().ToLowerInvariant(),
                sdp = pc.localDescription.sdp.ToString()
            };
            string offerJson = JsonSerializer.Serialize(offerObj);
            string offerB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(offerJson));

            var offerSignal = JsonSerializer.Serialize(new
            {
                type = "offer",
                data = new { sd = offerB64 }
            });

            await SendWsTextAsync(ws, offerSignal);

            var answerWait = await Task.WhenAny(answerTcs.Task, Task.Delay(4000));
            if (answerWait != answerTcs.Task)
            {
                Debug.WriteLine("[JetKvmService] Timeout oczekiwania na odpowiedź SDP (answer) z JetKVM.");
                try { pc.close(); } catch { }
                try { ws.Dispose(); } catch { }
                try { cts.Dispose(); } catch { }
                return false;
            }

            string answerSdp = await answerTcs.Task;
            pc.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp = answerSdp
            });

            var openWait = await Task.WhenAny(openTcs.Task, Task.Delay(5000));
            if (openWait == openTcs.Task && await openTcs.Task)
            {
                _peerConnection = pc;
                _rpcDataChannel = dc;
                _signalingWs = ws;
                _signalingCts = cts;
                return true;
            }

            Debug.WriteLine("[JetKvmService] Timeout otwierania kanału danych RPC przez WebRTC.");
            try { pc.close(); } catch { }
            try { ws.Dispose(); } catch { }
            try { cts.Dispose(); } catch { }
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JetKvmService] Błąd sygnalizacji WebSocket: {ex.Message}");
            try { pc?.close(); } catch { }
            try { ws?.Dispose(); } catch { }
            try { cts?.Dispose(); } catch { }
            return false;
        }
    }

    private async Task ReceiveWsSignalingLoopAsync(
        ClientWebSocket ws,
        RTCPeerConnection pc,
        TaskCompletionSource<string> answerTcs,
        CancellationToken ct)
    {
        var buffer = new byte[8192];
        var ms = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (res.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                ms.Write(buffer, 0, res.Count);
                if (res.EndOfMessage)
                {
                    byte[] msgBytes = ms.ToArray();
                    ms.SetLength(0);
                    string text = Encoding.UTF8.GetString(msgBytes);

                    try
                    {
                        using var doc = JsonDocument.Parse(text);
                        if (doc.RootElement.TryGetProperty("type", out var typeProp))
                        {
                            string msgType = typeProp.GetString() ?? "";
                            if (msgType == "answer" && doc.RootElement.TryGetProperty("data", out var dataElem))
                            {
                                string? sdp = ExtractSdp(dataElem);
                                if (!string.IsNullOrEmpty(sdp))
                                {
                                    answerTcs.TrySetResult(sdp);
                                }
                            }
                            else if (msgType == "new-ice-candidate" && doc.RootElement.TryGetProperty("data", out var candElem))
                            {
                                if (candElem.ValueKind == JsonValueKind.Object && candElem.TryGetProperty("candidate", out var cProp))
                                {
                                    string cStr = cProp.GetString() ?? "";
                                    if (!string.IsNullOrEmpty(cStr))
                                    {
                                        string? mid = candElem.TryGetProperty("sdpMid", out var m) ? m.GetString() : null;
                                        int mIdx = candElem.TryGetProperty("sdpMLineIndex", out var mi) && mi.TryGetInt32(out int val) ? val : 0;
                                        pc.addIceCandidate(new RTCIceCandidateInit
                                        {
                                            candidate = cStr,
                                            sdpMid = mid,
                                            sdpMLineIndex = (ushort)mIdx
                                        });
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[JetKvmService] Błąd parsowania wiadomości sygnalizacyjnej: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JetKvmService] Wyjątek pętli WebSocket: {ex.Message}");
        }
        finally
        {
            ms.Dispose();
            answerTcs.TrySetCanceled();
        }
    }

    private static string? ExtractSdp(JsonElement dataElem)
    {
        if (dataElem.ValueKind == JsonValueKind.String)
        {
            string raw = dataElem.GetString()!;
            try
            {
                byte[] bytes = Convert.FromBase64String(raw);
                string decoded = Encoding.UTF8.GetString(bytes);
                if (decoded.TrimStart().StartsWith("{"))
                {
                    using var innerDoc = JsonDocument.Parse(decoded);
                    if (innerDoc.RootElement.TryGetProperty("sdp", out var sdpProp))
                    {
                        return sdpProp.GetString();
                    }
                }
                else if (decoded.StartsWith("v="))
                {
                    return decoded;
                }
            }
            catch { }

            if (raw.StartsWith("v="))
            {
                return raw;
            }
        }
        else if (dataElem.ValueKind == JsonValueKind.Object)
        {
            if (dataElem.TryGetProperty("sd", out var sdProp))
            {
                return ExtractSdp(sdProp);
            }
            if (dataElem.TryGetProperty("sdp", out var sdpProp))
            {
                return sdpProp.GetString();
            }
        }

        return null;
    }

    private static async Task SendWsTextAsync(ClientWebSocket? ws, string text)
    {
        if (ws == null || ws.State != WebSocketState.Open) return;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task<bool> TryConnectWebRtcViaHttpFallbackAsync(Uri baseUri)
    {
        RTCPeerConnection? pc = null;
        try
        {
            pc = new RTCPeerConnection(new RTCConfiguration());
            var dc = await pc.createDataChannel("rpc");
            var openTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void HookDc(RTCDataChannel channel)
            {
                channel.onopen += () =>
                {
                    _rpcDataChannel = channel;
                    openTcs.TrySetResult(true);
                };
                channel.onclose += () =>
                {
                    if (_rpcDataChannel == channel) _rpcDataChannel = null;
                };
                channel.onmessage += (dataChannel, protocol, data) =>
                {
                    try
                    {
                        string text = Encoding.UTF8.GetString(data);
                        using var doc = JsonDocument.Parse(text);
                        if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out int id))
                        {
                            if (_pendingRpc.TryRemove(id, out var tcs))
                            {
                                tcs.TrySetResult(doc.RootElement.Clone());
                            }
                        }
                    }
                    catch { }
                };
            }

            HookDc(dc);
            pc.ondatachannel += (remoteDc) =>
            {
                if (remoteDc.label == "rpc") HookDc(remoteDc);
            };

            pc.onconnectionstatechange += (state) =>
            {
                if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed)
                {
                    _rpcDataChannel = null;
                    foreach (var kvp in _pendingRpc)
                    {
                        kvp.Value.TrySetCanceled();
                    }
                    _pendingRpc.Clear();
                }
            };

            var offer = pc.createOffer(null);
            await pc.setLocalDescription(offer);

            if (pc.iceGatheringState != RTCIceGatheringState.complete)
            {
                var iceTcs = new TaskCompletionSource<bool>();
                pc.onicegatheringstatechange += (state) =>
                {
                    if (state == RTCIceGatheringState.complete) iceTcs.TrySetResult(true);
                };
                await Task.WhenAny(iceTcs.Task, Task.Delay(1000));
            }

            var offerObj = new
            {
                type = pc.localDescription.type.ToString().ToLowerInvariant(),
                sdp = pc.localDescription.sdp.ToString()
            };
            string offerJson = JsonSerializer.Serialize(offerObj);
            string offerB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(offerJson));

            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/webrtc/session"));
            req.Content = new StringContent(JsonSerializer.Serialize(new { sd = offerB64 }), Encoding.UTF8, "application/json");
            AttachAuth(req);

            using var resp = await _httpClient!.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                pc.close();
                return false;
            }

            string respContent = await resp.Content.ReadAsStringAsync();
            using var respDoc = JsonDocument.Parse(respContent);
            if (!respDoc.RootElement.TryGetProperty("sd", out var sdProp))
            {
                pc.close();
                return false;
            }

            string answerB64 = sdProp.GetString()!;
            string answerJson = Encoding.UTF8.GetString(Convert.FromBase64String(answerB64));
            using var answerDoc = JsonDocument.Parse(answerJson);
            string answerSdp = answerDoc.RootElement.GetProperty("sdp").GetString()!;

            pc.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp = answerSdp
            });

            var waitResult = await Task.WhenAny(openTcs.Task, Task.Delay(4000));
            if (waitResult == openTcs.Task && await openTcs.Task)
            {
                _peerConnection = pc;
                _rpcDataChannel = dc;
                return true;
            }

            pc.close();
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[JetKvmService] Błąd fallbacku HTTP WebRTC: {ex.Message}");
            try { pc?.close(); } catch { }
            return false;
        }
    }

    private async Task<(bool Success, JsonElement? Result, string? Error)> CallRpcAsync(string method, object? @params = null, int timeoutMs = 5000)
    {
        bool connected = await EnsureWebRtcConnectedAsync();
        if (!connected || _rpcDataChannel == null)
        {
            return (false, null, "Kanał WebRTC RPC nie jest połączony.");
        }

        int id = Interlocked.Increment(ref _rpcRequestId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRpc[id] = tcs;

        object rpcReq = @params != null
            ? new { jsonrpc = "2.0", method = method, @params = @params, id = id }
            : new { jsonrpc = "2.0", method = method, id = id };

        string msgJson = JsonSerializer.Serialize(rpcReq);
        try
        {
            _rpcDataChannel.send(msgJson);
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (completed == tcs.Task)
            {
                var elem = await tcs.Task;
                if (elem.TryGetProperty("error", out var errElem) && errElem.ValueKind != JsonValueKind.Null)
                {
                    return (false, null, errElem.ToString());
                }
                if (elem.TryGetProperty("result", out var resElem))
                {
                    return (true, resElem, null);
                }
                return (true, null, null);
            }

            _pendingRpc.TryRemove(id, out _);
            return (false, null, "Przekroczono limit czasu oczekiwania na odpowiedź RPC.");
        }
        catch (Exception ex)
        {
            _pendingRpc.TryRemove(id, out _);
            CloseWebRtc();
            return (false, null, ex.Message);
        }
    }

    private void UpdateState(bool connected, bool power, bool hdd)
    {
        bool connChanged = IsConnected != connected;
        bool pwrChanged = LastPowerState != power || LastHddState != hdd;

        IsConnected = connected;
        LastPowerState = power;
        LastHddState = hdd;

        if (connChanged)
        {
            ConnectionStateChanged?.Invoke(connected);
        }

        if (pwrChanged || connChanged)
        {
            PowerStateChanged?.Invoke(CurrentState);
        }
    }

    private void TriggerFastStateRefresh()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500);
                await RefreshStateAsync();
                await Task.Delay(1500);
                await RefreshStateAsync();
            }
            catch { }
        });
    }

    public void StartMonitoring()
    {
        lock (_lock)
        {
            if (_pollTimer != null) return;

            int intervalSec = Math.Max(1, _settings.StatusPollIntervalSeconds);
            _pollTimer = new Timer(async _ =>
            {
                if (_isPolling) return;
                if (DateTime.UtcNow < _nextJetKvmPollAllowedUtc) return;

                _isPolling = true;
                try
                {
                    var state = await RefreshStateAsync();
                    if (!state.IsConnected)
                    {
                        _nextJetKvmPollAllowedUtc = DateTime.UtcNow.AddSeconds(30);
                    }
                    else
                    {
                        _nextJetKvmPollAllowedUtc = DateTime.MinValue;
                    }
                }
                catch
                {
                    _nextJetKvmPollAllowedUtc = DateTime.UtcNow.AddSeconds(30);
                }
                finally
                {
                    _isPolling = false;
                }
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(intervalSec));
        }
    }

    public void StopMonitoring()
    {
        lock (_lock)
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
        }
    }

    private void CloseWebRtc()
    {
        try
        {
            _signalingCts?.Cancel();
            _signalingCts?.Dispose();
            _signalingCts = null;

            if (_signalingWs != null)
            {
                try { _signalingWs.Dispose(); } catch { }
                _signalingWs = null;
            }

            _rpcDataChannel = null;
            _peerConnection?.close();
            _peerConnection?.Dispose();
            _peerConnection = null;

            foreach (var kvp in _pendingRpc)
            {
                kvp.Value.TrySetCanceled();
            }
            _pendingRpc.Clear();
        }
        catch { }
    }

    public void Dispose()
    {
        StopMonitoring();
        CloseWebRtc();
        _rtcLock.Dispose();
        _httpClient?.Dispose();
    }
}
