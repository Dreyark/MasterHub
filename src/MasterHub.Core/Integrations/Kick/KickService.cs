namespace MasterHub.Core.Integrations.Kick;

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Usługa integracji z platformą Kick (Pusher WebSocket czatu &amp; Kick API).
/// Zapewnia odbiór i wysyłanie wiadomości, sterowanie trybami czatu oraz tworzenie klipów.
/// </summary>
public sealed class KickService : IKickService
{
    private const string PusherWsUri = "wss://ws-us2.pusher.com/app/eb1d5f283081a78b932c?protocol=7&client=js&version=7.6.0&flash=false";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _pingTask;
    private bool _isDisposed;

    private string? _cachedAuthToken;
    private string? _chatroomId;
    private long? _broadcasterUserId;

    public KickService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MasterHub/1.0");
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

    public string? LastClipUrl { get; private set; }

    public event Action<bool>? ConnectionStateChanged;
    public event Action<string, string>? MessageReceived;
    public event Action<bool>? SubOnlyStateChanged;
    public event Action<bool, int>? SlowModeStateChanged;
    public event Action<bool>? EmoteOnlyStateChanged;
    public event Action<KickClipResult>? ClipCreated;

    /// <inheritdoc />
    public async Task<bool> ConnectAsync(
        string channel,
        string? authToken = null,
        string? username = null,
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

            ChannelName = cleanChannel;
            BotUsername = !string.IsNullOrWhiteSpace(username) ? username.Trim() : cleanChannel;
            _cachedAuthToken = !string.IsNullOrWhiteSpace(authToken) ? authToken.Trim() : null;

            _cts = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
            var ct = linked.Token;

            // Spróbuj pobrać ID pokoju czatu (chatroom.id) z API Kick
            _broadcasterUserId = null;
            _chatroomId = await ResolveChatroomIdAsync(cleanChannel, ct);

            try
            {
                _webSocket = new ClientWebSocket();
                _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                await _webSocket.ConnectAsync(new Uri(PusherWsUri), ct);

                _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
                _pingTask = Task.Run(() => PingLoopAsync(_cts.Token), _cts.Token);

                // Subskrypcja kanału Pusher dla czatu
                string pusherChannel = !string.IsNullOrWhiteSpace(_chatroomId)
                    ? $"chatrooms.{_chatroomId}.v2"
                    : $"channel.{cleanChannel}";

                string subMessage = JsonSerializer.Serialize(new
                {
                    @event = "pusher:subscribe",
                    data = new { auth = "", channel = pusherChannel }
                });

                await SendRawWsAsync(subMessage, ct);

                IsConnected = true;
                return true;
            }
            catch
            {
                // W przypadku błędu połączenia z websocketem, jeśli mamy channel name, uznajemy połączenie logiczne za aktywne
                // aby umożliwić wywoływanie akcji API
                if (!string.IsNullOrWhiteSpace(cleanChannel))
                {
                    IsConnected = true;
                    return true;
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
        if (_cts != null)
        {
            try
            {
                _cts.Cancel();
            }
            catch { }
        }

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.CloseReceived)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", closeCts.Token);
                }
            }
            catch { }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
            }
        }

        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;
        _pingTask = null;

        IsConnected = false;
    }

    private async Task<string?> ResolveChatroomIdAsync(string channel, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://kick.com/api/v2/channels/{channel}");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await _httpClient.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("chatroom", out var chatroom) &&
                    chatroom.TryGetProperty("id", out var idProp))
                {
                    return idProp.GetInt64().ToString();
                }
            }
        }
        catch { }

        return null;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[8192];
        var ms = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && _webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                ms.SetLength(0);

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await DisconnectAsync();
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                string message = Encoding.UTF8.GetString(ms.ToArray());
                ProcessPusherMessage(message);
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!ct.IsCancellationRequested)
            {
                IsConnected = false;
            }
        }
    }

    /// <summary>Odczytuje tryby czatu ze zdarzenia ChatroomUpdatedEvent (slow_mode, subscribers_mode, emotes_mode).</summary>
    internal void ApplyChatroomUpdate(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("emotes_mode", out var emotes) && emotes.TryGetProperty("enabled", out var eEn))
            {
                IsEmoteOnly = eEn.ValueKind == JsonValueKind.True;
            }
            if (root.TryGetProperty("subscribers_mode", out var subs) && subs.TryGetProperty("enabled", out var sEn))
            {
                IsSubOnly = sEn.ValueKind == JsonValueKind.True;
            }
            if (root.TryGetProperty("slow_mode", out var slow) && slow.TryGetProperty("enabled", out var slEn))
            {
                if (slow.TryGetProperty("message_interval", out var interval) && interval.TryGetInt32(out int secs) && secs > 0)
                {
                    SlowModeDurationSeconds = secs;
                }
                IsSlowMode = slEn.ValueKind == JsonValueKind.True;
            }
        }
        catch { }
    }

    private void ProcessPusherMessage(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("event", out var evProp))
            {
                string? ev = evProp.GetString();
                if (ev == "pusher:ping")
                {
                    _ = SendRawWsAsync("{\"event\":\"pusher:pong\",\"data\":{}}", CancellationToken.None);
                }
                else if (ev == "App\\Events\\ChatroomUpdatedEvent" && root.TryGetProperty("data", out var roomData))
                {
                    ApplyChatroomUpdate(roomData.ValueKind == JsonValueKind.String ? roomData.GetString() ?? "{}" : roomData.GetRawText());
                }
                else if (ev == "App\\Events\\ChatMessageEvent" && root.TryGetProperty("data", out var dataProp))
                {
                    string dataStr = dataProp.GetString() ?? dataProp.GetRawText();
                    using var dataDoc = JsonDocument.Parse(dataStr);
                    var dataRoot = dataDoc.RootElement;

                    string content = dataRoot.TryGetProperty("content", out var cProp) ? (cProp.GetString() ?? "") : "";
                    string sender = "";
                    if (dataRoot.TryGetProperty("sender", out var senderObj) &&
                        senderObj.TryGetProperty("username", out var uProp))
                    {
                        sender = uProp.GetString() ?? "";
                    }

                    if (!string.IsNullOrEmpty(sender) && !string.IsNullOrEmpty(content))
                    {
                        MessageReceived?.Invoke(sender, content);
                    }
                }
            }
        }
        catch { }
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(25), ct);
                if (_webSocket != null && _webSocket.State == WebSocketState.Open)
                {
                    await SendRawWsAsync("{\"event\":\"pusher:ping\",\"data\":{}}", ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task<bool> SendRawWsAsync(string json, CancellationToken ct)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            return false;
        }

        await _sendLock.WaitAsync(ct);
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
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

    /// <inheritdoc />
    public async Task<bool> SendChatMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(ChannelName))
        {
            return false;
        }

        // Oficjalne publiczne API Kick wymaga tokenu OAuth (scope chat:write). Bez tokenu NIE wysyłamy nic -
        // wcześniej metoda zwracała true mimo że wiadomość nigdy nie trafiała na czat.
        if (string.IsNullOrWhiteSpace(_cachedAuthToken))
        {
            return false;
        }

        try
        {
            long? broadcasterId = await EnsureBroadcasterUserIdAsync(cancellationToken);

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.kick.com/public/v1/chat");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedAuthToken);

            // type "user" wysyła jako autoryzowany użytkownik na wskazany kanał; bez ID kanału zostaje "bot".
            object payload = broadcasterId.HasValue
                ? new { broadcaster_user_id = broadcasterId.Value, content = message.Trim(), type = "user" }
                : new { content = message.Trim(), type = "bot" };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ID użytkownika-nadawcy kanału z oficjalnego API (GET /public/v1/channels?slug=...), wymagane przez POST /chat.
    /// </summary>
    private async Task<long?> EnsureBroadcasterUserIdAsync(CancellationToken ct)
    {
        if (_broadcasterUserId.HasValue) return _broadcasterUserId;
        if (string.IsNullOrWhiteSpace(_cachedAuthToken) || string.IsNullOrWhiteSpace(ChannelName)) return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.kick.com/public/v1/channels?slug={Uri.EscapeDataString(ChannelName)}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cachedAuthToken);
            using var resp = await _httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0 &&
                data[0].TryGetProperty("broadcaster_user_id", out var idProp) &&
                idProp.TryGetInt64(out long id))
            {
                _broadcasterUserId = id;
                return id;
            }
        }
        catch { }

        return null;
    }

    // Publiczne API Kick nie udostępnia (jeszcze) żadnego endpointu zmiany ustawień czatu ani czyszczenia czatu.
    // Wcześniej metody tylko przestawiały stan lokalnie, zwracały true i wysyłały na czat dosłowny tekst "/slow 30" itp.
    // Teraz zwracają false (akcja nieobsługiwana), a stan trybów jest odczytywany ze zdarzeń czatu (ChatroomUpdatedEvent).

    /// <inheritdoc />
    public Task<bool> ClearChatAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<bool> ToggleSubOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<bool> SetSubOnlyAsync(bool enabled, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<bool> ToggleSlowModeAsync(int seconds = 30, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<bool> SetSlowModeAsync(bool enabled, int seconds = 30, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<bool> ToggleEmoteOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<bool> SetEmoteOnlyAsync(bool enabled, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<KickClipResult?> CreateClipAsync(CancellationToken cancellationToken = default)
    {
        // Kick nie ma publicznego API tworzenia klipów. Wcześniej zwracany był "sukces" z wymyślonym ID i linkiem do listy klipów.
        string message = string.IsNullOrWhiteSpace(ChannelName)
            ? "Brak połączonego kanału Kick"
            : $"Kick nie udostępnia API tworzenia klipów - utwórz klip ręcznie na https://kick.com/{ChannelName}";
        var res = new KickClipResult(false, null, null, message);
        ClipCreated?.Invoke(res);
        return Task.FromResult<KickClipResult?>(res);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            DisconnectInternalAsync().GetAwaiter().GetResult();
        }
        catch { }

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
