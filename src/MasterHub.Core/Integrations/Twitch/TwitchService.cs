namespace MasterHub.Core.Integrations.Twitch;

using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Usługa obsługi czatu Twitch (Twitch IRC over WebSocket: wss://irc-ws.chat.twitch.tv:443)
/// oraz Twitch Helix API (Klipy, Markery transmisji).
/// Zapewnia sterowanie trybami czatu (Emote-Only, Subscribers-Only, Slow Mode),
/// czyszczenie czatu, wysyłanie wiadomości oraz tworzenie klipów i markerów.
/// </summary>
public sealed class TwitchService : ITwitchService
{
    private const string TwitchWsUri = "wss://irc-ws.chat.twitch.tv:443";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private string? _cachedToken;
    private string? _cachedClientId;
    private string? _cachedBroadcasterId;

    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private bool _isDisposed;

    public TwitchService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                ConnectionStateChanged?.Invoke(value);
            }
        }
    }

    public string? ChannelName { get; private set; }
    public string? BotUsername { get; private set; }

    private bool _isEmoteOnly;
    public bool IsEmoteOnly
    {
        get => _isEmoteOnly;
        private set
        {
            if (_isEmoteOnly != value)
            {
                _isEmoteOnly = value;
                EmoteOnlyStateChanged?.Invoke(value);
            }
        }
    }

    private bool _isSubOnly;
    public bool IsSubOnly
    {
        get => _isSubOnly;
        private set
        {
            if (_isSubOnly != value)
            {
                _isSubOnly = value;
                SubOnlyStateChanged?.Invoke(value);
            }
        }
    }

    private bool _isSlowMode;
    public bool IsSlowMode
    {
        get => _isSlowMode;
        private set
        {
            if (_isSlowMode != value)
            {
                _isSlowMode = value;
                SlowModeStateChanged?.Invoke(value, SlowModeDurationSeconds);
            }
        }
    }

    private int _slowModeDurationSeconds = 30;
    public int SlowModeDurationSeconds
    {
        get => _slowModeDurationSeconds;
        private set
        {
            if (_slowModeDurationSeconds != value)
            {
                _slowModeDurationSeconds = value;
                if (IsSlowMode)
                {
                    SlowModeStateChanged?.Invoke(true, value);
                }
            }
        }
    }

    public string? LastClipUrl { get; private set; }
    public string? LastMarkerPosition { get; private set; }
    public string? LastRaidTargetChannel { get; private set; }
    public bool IsRaidPending { get; private set; }
    public int LastCommercialLengthSeconds { get; private set; }

    public event Action<bool>? ConnectionStateChanged;
    public event Action<bool>? EmoteOnlyStateChanged;
    public event Action<bool>? SubOnlyStateChanged;
    public event Action<bool, int>? SlowModeStateChanged;
    public event Action<string, string>? MessageReceived;
    public event Action<TwitchClipResult>? ClipCreated;
    public event Action<TwitchMarkerResult>? MarkerCreated;
    public event Action<TwitchRaidResult>? RaidStarted;
    public event Action<bool>? RaidCancelled;
    public event Action<TwitchCommercialResult>? CommercialStarted;

    /// <inheritdoc />
    public async Task<bool> ConnectAsync(
        string channel,
        string oauthToken,
        string? username = null,
        string? clientId = null,
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

            string cleanChannel = string.IsNullOrWhiteSpace(channel) ? string.Empty : channel.Trim().TrimStart('#').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(cleanChannel))
            {
                return false;
            }

            string cleanToken = string.IsNullOrWhiteSpace(oauthToken) ? string.Empty : oauthToken.Trim();
            string rawBearerToken = cleanToken.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase)
                ? cleanToken[6..].Trim()
                : cleanToken;
            _cachedToken = rawBearerToken;
            _cachedClientId = !string.IsNullOrWhiteSpace(clientId) ? clientId.Trim() : null;
            _cachedBroadcasterId = null;

            if (!cleanToken.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase))
            {
                cleanToken = $"oauth:{cleanToken}";
            }

            string cleanUser = string.IsNullOrWhiteSpace(username) ? cleanChannel : username.Trim().ToLowerInvariant();

            ChannelName = cleanChannel;
            BotUsername = cleanUser;

            _cts = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            var ct = linked.Token;

            var tcsReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                _webSocket = new ClientWebSocket();
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

                await _webSocket.ConnectAsync(new Uri(TwitchWsUri), ct);

                _receiveTask = Task.Run(() => ReceiveLoopAsync(tcsReady, _cts.Token), _cts.Token);

                // Rejestracja IRC
                await SendRawIrcAsync($"PASS {cleanToken}", ct);
                await SendRawIrcAsync($"NICK {cleanUser}", ct);
                await SendRawIrcAsync("CAP REQ :twitch.tv/commands twitch.tv/tags", ct);
                await SendRawIrcAsync($"JOIN #{cleanChannel}", ct);

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                using var timeoutReg = timeoutCts.Token.Register(() => tcsReady.TrySetResult(false));

                bool joined = await tcsReady.Task;
                if (!joined)
                {
                    // W przypadku braku potwierdzenia JOIN w 6s, ale gdy socket jest nadal otwarty, uznajemy połączenie za nawiązane
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        joined = true;
                    }
                }

                if (joined)
                {
                    IsConnected = true;
                    return true;
                }

                await DisconnectInternalAsync();
                return false;
            }
            catch
            {
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
        try
        {
            _cts?.Cancel();
        }
        catch { }

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", closeCts.Token);
                }
            }
            catch { }

            try
            {
                _webSocket.Dispose();
            }
            catch { }
            _webSocket = null;
        }

        try
        {
            if (_receiveTask != null)
            {
                await _receiveTask;
            }
        }
        catch { }
        _receiveTask = null;

        _cts?.Dispose();
        _cts = null;

        IsConnected = false;
        IsEmoteOnly = false;
        IsSubOnly = false;
        IsSlowMode = false;
        LastClipUrl = null;
        LastMarkerPosition = null;
        LastRaidTargetChannel = null;
        IsRaidPending = false;
        LastCommercialLengthSeconds = 0;
    }

    /// <inheritdoc />
    public async Task<bool> ToggleEmoteOnlyAsync(CancellationToken cancellationToken = default)
    {
        return await SetEmoteOnlyAsync(!IsEmoteOnly, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SetEmoteOnlyAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || string.IsNullOrEmpty(ChannelName)) return false;
        string cmd = enabled ? "/emoteonly" : "/emoteonlyoff";
        bool sent = await SendChatMessageAsync(cmd, cancellationToken);
        if (sent)
        {
            IsEmoteOnly = enabled;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> ToggleSubOnlyAsync(CancellationToken cancellationToken = default)
    {
        return await SetSubOnlyAsync(!IsSubOnly, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SetSubOnlyAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || string.IsNullOrEmpty(ChannelName)) return false;
        string cmd = enabled ? "/subscribers" : "/subscribersoff";
        bool sent = await SendChatMessageAsync(cmd, cancellationToken);
        if (sent)
        {
            IsSubOnly = enabled;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> ToggleSlowModeAsync(int seconds = 30, CancellationToken cancellationToken = default)
    {
        return await SetSlowModeAsync(!IsSlowMode, seconds, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SetSlowModeAsync(bool enabled, int seconds = 30, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || string.IsNullOrEmpty(ChannelName)) return false;
        seconds = Math.Clamp(seconds, 1, 86400);
        string cmd = enabled ? $"/slow {seconds}" : "/slowoff";
        bool sent = await SendChatMessageAsync(cmd, cancellationToken);
        if (sent)
        {
            _slowModeDurationSeconds = seconds;
            IsSlowMode = enabled;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<bool> ClearChatAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected || string.IsNullOrEmpty(ChannelName)) return false;
        return await SendChatMessageAsync("/clear", cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SendChatMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || string.IsNullOrEmpty(ChannelName) || string.IsNullOrWhiteSpace(message)) return false;
        string raw = $"PRIVMSG #{ChannelName} :{message.Trim()}";
        return await SendRawIrcAsync(raw, cancellationToken);
    }

    /// <summary>
    /// Ustawia identyfikatory Twitch API (np. do testów jednostkowych lub gdy są znane z góry).
    /// </summary>
    public void SetHelixCredentials(string clientId, string broadcasterId, string? oauthToken = null)
    {
        _cachedClientId = clientId;
        _cachedBroadcasterId = broadcasterId;
        if (!string.IsNullOrWhiteSpace(oauthToken))
        {
            _cachedToken = oauthToken.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase)
                ? oauthToken[6..].Trim()
                : oauthToken.Trim();
        }
    }

    /// <inheritdoc />
    public async Task<TwitchClipResult?> CreateClipAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_cachedToken))
        {
            var res = new TwitchClipResult(false, null, null, "Brak aktywnego połączenia lub tokenu OAuth Twitch");
            ClipCreated?.Invoke(res);
            return res;
        }

        bool hasCreds = await EnsureHelixCredentialsAsync(cancellationToken);
        if (!hasCreds || string.IsNullOrWhiteSpace(_cachedClientId) || string.IsNullOrWhiteSpace(_cachedBroadcasterId))
        {
            var res = new TwitchClipResult(false, null, null, "Nie udało się zweryfikować uprawnień Twitch API (Client-Id / Broadcaster ID)");
            ClipCreated?.Invoke(res);
            return res;
        }

        try
        {
            string url = $"https://api.twitch.tv/helix/clips?broadcaster_id={_cachedBroadcasterId}&has_delay=false";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("Client-Id", _cachedClientId);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_cachedToken}");

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            string responseBody = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (resp.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(responseBody);
                if (json.RootElement.TryGetProperty("data", out var dataArr) && dataArr.GetArrayLength() > 0)
                {
                    var clip = dataArr[0];
                    string? clipId = clip.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    string? editUrl = clip.TryGetProperty("edit_url", out var editProp) ? editProp.GetString() : null;

                    LastClipUrl = editUrl ?? (clipId != null ? $"https://clips.twitch.tv/{clipId}" : null);
                    var res = new TwitchClipResult(true, clipId, LastClipUrl, null);
                    ClipCreated?.Invoke(res);
                    return res;
                }
            }

            string error = $"Błąd Twitch API: {resp.StatusCode} - {responseBody}";
            var failRes = new TwitchClipResult(false, null, null, error);
            ClipCreated?.Invoke(failRes);
            return failRes;
        }
        catch (Exception ex)
        {
            var exRes = new TwitchClipResult(false, null, null, $"Wyjątek podczas tworzenia klipu: {ex.Message}");
            ClipCreated?.Invoke(exRes);
            return exRes;
        }
    }

    /// <inheritdoc />
    public async Task<TwitchMarkerResult?> CreateStreamMarkerAsync(string? description = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_cachedToken))
        {
            var res = new TwitchMarkerResult(false, null, 0, description, "Brak aktywnego połączenia lub tokenu OAuth Twitch");
            MarkerCreated?.Invoke(res);
            return res;
        }

        bool hasCreds = await EnsureHelixCredentialsAsync(cancellationToken);
        if (!hasCreds || string.IsNullOrWhiteSpace(_cachedClientId) || string.IsNullOrWhiteSpace(_cachedBroadcasterId))
        {
            var res = new TwitchMarkerResult(false, null, 0, description, "Nie udało się zweryfikować uprawnień Twitch API (Client-Id / Broadcaster ID)");
            MarkerCreated?.Invoke(res);
            return res;
        }

        try
        {
            string url = "https://api.twitch.tv/helix/streams/markers";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("Client-Id", _cachedClientId);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_cachedToken}");

            string desc = string.IsNullOrWhiteSpace(description) ? "MasterHub Marker" : description.Trim();
            if (desc.Length > 140) desc = desc[..140];

            string payload = JsonSerializer.Serialize(new
            {
                user_id = _cachedBroadcasterId,
                description = desc
            });

            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            string responseBody = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (resp.IsSuccessStatusCode)
            {
                using var json = JsonDocument.Parse(responseBody);
                if (json.RootElement.TryGetProperty("data", out var dataArr) && dataArr.GetArrayLength() > 0)
                {
                    var marker = dataArr[0];
                    string? markerId = marker.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    int pos = marker.TryGetProperty("position_seconds", out var posProp) ? posProp.GetInt32() : 0;
                    string? resDesc = marker.TryGetProperty("description", out var descProp) ? descProp.GetString() : desc;

                    int minutes = pos / 60;
                    int seconds = pos % 60;
                    LastMarkerPosition = $"{minutes:D2}:{seconds:D2}";

                    var res = new TwitchMarkerResult(true, markerId, pos, resDesc, null);
                    MarkerCreated?.Invoke(res);
                    return res;
                }
            }

            string error = $"Błąd Twitch API: {resp.StatusCode} - {responseBody}";
            var failRes = new TwitchMarkerResult(false, null, 0, description, error);
            MarkerCreated?.Invoke(failRes);
            return failRes;
        }
        catch (Exception ex)
        {
            var exRes = new TwitchMarkerResult(false, null, 0, description, $"Wyjątek podczas tworzenia markera: {ex.Message}");
            MarkerCreated?.Invoke(exRes);
            return exRes;
        }
    }

    /// <inheritdoc />
    public async Task<TwitchRaidResult> StartRaidAsync(string targetChannel, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetChannel))
        {
            var res = new TwitchRaidResult(false, null, "Nie podano nazwy kanału docelowego dla raidu.");
            RaidStarted?.Invoke(res);
            return res;
        }

        string cleanTarget = targetChannel.Trim().TrimStart('#').ToLowerInvariant();
        string? lastHelixError = null;

        // 1. Próba wykonania przez Twitch Helix API (POST /helix/raids)
        try
        {
            bool hasCreds = await EnsureHelixCredentialsAsync(cancellationToken);
            if (hasCreds && !string.IsNullOrWhiteSpace(_cachedClientId) && !string.IsNullOrWhiteSpace(_cachedBroadcasterId))
            {
                using var userReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.twitch.tv/helix/users?login={Uri.EscapeDataString(cleanTarget)}");
                userReq.Headers.TryAddWithoutValidation("Client-Id", _cachedClientId);
                userReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_cachedToken}");

                using var userResp = await _httpClient.SendAsync(userReq, cancellationToken);
                if (userResp.IsSuccessStatusCode)
                {
                    using var json = await JsonDocument.ParseAsync(await userResp.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                    if (json.RootElement.TryGetProperty("data", out var dataArr))
                    {
                        if (dataArr.GetArrayLength() == 0)
                        {
                            var notFoundRes = new TwitchRaidResult(false, cleanTarget, $"Kanał docelowy '{cleanTarget}' nie istnieje na Twitchu.");
                            RaidStarted?.Invoke(notFoundRes);
                            return notFoundRes;
                        }

                        var firstUser = dataArr[0];
                        if (firstUser.TryGetProperty("id", out var targetIdProp))
                        {
                            string? toBroadcasterId = targetIdProp.GetString();
                            if (!string.IsNullOrWhiteSpace(toBroadcasterId))
                            {
                                if (string.Equals(toBroadcasterId, _cachedBroadcasterId, StringComparison.OrdinalIgnoreCase))
                                {
                                    var selfRaidRes = new TwitchRaidResult(false, cleanTarget, "Nie można rozpocząć raidu na własny kanał.");
                                    RaidStarted?.Invoke(selfRaidRes);
                                    return selfRaidRes;
                                }

                                string raidUrl = $"https://api.twitch.tv/helix/raids?from_broadcaster_id={Uri.EscapeDataString(_cachedBroadcasterId)}&to_broadcaster_id={Uri.EscapeDataString(toBroadcasterId)}";
                                using var raidReq = new HttpRequestMessage(HttpMethod.Post, raidUrl);
                                raidReq.Headers.TryAddWithoutValidation("Client-Id", _cachedClientId);
                                raidReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_cachedToken}");

                                using var raidResp = await _httpClient.SendAsync(raidReq, cancellationToken);
                                if (raidResp.IsSuccessStatusCode)
                                {
                                    LastRaidTargetChannel = cleanTarget;
                                    IsRaidPending = true;
                                    var okRes = new TwitchRaidResult(true, cleanTarget, null);
                                    RaidStarted?.Invoke(okRes);
                                    return okRes;
                                }

                                string errBody = await raidResp.Content.ReadAsStringAsync(cancellationToken);
                                lastHelixError = ExtractHelixErrorMessage(errBody, (int)raidResp.StatusCode, "channel:manage:raids");
                                if (raidResp.StatusCode != System.Net.HttpStatusCode.Unauthorized || !IsConnected)
                                {
                                    var apiErrRes = new TwitchRaidResult(false, cleanTarget, lastHelixError);
                                    RaidStarted?.Invoke(apiErrRes);
                                    return apiErrRes;
                                }
                            }
                        }
                    }
                }
                else
                {
                    string errBody = await userResp.Content.ReadAsStringAsync(cancellationToken);
                    lastHelixError = ExtractHelixErrorMessage(errBody, (int)userResp.StatusCode, "channel:manage:raids");
                }
            }
        }
        catch (Exception ex)
        {
            lastHelixError = ex.Message;
        }

        // 2. Fallback przez IRC Czat (/raid {channel}) gdy brak poświadczeń Helix
        if (IsConnected)
        {
            bool sent = await SendChatMessageAsync($"/raid {cleanTarget}", cancellationToken);
            if (sent)
            {
                LastRaidTargetChannel = cleanTarget;
                IsRaidPending = true;
                var ircRes = new TwitchRaidResult(true, cleanTarget, null);
                RaidStarted?.Invoke(ircRes);
                return ircRes;
            }
        }

        var failRes = new TwitchRaidResult(false, cleanTarget, lastHelixError ?? "Brak połączenia z Twitch (Helix / IRC) lub błąd uprawnień do raidu (wymagany scope channel:manage:raids).");
        RaidStarted?.Invoke(failRes);
        return failRes;
    }

    /// <inheritdoc />
    public async Task<bool> CancelRaidAsync(CancellationToken cancellationToken = default)
    {
        // 1. Próba wykonania przez Helix API (DELETE /helix/raids)
        try
        {
            bool hasCreds = await EnsureHelixCredentialsAsync(cancellationToken);
            if (hasCreds && !string.IsNullOrWhiteSpace(_cachedClientId) && !string.IsNullOrWhiteSpace(_cachedBroadcasterId))
            {
                string cancelUrl = $"https://api.twitch.tv/helix/raids?broadcaster_id={Uri.EscapeDataString(_cachedBroadcasterId)}";
                using var cancelReq = new HttpRequestMessage(HttpMethod.Delete, cancelUrl);
                cancelReq.Headers.TryAddWithoutValidation("Client-Id", _cachedClientId);
                cancelReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_cachedToken}");

                using var cancelResp = await _httpClient.SendAsync(cancelReq, cancellationToken);
                if (cancelResp.IsSuccessStatusCode)
                {
                    IsRaidPending = false;
                    RaidCancelled?.Invoke(true);
                    return true;
                }

                if (cancelResp.StatusCode != System.Net.HttpStatusCode.Unauthorized || !IsConnected)
                {
                    RaidCancelled?.Invoke(false);
                    return false;
                }
            }
        }
        catch
        {
            // Fallback IRC
        }

        // 2. Fallback przez IRC Czat (/unraid)
        if (IsConnected)
        {
            bool sent = await SendChatMessageAsync("/unraid", cancellationToken);
            if (sent)
            {
                IsRaidPending = false;
                RaidCancelled?.Invoke(true);
                return true;
            }
        }

        RaidCancelled?.Invoke(false);
        return false;
    }

    /// <inheritdoc />
    public async Task<TwitchCommercialResult> StartCommercialAsync(int lengthSeconds = 30, CancellationToken cancellationToken = default)
    {
        int validLength = lengthSeconds switch
        {
            <= 30 => 30,
            <= 60 => 60,
            <= 90 => 90,
            <= 120 => 120,
            <= 150 => 150,
            _ => 180
        };

        string? lastHelixError = null;

        // 1. Próba wykonania przez Helix API (POST /helix/channels/commercial)
        try
        {
            bool hasCreds = await EnsureHelixCredentialsAsync(cancellationToken);
            if (hasCreds && !string.IsNullOrWhiteSpace(_cachedClientId) && !string.IsNullOrWhiteSpace(_cachedBroadcasterId))
            {
                string commercialUrl = "https://api.twitch.tv/helix/channels/commercial";
                using var req = new HttpRequestMessage(HttpMethod.Post, commercialUrl);
                req.Headers.TryAddWithoutValidation("Client-Id", _cachedClientId);
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_cachedToken}");

                string payload = JsonSerializer.Serialize(new
                {
                    broadcaster_id = _cachedBroadcasterId,
                    length = validLength
                });
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using var resp = await _httpClient.SendAsync(req, cancellationToken);
                string responseBody = await resp.Content.ReadAsStringAsync(cancellationToken);

                if (resp.IsSuccessStatusCode)
                {
                    using var json = JsonDocument.Parse(responseBody);
                    int respLength = validLength;
                    int retryAfter = 0;
                    if (json.RootElement.TryGetProperty("data", out var dataArr) && dataArr.GetArrayLength() > 0)
                    {
                        var first = dataArr[0];
                        if (first.TryGetProperty("length", out var lenProp)) respLength = lenProp.GetInt32();
                        if (first.TryGetProperty("retry_after", out var retryProp)) retryAfter = retryProp.GetInt32();
                    }

                    LastCommercialLengthSeconds = respLength;
                    var okRes = new TwitchCommercialResult(true, respLength, retryAfter, null);
                    CommercialStarted?.Invoke(okRes);
                    return okRes;
                }

                lastHelixError = ExtractHelixErrorMessage(responseBody, (int)resp.StatusCode, "channel:edit:commercial");
                if (resp.StatusCode != System.Net.HttpStatusCode.Unauthorized || !IsConnected)
                {
                    var apiErrRes = new TwitchCommercialResult(false, validLength, 0, lastHelixError);
                    CommercialStarted?.Invoke(apiErrRes);
                    return apiErrRes;
                }
            }
        }
        catch (Exception ex)
        {
            lastHelixError = ex.Message;
        }

        // 2. Fallback przez IRC Czat (/commercial {validLength})
        if (IsConnected)
        {
            bool sent = await SendChatMessageAsync($"/commercial {validLength}", cancellationToken);
            if (sent)
            {
                LastCommercialLengthSeconds = validLength;
                var ircRes = new TwitchCommercialResult(true, validLength, 0, null);
                CommercialStarted?.Invoke(ircRes);
                return ircRes;
            }
        }

        var failRes = new TwitchCommercialResult(false, validLength, 0, lastHelixError ?? "Brak połączenia lub uprawnień do uruchomienia reklamy (wymagany Partner/Afiliant Twitch oraz scope channel:edit:commercial).");
        CommercialStarted?.Invoke(failRes);
        return failRes;
    }

    private static string ExtractHelixErrorMessage(string responseBody, int statusCode, string requiredScope)
    {
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("message", out var msgProp))
                {
                    string? msg = msgProp.GetString();
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        return $"Twitch Helix API ({statusCode}): {msg}";
                    }
                }
            }
            catch
            {
                // Ignoruj błędy parsowania JSON
            }
        }

        return statusCode == 401
            ? $"Brak autoryzacji Twitch API (401) — upewnij się, że token OAuth posiada uprawnienie '{requiredScope}'."
            : $"Błąd odpowiedzi Twitch Helix API (HTTP {statusCode}).";
    }

    private async Task<bool> EnsureHelixCredentialsAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_cachedToken))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_cachedClientId) && !string.IsNullOrWhiteSpace(_cachedBroadcasterId))
        {
            return true;
        }

        try
        {
            // 1. Walidacja tokena przez https://id.twitch.tv/oauth2/validate
            using var validateReq = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
            validateReq.Headers.TryAddWithoutValidation("Authorization", $"OAuth {_cachedToken}");

            using var validateResp = await _httpClient.SendAsync(validateReq, ct);
            if (validateResp.IsSuccessStatusCode)
            {
                using var json = await JsonDocument.ParseAsync(await validateResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var root = json.RootElement;
                if (root.TryGetProperty("client_id", out var clientProp))
                {
                    _cachedClientId = clientProp.GetString();
                }
                if (root.TryGetProperty("user_id", out var userProp))
                {
                    _cachedBroadcasterId = userProp.GetString();
                }
            }

            // 2. Jeśli mamy ChannelName i broadcaster ID nie jest ustawione lub pobieramy ID kanału docelowego:
            if (!string.IsNullOrWhiteSpace(_cachedClientId) && !string.IsNullOrWhiteSpace(ChannelName) &&
                string.IsNullOrWhiteSpace(_cachedBroadcasterId))
            {
                using var userReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.twitch.tv/helix/users?login={ChannelName.ToLowerInvariant()}");
                userReq.Headers.TryAddWithoutValidation("Client-Id", _cachedClientId);
                userReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_cachedToken}");

                using var userResp = await _httpClient.SendAsync(userReq, ct);
                if (userResp.IsSuccessStatusCode)
                {
                    using var json = await JsonDocument.ParseAsync(await userResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                    if (json.RootElement.TryGetProperty("data", out var dataArr) && dataArr.GetArrayLength() > 0)
                    {
                        var firstUser = dataArr[0];
                        if (firstUser.TryGetProperty("id", out var idProp))
                        {
                            _cachedBroadcasterId = idProp.GetString();
                        }
                    }
                }
            }

            return !string.IsNullOrWhiteSpace(_cachedClientId) && !string.IsNullOrWhiteSpace(_cachedBroadcasterId);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> SendRawIrcAsync(string message, CancellationToken cancellationToken)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open) return false;

        byte[] bytes = Encoding.UTF8.GetBytes($"{message}\r\n");

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_webSocket.State != WebSocketState.Open) return false;
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(TaskCompletionSource<bool> tcsReady, CancellationToken ct)
    {
        var buffer = new byte[4096];
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
                    break;
                }

                string text = Encoding.UTF8.GetString(ms.ToArray());
                string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    // Odpowiedź na PING Twitcha
                    if (line.StartsWith("PING", StringComparison.OrdinalIgnoreCase))
                    {
                        string pongArg = line.Length > 5 ? line[5..] : ":tmi.twitch.tv";
                        _ = SendRawIrcAsync($"PONG {pongArg}", CancellationToken.None);
                        continue;
                    }

                    // Potwierdzenie JOIN lub dołączenia do kanału
                    if (line.Contains("JOIN #", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("353 ", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("366 ", StringComparison.OrdinalIgnoreCase))
                    {
                        tcsReady.TrySetResult(true);
                    }

                    // Tagi ROOMSTATE (zawierają stan czatu: emote-only, subs-only, slow)
                    if (line.Contains("ROOMSTATE", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseRoomState(line);
                        tcsReady.TrySetResult(true);
                    }
                    // Wiadomości NOTICE (komunikaty o włączeniu / wyłączeniu trybów czatu)
                    else if (line.Contains("NOTICE", StringComparison.OrdinalIgnoreCase))
                    {
                        ParseNotice(line);
                    }
                    // Zwykłe wiadomości PRIVMSG
                    else if (line.Contains("PRIVMSG", StringComparison.OrdinalIgnoreCase))
                    {
                        ParsePrivmsg(line);
                    }
                }
            }
        }
        catch
        {
            // Przerwanie połączenia
        }
        finally
        {
            tcsReady.TrySetResult(false);
            IsConnected = false;
        }
    }

    private void ParseRoomState(string line)
    {
        // Format: @badge-info=;emote-only=1;followers-only=-1;r9k=0;rituals=0;room-id=...;slow=0;subs-only=0 :tmi.twitch.tv ROOMSTATE #channel
        int tagEnd = line.IndexOf(" :", StringComparison.Ordinal);
        if (tagEnd <= 0) return;

        string tagsPart = line[..tagEnd].TrimStart('@');
        string[] tags = tagsPart.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (string tag in tags)
        {
            int eq = tag.IndexOf('=');
            if (eq <= 0) continue;

            string key = tag[..eq];
            string val = tag[(eq + 1)..];

            if (key.Equals("emote-only", StringComparison.OrdinalIgnoreCase))
            {
                IsEmoteOnly = val == "1";
            }
            else if (key.Equals("subs-only", StringComparison.OrdinalIgnoreCase))
            {
                IsSubOnly = val == "1";
            }
            else if (key.Equals("slow", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(val, out int seconds))
                {
                    if (seconds > 0)
                    {
                        SlowModeDurationSeconds = seconds;
                        IsSlowMode = true;
                    }
                    else
                    {
                        IsSlowMode = false;
                    }
                }
            }
        }
    }

    private void ParseNotice(string line)
    {
        // @msg-id=emote_only_on :tmi.twitch.tv NOTICE #channel :This room is now in emote-only mode.
        if (line.Contains("emote_only_on", StringComparison.OrdinalIgnoreCase))
        {
            IsEmoteOnly = true;
        }
        else if (line.Contains("emote_only_off", StringComparison.OrdinalIgnoreCase))
        {
            IsEmoteOnly = false;
        }
        else if (line.Contains("subs_on", StringComparison.OrdinalIgnoreCase))
        {
            IsSubOnly = true;
        }
        else if (line.Contains("subs_off", StringComparison.OrdinalIgnoreCase))
        {
            IsSubOnly = false;
        }
        else if (line.Contains("slow_on", StringComparison.OrdinalIgnoreCase))
        {
            IsSlowMode = true;
        }
        else if (line.Contains("slow_off", StringComparison.OrdinalIgnoreCase))
        {
            IsSlowMode = false;
        }
    }

    private void ParsePrivmsg(string line)
    {
        try
        {
            // :username!username@username.tmi.twitch.tv PRIVMSG #channel :treść wiadomości
            int colonIdx = line.IndexOf(':', 1);
            if (colonIdx > 0)
            {
                string msg = line[(colonIdx + 1)..];
                string prefix = line[1..colonIdx];
                int exclamation = prefix.IndexOf('!');
                string sender = exclamation > 0 ? prefix[..exclamation] : "User";
                MessageReceived?.Invoke(sender, msg);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _cts?.Cancel();
        }
        catch { }

        _webSocket?.Dispose();
        _cts?.Dispose();
        _connectionLock.Dispose();
        _sendLock.Dispose();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await DisconnectInternalAsync();

        _connectionLock.Dispose();
        _sendLock.Dispose();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
