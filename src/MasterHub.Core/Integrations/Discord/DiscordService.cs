using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.Discord;

/// <summary>
/// Lokalny klient potoku Named Pipe (\\.\pipe\discord-ipc-0..9) do komunikacji z aplikacją Discord Desktop.
/// Obsługuje binarny protokół ramkowania Discord IPC (8-bajtowy nagłówek Little-Endian [Opcode:u32][Length:u32]),
/// Handshake JSON-RPC (READY), opcjonalną autoryzację OAuth2 (AUTHENTICATE), pobieranie stanu głosu
/// (GET_VOICE_SETTINGS) oraz subskrypcję zmian wyciszenia w czasie rzeczywistym (VOICE_SETTINGS_UPDATE).
/// </summary>
public sealed class DiscordService : IDiscordService
{
    /// <summary>
    /// Domyślny identyfikator aplikacji Discord RPC (kompatybilny z lokalnym Handshake IPC).
    /// </summary>
    public const string DefaultClientId = "207646673902501888";

    /// <summary>
    /// Rozmiar binarnego nagłówka ramki Discord IPC w bajtach (4 bajty Opcode + 4 bajty Length).
    /// </summary>
    public const int FrameHeaderSize = 8;

    /// <summary>
    /// Maksymalny akceptowany rozmiar ładunku JSON w pojedynczej ramce IPC (1 MB).
    /// </summary>
    public const int MaxPayloadSize = 1024 * 1024;

    private readonly Func<DiscordIpcOpcode, string, CancellationToken, Task<(DiscordIpcOpcode Opcode, string JsonPayload)?>>? _customIpcHandler;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement?>> _pendingRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private NamedPipeClientStream? _pipeStream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoopTask;

    private long _nextNonceId;
    private bool _isConnected;
    private bool _isAuthenticated;
    private bool _isMuted;
    private bool _isDeafened;
    private bool _isCameraEnabled;
    private bool _isScreenSharing;
    private bool _isPushToTalkActive;
    private string? _currentVoiceChannelId;
    private string? _currentVoiceChannelName;
    private string? _currentGuildId;
    private string _clientId = DefaultClientId;
    private string? _connectedPipeName;
    private string? _currentUsername;
    private string? _currentUserId;
    private bool _disposed;

    public bool IsConnected => _isConnected;
    public bool IsAuthenticated => _isAuthenticated;
    public bool IsMuted => _isMuted;
    public bool IsDeafened => _isDeafened;
    public bool IsCameraEnabled => _isCameraEnabled;
    public bool IsScreenSharing => _isScreenSharing;
    public bool IsPushToTalkActive => _isPushToTalkActive;
    public string? CurrentVoiceChannelId => _currentVoiceChannelId;
    public string? CurrentVoiceChannelName => _currentVoiceChannelName;
    public string? CurrentGuildId => _currentGuildId;
    public string ClientId => _clientId;
    public string? ConnectedPipeName => _connectedPipeName;
    public string? CurrentUsername => _currentUsername;
    public string? CurrentUserId => _currentUserId;

    public event Action<bool>? ConnectionStateChanged;
    public event Action<bool, bool>? VoiceStateChanged;
    public event Action<bool, bool>? VideoStateChanged;
    public event Action<bool>? PushToTalkStateChanged;
    public event Action<string?, string?>? VoiceChannelChanged;
    public event Action<string>? LogMessage;

    /// <summary>
    /// Tworzy nową instancję <see cref="DiscordService"/>.
    /// Opcjonalny parametr <paramref name="customIpcHandler"/> umożliwia deterministyczne testowanie protokołu
    /// bez wymagania uruchomionej aplikacji Discord Desktop w tle.
    /// </summary>
    public DiscordService(
        Func<DiscordIpcOpcode, string, CancellationToken, Task<(DiscordIpcOpcode Opcode, string JsonPayload)?>>? customIpcHandler = null)
    {
        _customIpcHandler = customIpcHandler;
    }

    /// <summary>
    /// Koduje ramkę protokołu Discord IPC: 8-bajtowy nagłówek Little-Endian [Opcode:u32][Length:u32] + UTF-8 JSON.
    /// </summary>
    public static byte[] EncodeFrame(DiscordIpcOpcode opcode, string jsonPayload)
    {
        byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload ?? string.Empty);
        byte[] frame = new byte[FrameHeaderSize + payloadBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)opcode);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4, 4), (uint)payloadBytes.Length);
        payloadBytes.CopyTo(frame.AsSpan(FrameHeaderSize));
        return frame;
    }

    /// <summary>
    /// Dekoduje 8-bajtowy nagłówek ramki Discord IPC (Little-Endian).
    /// </summary>
    public static bool TryDecodeFrameHeader(
        ReadOnlySpan<byte> headerBytes,
        out DiscordIpcOpcode opcode,
        out int payloadLength)
    {
        opcode = DiscordIpcOpcode.Handshake;
        payloadLength = 0;

        if (headerBytes.Length < FrameHeaderSize)
        {
            return false;
        }

        uint rawOpcode = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(0, 4));
        uint rawLength = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(4, 4));

        if (rawOpcode > (uint)DiscordIpcOpcode.Pong || rawLength > MaxPayloadSize)
        {
            return false;
        }

        opcode = (DiscordIpcOpcode)rawOpcode;
        payloadLength = (int)rawLength;
        return true;
    }

    /// <summary>
    /// Dekoduje pełną ramkę binarną Discord IPC do pary (Opcode, JsonPayload).
    /// </summary>
    public static bool TryDecodeFrame(
        ReadOnlySpan<byte> frameBytes,
        out DiscordIpcOpcode opcode,
        out string jsonPayload)
    {
        jsonPayload = string.Empty;
        if (!TryDecodeFrameHeader(frameBytes, out opcode, out int payloadLength))
        {
            return false;
        }

        if (frameBytes.Length < FrameHeaderSize + payloadLength)
        {
            return false;
        }

        jsonPayload = Encoding.UTF8.GetString(frameBytes.Slice(FrameHeaderSize, payloadLength));
        return true;
    }

    public async Task<bool> ConnectAsync(
        string? clientId = null,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        await DisconnectAsync().ConfigureAwait(false);

        _clientId = string.IsNullOrWhiteSpace(clientId) ? DefaultClientId : clientId.Trim();
        string trimmedToken = (accessToken ?? string.Empty).Trim();

        // 1. Tryb z wstrzykniętym handlerem IPC (np. testy jednostkowe)
        if (_customIpcHandler != null)
        {
            return await ConnectWithCustomHandlerAsync(_clientId, trimmedToken, cancellationToken).ConfigureAwait(false);
        }

        // Sprawdź czy Discord jest w ogóle uruchomiony w systemie przed odpytywaniem potoków
        try
        {
            if (Process.GetProcessesByName("Discord").Length == 0 &&
                Process.GetProcessesByName("DiscordCanary").Length == 0 &&
                Process.GetProcessesByName("DiscordPTB").Length == 0)
            {
                SetConnectedState(false);
                return false;
            }
        }
        catch { }

        // 2. Wyszukiwanie lokalnego potoku Named Pipe: discord-ipc-0 .. discord-ipc-9
        for (int i = 0; i < 10; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string pipeName = $"discord-ipc-{i}";
            if (!IsNamedPipeAvailable(pipeName) && i > 0)
            {
                continue;
            }

            NamedPipeClientStream? candidatePipe = null;
            try
            {
                candidatePipe = new NamedPipeClientStream(
                    serverName: ".",
                    pipeName: pipeName,
                    direction: PipeDirection.InOut,
                    options: PipeOptions.Asynchronous);

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(TimeSpan.FromMilliseconds(350));

                await candidatePipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);

                // Wyślij ramkę HANDSHAKE (Opcode = 0)
                string handshakeJson = JsonSerializer.Serialize(new
                {
                    v = 1,
                    client_id = _clientId
                });

                byte[] handshakeFrame = EncodeFrame(DiscordIpcOpcode.Handshake, handshakeJson);
                await candidatePipe.WriteAsync(handshakeFrame, cancellationToken).ConfigureAwait(false);
                await candidatePipe.FlushAsync(cancellationToken).ConfigureAwait(false);

                // Odczytaj odpowiedź READY (Opcode = 1) lub CLOSE (Opcode = 2)
                using var handshakeReadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                handshakeReadCts.CancelAfter(TimeSpan.FromSeconds(3));

                var firstFrame = await ReadFrameFromStreamAsync(candidatePipe, handshakeReadCts.Token).ConfigureAwait(false);
                if (firstFrame == null)
                {
                    candidatePipe.Dispose();
                    continue;
                }

                var (respOpcode, respJson) = firstFrame.Value;
                if (respOpcode == DiscordIpcOpcode.Close)
                {
                    LogMessage?.Invoke($"[Discord] Potok {pipeName} odrzucił Handshake: {respJson}");
                    candidatePipe.Dispose();
                    continue;
                }

                if (!TryParseReadyDispatch(respJson, out string? userId, out string? username, out string? errorMsg))
                {
                    LogMessage?.Invoke($"[Discord] Nieprawidłowa odpowiedź Handshake na {pipeName}: {errorMsg ?? respJson}");
                    candidatePipe.Dispose();
                    continue;
                }

                // Handshake zakończony sukcesem
                _pipeStream = candidatePipe;
                _connectedPipeName = pipeName;
                _currentUserId = userId;
                _currentUsername = username;

                _receiveCts = new CancellationTokenSource();
                _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));

                SetConnectedState(true);
                LogMessage?.Invoke($"[Discord] Połączono z lokalnym potokiem {pipeName} (Użytkownik: {_currentUsername ?? "nieznany"}).");

                // Opcjonalna autoryzacja OAuth2 + pobranie stanu wyciszenia
                if (!string.IsNullOrEmpty(trimmedToken))
                {
                    await AuthenticateAsync(trimmedToken, cancellationToken).ConfigureAwait(false);
                }

                // Spróbuj pobrać bieżący stan głosu i zasubskrybować zdarzenie VOICE_SETTINGS_UPDATE
                await GetVoiceSettingsAsync(cancellationToken).ConfigureAwait(false);
                await SubscribeVoiceSettingsUpdateAsync(cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                candidatePipe?.Dispose();
            }
            catch (Exception ex)
            {
                candidatePipe?.Dispose();
                LogMessage?.Invoke($"[Discord] Próba połączenia z {pipeName} nie powiodła się: {ex.Message}");
            }
        }

        LogMessage?.Invoke("[Discord] Nie wykryto uruchomionej aplikacji Discord Desktop (brak potoku discord-ipc-0..9).");
        SetConnectedState(false);
        return false;
    }

    private async Task<bool> ConnectWithCustomHandlerAsync(
        string clientId,
        string trimmedToken,
        CancellationToken cancellationToken)
    {
        try
        {
            string handshakeJson = JsonSerializer.Serialize(new
            {
                v = 1,
                client_id = clientId
            });

            var response = await _customIpcHandler!(DiscordIpcOpcode.Handshake, handshakeJson, cancellationToken).ConfigureAwait(false);
            if (response == null || response.Value.Opcode == DiscordIpcOpcode.Close)
            {
                SetConnectedState(false);
                return false;
            }

            if (!TryParseReadyDispatch(response.Value.JsonPayload, out string? userId, out string? username, out _))
            {
                SetConnectedState(false);
                return false;
            }

            _connectedPipeName = "discord-ipc-0";
            _currentUserId = userId;
            _currentUsername = username;
            SetConnectedState(true);

            if (!string.IsNullOrEmpty(trimmedToken))
            {
                await AuthenticateAsync(trimmedToken, cancellationToken).ConfigureAwait(false);
            }

            await GetVoiceSettingsAsync(cancellationToken).ConfigureAwait(false);
            await SubscribeVoiceSettingsUpdateAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Discord] Błąd połączenia w trybie niestandardowego handlera: {ex.Message}");
            SetConnectedState(false);
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _receiveCts?.Cancel();
        }
        catch
        {
            // Ignoruj
        }

        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingRequests.Clear();

        if (_pipeStream != null)
        {
            try
            {
                if (_pipeStream.IsConnected)
                {
                    byte[] closeFrame = EncodeFrame(DiscordIpcOpcode.Close, "{}");
                    await _pipeStream.WriteAsync(closeFrame).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignoruj błędy przy zamykaniu potoku
            }

            try
            {
                await _pipeStream.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignoruj
            }
            _pipeStream = null;
        }

        if (_receiveLoopTask != null)
        {
            try
            {
                await Task.WhenAny(_receiveLoopTask, Task.Delay(200)).ConfigureAwait(false);
            }
            catch
            {
                // Ignoruj
            }
            _receiveLoopTask = null;
        }

        _receiveCts?.Dispose();
        _receiveCts = null;

        _connectedPipeName = null;
        _isAuthenticated = false;
        SetConnectedState(false);
    }

    public async Task<DiscordVoiceSettings?> GetVoiceSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _disposed) return null;

        try
        {
            var dataElement = await SendRpcCommandAsync("GET_VOICE_SETTINGS", new { }, null, cancellationToken).ConfigureAwait(false);
            if (!dataElement.HasValue || dataElement.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var settings = ParseVoiceSettingsData(dataElement.Value);
            UpdateVoiceState(settings.IsMuted, settings.IsDeafened);
            return settings;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Discord] Błąd pobierania GET_VOICE_SETTINGS: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> SetMuteAsync(bool mute, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _disposed) return false;

        try
        {
            var dataElement = await SendRpcCommandAsync("SET_VOICE_SETTINGS", new { mute }, null, cancellationToken).ConfigureAwait(false);
            if (!dataElement.HasValue || dataElement.Value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var settings = ParseVoiceSettingsData(dataElement.Value);
            UpdateVoiceState(settings.IsMuted, settings.IsDeafened);
            LogMessage?.Invoke($"[Discord] Mikrofon: {(_isMuted ? "WYCISZONY (Muted)" : "AKTYWNY (Unmuted)")}");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Discord] Błąd zmiany wyciszenia mikrofonu (SET_VOICE_SETTINGS): {ex.Message}");
            return false;
        }
    }

    public Task<bool> ToggleMuteAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _disposed) return Task.FromResult(false);
        return SetMuteAsync(!_isMuted, cancellationToken);
    }

    public async Task<bool> SetDeafenAsync(bool deaf, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _disposed) return false;

        try
        {
            var dataElement = await SendRpcCommandAsync("SET_VOICE_SETTINGS", new { deaf }, null, cancellationToken).ConfigureAwait(false);
            if (!dataElement.HasValue || dataElement.Value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var settings = ParseVoiceSettingsData(dataElement.Value);
            UpdateVoiceState(settings.IsMuted, settings.IsDeafened);
            LogMessage?.Invoke($"[Discord] Odsłuch: {(_isDeafened ? "WYCISZONY (Deafened)" : "AKTYWNY (Undeafened)")}");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Discord] Błąd zmiany wyciszenia odsłuchu (SET_VOICE_SETTINGS): {ex.Message}");
            return false;
        }
    }

    public Task<bool> ToggleDeafenAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _disposed) return Task.FromResult(false);
        return SetDeafenAsync(!_isDeafened, cancellationToken);
    }

    public async Task<bool> SetCameraAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _disposed) return false;

        try
        {
            var dataElement = await SendRpcCommandAsync("TOGGLE_VIDEO", new { enabled }, null, cancellationToken).ConfigureAwait(false);
            if (!dataElement.HasValue || dataElement.Value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            bool resolvedEnabled = enabled;
            var data = dataElement.Value;
            if (data.TryGetProperty("enabled", out var enabledProp) &&
                (enabledProp.ValueKind == JsonValueKind.True || enabledProp.ValueKind == JsonValueKind.False))
            {
                resolvedEnabled = enabledProp.GetBoolean();
            }
            else if (data.TryGetProperty("video", out var videoProp) &&
                     (videoProp.ValueKind == JsonValueKind.True || videoProp.ValueKind == JsonValueKind.False))
            {
                resolvedEnabled = videoProp.GetBoolean();
            }
            else if (data.TryGetProperty("camera_enabled", out var camProp) &&
                     (camProp.ValueKind == JsonValueKind.True || camProp.ValueKind == JsonValueKind.False))
            {
                resolvedEnabled = camProp.GetBoolean();
            }

            UpdateVideoState(resolvedEnabled, _isScreenSharing);
            LogMessage?.Invoke($"[Discord] Kamera: {(_isCameraEnabled ? "WŁĄCZONA (Camera ON)" : "WYŁĄCZONA (Camera OFF)")}");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Discord] Błąd zmiany stanu kamery (TOGGLE_VIDEO): {ex.Message}");
            return false;
        }
    }

    public Task<bool> ToggleCameraAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _disposed) return Task.FromResult(false);
        return SetCameraAsync(!_isCameraEnabled, cancellationToken);
    }

    public async Task<bool> SetScreenShareAsync(bool active, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _disposed) return false;

        try
        {
            var dataElement = await SendRpcCommandAsync("TOGGLE_SCREENSHARE", new { active }, null, cancellationToken).ConfigureAwait(false);
            if (!dataElement.HasValue || dataElement.Value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            bool resolvedActive = active;
            var data = dataElement.Value;
            if (data.TryGetProperty("active", out var activeProp) &&
                (activeProp.ValueKind == JsonValueKind.True || activeProp.ValueKind == JsonValueKind.False))
            {
                resolvedActive = activeProp.GetBoolean();
            }
            else if (data.TryGetProperty("screenshare", out var screenProp) &&
                     (screenProp.ValueKind == JsonValueKind.True || screenProp.ValueKind == JsonValueKind.False))
            {
                resolvedActive = screenProp.GetBoolean();
            }
            else if (data.TryGetProperty("screen_share", out var ssProp) &&
                     (ssProp.ValueKind == JsonValueKind.True || ssProp.ValueKind == JsonValueKind.False))
            {
                resolvedActive = ssProp.GetBoolean();
            }

            UpdateVideoState(_isCameraEnabled, resolvedActive);
            LogMessage?.Invoke($"[Discord] Udostępnianie ekranu: {(_isScreenSharing ? "AKTYWNE (Go Live ON)" : "ZATRZYMANE (Go Live OFF)")}");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Discord] Błąd zmiany stanu udostępniania ekranu (TOGGLE_SCREENSHARE): {ex.Message}");
            return false;
        }
    }

    public Task<bool> ToggleScreenShareAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _disposed) return Task.FromResult(false);
        return SetScreenShareAsync(!_isScreenSharing, cancellationToken);
    }

    public async Task<bool> SetPushToTalkActiveAsync(bool active, CancellationToken cancellationToken = default)
    {
        bool changed = _isPushToTalkActive != active;
        _isPushToTalkActive = active;
        if (changed)
        {
            PushToTalkStateChanged?.Invoke(active);
        }

        if (!_isConnected || _disposed) return true;

        try
        {
            // Na Discordzie PTT aktywuje nadawanie głosu (mute = false) lub je blokuje (mute = true)
            var dataElement = await SendRpcCommandAsync("SET_VOICE_SETTINGS", new { mute = !active }, null, cancellationToken).ConfigureAwait(false);
            if (dataElement.HasValue && dataElement.Value.ValueKind == JsonValueKind.Object)
            {
                var settings = ParseVoiceSettingsData(dataElement.Value);
                UpdateVoiceState(settings.IsMuted, settings.IsDeafened);
            }
            LogMessage?.Invoke($"[Discord] Push-To-Talk: {(active ? "NADAWANIE (Active)" : "CISZA (Muted)")}");
            return true;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Discord] Błąd Push-To-Talk (SET_VOICE_SETTINGS): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Dołącza do wskazanego kanału głosowego w Discord Desktop (SELECT_VOICE_CHANNEL) (Etap 5.5).
    /// Obsługuje surowe ID kanału (Snowflake) lub linki URL Discord (np. https://discord.com/channels/123/456).
    /// Jeśli parametr <paramref name="channelId"/> jest null lub pusty, następuje opuszczenie kanału głosowego.
    /// </summary>
    public async Task<bool> SelectVoiceChannelAsync(
        string? channelId,
        bool force = true,
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _disposed) return false;

        try
        {
            string? cleanChannelId = ExtractChannelId(channelId);

            var dataElement = await SendRpcCommandAsync(
                "SELECT_VOICE_CHANNEL",
                new { channel_id = cleanChannelId, force },
                null,
                cancellationToken).ConfigureAwait(false);

            if (dataElement.HasValue && dataElement.Value.ValueKind == JsonValueKind.Object)
            {
                var data = dataElement.Value;
                string? resolvedId = data.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                    ? idProp.GetString()
                    : cleanChannelId;
                string? resolvedName = data.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                    ? nameProp.GetString()
                    : null;
                string? resolvedGuild = data.TryGetProperty("guild_id", out var gProp) && gProp.ValueKind == JsonValueKind.String
                    ? gProp.GetString()
                    : null;

                UpdateVoiceChannelState(resolvedId, resolvedName, resolvedGuild);
                LogMessage?.Invoke($"[Discord] Dołączono do kanału głosowego: {resolvedName ?? resolvedId ?? "Kanał"} (ID: {resolvedId})");
                return true;
            }
            else if (string.IsNullOrWhiteSpace(cleanChannelId))
            {
                UpdateVoiceChannelState(null, null, null);
                LogMessage?.Invoke("[Discord] Rozłączono z kanału głosowego.");
                return true;
            }
            else
            {
                UpdateVoiceChannelState(cleanChannelId, null, null);
                LogMessage?.Invoke($"[Discord] Wybrano kanał głosowy: {cleanChannelId}");
                return true;
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Discord] Błąd dołączania do kanału głosowego (SELECT_VOICE_CHANNEL): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opuszcza bieżący kanał głosowy w Discord Desktop (SELECT_VOICE_CHANNEL channel_id=null) (Etap 5.5).
    /// </summary>
    public Task<bool> LeaveVoiceChannelAsync(CancellationToken cancellationToken = default)
    {
        return SelectVoiceChannelAsync(null, true, cancellationToken);
    }

    /// <summary>
    /// Wyodrębnia identyfikator kanału Discord (ciąg cyfr) z podanego ciągu znaków lub linku URL.
    /// Obsługuje linki https://discord.com/channels/guild/channel, discord://-/channels/guild/channel oraz czyste ID.
    /// </summary>
    public static string? ExtractChannelId(string? input)
    {
        var (_, channelId) = ExtractGuildAndChannelId(input);
        return channelId;
    }

    /// <summary>
    /// Wyodrębnia identyfikator serwera (Guild ID) oraz kanału (Channel ID) z linku URL lub ciągu znaków.
    /// Obsługuje linki https://discord.com/channels/guild/channel, discord://-/channels/guild/channel oraz formaty "guild/channel" lub samo ID.
    /// </summary>
    public static (string? GuildId, string? ChannelId) ExtractGuildAndChannelId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (null, null);
        }

        string trimmed = input.Trim().TrimEnd('/');

        if (trimmed.Contains("/channels/", StringComparison.OrdinalIgnoreCase))
        {
            int idx = trimmed.IndexOf("/channels/", StringComparison.OrdinalIgnoreCase);
            string sub = trimmed.Substring(idx + "/channels/".Length);
            string[] parts = sub.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return (parts[0], parts[1]);
            }
            else if (parts.Length == 1)
            {
                return (null, parts[0]);
            }
        }
        else if (trimmed.Contains('/'))
        {
            string[] parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return (parts[0], parts[1]);
            }
            else if (parts.Length == 1)
            {
                return (null, parts[0]);
            }
        }

        return (null, trimmed);
    }

    /// <summary>
    /// Przetwarza przychodzącą ramkę IPC (np. zdarzenie DISPATCH VOICE_SETTINGS_UPDATE lub odpowiedź na komendę).
    /// Udostępnione również do testowania asynchronicznych zdarzeń z Discord Desktop.
    /// </summary>
    public void ProcessIncomingFrame(DiscordIpcOpcode opcode, string jsonPayload)
    {
        if (opcode == DiscordIpcOpcode.Close)
        {
            LogMessage?.Invoke("[Discord] Serwer Discord IPC zamknął połączenie.");
            SetConnectedState(false);
            return;
        }

        if (opcode != DiscordIpcOpcode.Frame || string.IsNullOrWhiteSpace(jsonPayload))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);
            var root = doc.RootElement;

            string? cmd = root.TryGetProperty("cmd", out var cmdProp) && cmdProp.ValueKind == JsonValueKind.String
                ? cmdProp.GetString()
                : null;
            string? evt = root.TryGetProperty("evt", out var evtProp) && evtProp.ValueKind == JsonValueKind.String
                ? evtProp.GetString()
                : null;
            string? nonce = root.TryGetProperty("nonce", out var nonceProp) && nonceProp.ValueKind == JsonValueKind.String
                ? nonceProp.GetString()
                : null;

            // 1. Jeśli ramka posiada nonce pasujący do oczekującego żądania RPC
            if (!string.IsNullOrEmpty(nonce) && _pendingRequests.TryRemove(nonce, out var tcs))
            {
                if (string.Equals(evt, "ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    tcs.TrySetResult(null);
                    return;
                }

                if (root.TryGetProperty("data", out var dataProp))
                {
                    tcs.TrySetResult(dataProp.Clone());
                }
                else
                {
                    tcs.TrySetResult(root.Clone());
                }
            }

            // 2. Obsługa zdarzeń DISPATCH (np. VOICE_SETTINGS_UPDATE / VIDEO_STATE_UPDATE / VOICE_CHANNEL_SELECT)
            if (string.Equals(cmd, "DISPATCH", StringComparison.OrdinalIgnoreCase) &&
                root.TryGetProperty("data", out var dispatchData) &&
                dispatchData.ValueKind == JsonValueKind.Object)
            {
                if (string.Equals(evt, "VOICE_SETTINGS_UPDATE", StringComparison.OrdinalIgnoreCase))
                {
                    var settings = ParseVoiceSettingsData(dispatchData);
                    UpdateVoiceState(settings.IsMuted, settings.IsDeafened);
                }
                else if (string.Equals(evt, "VIDEO_STATE_UPDATE", StringComparison.OrdinalIgnoreCase))
                {
                    bool cam = _isCameraEnabled;
                    bool screen = _isScreenSharing;
                    if (dispatchData.TryGetProperty("video", out var vidProp) &&
                        (vidProp.ValueKind == JsonValueKind.True || vidProp.ValueKind == JsonValueKind.False))
                    {
                        cam = vidProp.GetBoolean();
                    }
                    else if (dispatchData.TryGetProperty("enabled", out var enProp) &&
                             (enProp.ValueKind == JsonValueKind.True || enProp.ValueKind == JsonValueKind.False))
                    {
                        cam = enProp.GetBoolean();
                    }

                    if (dispatchData.TryGetProperty("screenshare", out var scrProp) &&
                        (scrProp.ValueKind == JsonValueKind.True || scrProp.ValueKind == JsonValueKind.False))
                    {
                        screen = scrProp.GetBoolean();
                    }
                    else if (dispatchData.TryGetProperty("active", out var actProp) &&
                             (actProp.ValueKind == JsonValueKind.True || actProp.ValueKind == JsonValueKind.False))
                    {
                        screen = actProp.GetBoolean();
                    }

                    UpdateVideoState(cam, screen);
                }
                else if (string.Equals(evt, "VOICE_CHANNEL_SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    string? chId = dispatchData.TryGetProperty("channel_id", out var cProp) && cProp.ValueKind == JsonValueKind.String
                        ? cProp.GetString()
                        : null;
                    string? gId = dispatchData.TryGetProperty("guild_id", out var gProp) && gProp.ValueKind == JsonValueKind.String
                        ? gProp.GetString()
                        : null;
                    UpdateVoiceChannelState(chId, null, gId);
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Discord] Błąd parsowania ramki IPC: {ex.Message}");
        }
    }

    private async Task<bool> AuthenticateAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            var data = await SendRpcCommandAsync("AUTHENTICATE", new { access_token = accessToken }, null, cancellationToken).ConfigureAwait(false);
            if (data.HasValue && data.Value.ValueKind == JsonValueKind.Object)
            {
                _isAuthenticated = true;
                if (data.Value.TryGetProperty("user", out var userProp) && userProp.ValueKind == JsonValueKind.Object)
                {
                    if (userProp.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                        _currentUserId = idProp.GetString();
                    if (userProp.TryGetProperty("username", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                        _currentUsername = nameProp.GetString();
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Discord] Błąd uwierzytelniania OAuth2: {ex.Message}");
        }

        _isAuthenticated = false;
        return false;
    }

    public async Task<(bool Success, string? AccessToken, string? Error)> AuthorizeAndAuthenticateAsync(
        string clientId,
        string clientSecret,
        string redirectUri = "https://localhost/",
        CancellationToken cancellationToken = default)
    {
        // Zawsze rozłącz i połącz na nowo, aby Handshake IPC miał poprawny Client ID aplikacji
        await DisconnectAsync().ConfigureAwait(false);
        bool connected = await ConnectAsync(clientId, null, cancellationToken).ConfigureAwait(false);
        if (!connected)
        {
            return (false, null, "Nie można połączyć się z aplikacją Discord (upewnij się, że Discord Desktop jest włączony).");
        }

        try
        {
            LogMessage?.Invoke("[Discord] Wysyłanie żądania AUTHORIZE do Discord Desktop...");
            var authData = await SendRpcCommandAsync("AUTHORIZE", new
            {
                client_id = _clientId,
                scopes = new[] { "rpc", "rpc.voice.read", "rpc.voice.write" },
                prompt = "consent"
            }, null, cancellationToken).ConfigureAwait(false);

            if (!authData.HasValue || authData.Value.ValueKind != JsonValueKind.Object)
            {
                return (false, null, "Discord odrzucił żądanie autoryzacji lub użytkownik anulował zgłoszenie.");
            }

            if (!authData.Value.TryGetProperty("code", out var codeProp) || codeProp.ValueKind != JsonValueKind.String)
            {
                return (false, null, "Nie otrzymano kodu autoryzacyjnego z Discorda.");
            }

            string code = codeProp.GetString()!;
            LogMessage?.Invoke("[Discord] Otrzymano kod autoryzacyjny. Wymiana na token OAuth2...");

            using var httpClient = new HttpClient();
            var dict = new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://discord.com/api/oauth2/token")
            {
                Content = new FormUrlEncodedContent(dict)
            };

            using var resp = await httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
            string respContent = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                LogMessage?.Invoke($"[Discord] Błąd wymiany tokenu OAuth2: {resp.StatusCode} - {respContent}");
                return (false, null, $"Błąd wymiany tokenu ({resp.StatusCode}): {respContent}");
            }

            using var doc = JsonDocument.Parse(respContent);
            if (!doc.RootElement.TryGetProperty("access_token", out var tokenProp) || tokenProp.ValueKind != JsonValueKind.String)
            {
                return (false, null, "Odpowiedź Discord OAuth2 nie zawierała pola access_token.");
            }

            string accessToken = tokenProp.GetString()!;
            LogMessage?.Invoke("[Discord] Uzyskano Access Token. Uwierzytelnianie sesji IPC...");

            bool authSuccess = await AuthenticateAsync(accessToken, cancellationToken).ConfigureAwait(false);
            if (authSuccess)
            {
                await GetVoiceSettingsAsync(cancellationToken).ConfigureAwait(false);
                await SubscribeVoiceSettingsUpdateAsync(cancellationToken).ConfigureAwait(false);
                LogMessage?.Invoke("[Discord] Pomyślnie zautoryzowano i uwierzytelniono natywną integrację głosową!");
                return (true, accessToken, null);
            }

            return (false, null, "Uwierzytelnienie sesji IPC uzyskanym tokenem nie powiodło się.");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Discord] Wyjątek podczas autoryzacji: {ex.Message}");
            return (false, null, ex.Message);
        }
    }

    private async Task SubscribeVoiceSettingsUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SendRpcCommandAsync("SUBSCRIBE", new { }, "VOICE_SETTINGS_UPDATE", cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Subskrypcja może wymagać autoryzacji OAuth2 - bezpiecznie ignorujemy błąd
        }
    }

    private async Task<JsonElement?> SendRpcCommandAsync(
        string command,
        object args,
        string? evt,
        CancellationToken cancellationToken)
    {
        if (!_isConnected || _disposed) return null;

        string nonce = Interlocked.Increment(ref _nextNonceId).ToString();
        string requestJson = evt != null
            ? JsonSerializer.Serialize(new { cmd = command, args, evt, nonce })
            : JsonSerializer.Serialize(new { cmd = command, args, nonce });

        if (_customIpcHandler != null)
        {
            var customResp = await _customIpcHandler(DiscordIpcOpcode.Frame, requestJson, cancellationToken).ConfigureAwait(false);
            if (customResp == null || customResp.Value.Opcode != DiscordIpcOpcode.Frame || string.IsNullOrWhiteSpace(customResp.Value.JsonPayload))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(customResp.Value.JsonPayload);
            var root = doc.RootElement;
            if (root.TryGetProperty("evt", out var evtProp) &&
                evtProp.ValueKind == JsonValueKind.String &&
                string.Equals(evtProp.GetString(), "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (root.TryGetProperty("data", out var dataProp))
            {
                return dataProp.Clone();
            }

            return root.Clone();
        }

        var pipe = _pipeStream;
        if (pipe == null || !pipe.IsConnected) return null;

        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[nonce] = tcs;

        try
        {
            byte[] frameBytes = EncodeFrame(DiscordIpcOpcode.Frame, requestJson);
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await pipe.WriteAsync(frameBytes, cancellationToken).ConfigureAwait(false);
                await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            TimeSpan timeout = string.Equals(command, "AUTHORIZE", StringComparison.OrdinalIgnoreCase)
                ? TimeSpan.FromSeconds(60)
                : TimeSpan.FromSeconds(3);
            timeoutCts.CancelAfter(timeout);
            using var reg = timeoutCts.Token.Register(() => tcs.TrySetResult(null));

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingRequests.TryRemove(nonce, out _);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var pipe = _pipeStream;
        if (pipe == null) return;

        try
        {
            while (!cancellationToken.IsCancellationRequested && pipe.IsConnected)
            {
                var frame = await ReadFrameFromStreamAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (frame == null)
                {
                    break;
                }

                var (opcode, jsonPayload) = frame.Value;
                if (opcode == DiscordIpcOpcode.Ping)
                {
                    byte[] pongFrame = EncodeFrame(DiscordIpcOpcode.Pong, jsonPayload);
                    await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await pipe.WriteAsync(pongFrame, cancellationToken).ConfigureAwait(false);
                        await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        _writeLock.Release();
                    }
                    continue;
                }

                ProcessIncomingFrame(opcode, jsonPayload);

                if (opcode == DiscordIpcOpcode.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normalne zakończenie pętli nasłuchu
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[Discord] Przerwano pętlę odczytu IPC: {ex.Message}");
        }
        finally
        {
            if (_isConnected)
            {
                SetConnectedState(false);
            }
        }
    }

    private static async Task<(DiscordIpcOpcode Opcode, string JsonPayload)?> ReadFrameFromStreamAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] headerBuffer = new byte[FrameHeaderSize];
        int headerRead = await ReadExactAsync(stream, headerBuffer, cancellationToken).ConfigureAwait(false);
        if (headerRead < FrameHeaderSize)
        {
            return null;
        }

        if (!TryDecodeFrameHeader(headerBuffer, out DiscordIpcOpcode opcode, out int payloadLength))
        {
            return null;
        }

        if (payloadLength == 0)
        {
            return (opcode, string.Empty);
        }

        byte[] payloadBuffer = new byte[payloadLength];
        int payloadRead = await ReadExactAsync(stream, payloadBuffer, cancellationToken).ConfigureAwait(false);
        if (payloadRead < payloadLength)
        {
            return null;
        }

        string json = Encoding.UTF8.GetString(payloadBuffer);
        return (opcode, json);
    }

    private static async Task<int> ReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead),
                cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }
        return totalRead;
    }

    private static bool TryParseReadyDispatch(
        string jsonPayload,
        out string? userId,
        out string? username,
        out string? errorMessage)
    {
        userId = null;
        username = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            errorMessage = "Pusta odpowiedź Handshake.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);
            var root = doc.RootElement;

            if (root.TryGetProperty("evt", out var evtProp) &&
                evtProp.ValueKind == JsonValueKind.String &&
                string.Equals(evtProp.GetString(), "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("data", out var errData) &&
                    errData.ValueKind == JsonValueKind.Object &&
                    errData.TryGetProperty("message", out var msgProp) &&
                    msgProp.ValueKind == JsonValueKind.String)
                {
                    errorMessage = msgProp.GetString();
                }
                else
                {
                    errorMessage = "Zwrócono zdarzenie ERROR z Discord IPC.";
                }
                return false;
            }

            if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Object)
            {
                if (dataProp.TryGetProperty("user", out var userProp) && userProp.ValueKind == JsonValueKind.Object)
                {
                    if (userProp.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    {
                        userId = idProp.GetString();
                    }

                    if (userProp.TryGetProperty("global_name", out var globalNameProp) &&
                        globalNameProp.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(globalNameProp.GetString()))
                    {
                        username = globalNameProp.GetString();
                    }
                    else if (userProp.TryGetProperty("username", out var usernameProp) &&
                             usernameProp.ValueKind == JsonValueKind.String)
                    {
                        username = usernameProp.GetString();
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static DiscordVoiceSettings ParseVoiceSettingsData(JsonElement data)
    {
        bool deaf = data.TryGetProperty("deaf", out var deafProp) &&
                    (deafProp.ValueKind == JsonValueKind.True ||
                     (deafProp.ValueKind == JsonValueKind.False ? false : false));

        bool mute = deaf || (data.TryGetProperty("mute", out var muteProp) && muteProp.ValueKind == JsonValueKind.True);

        double inputVol = 100.0;
        if (data.TryGetProperty("input", out var inputProp) &&
            inputProp.ValueKind == JsonValueKind.Object &&
            inputProp.TryGetProperty("volume", out var inVolProp) &&
            inVolProp.TryGetDouble(out double parsedInVol))
        {
            inputVol = parsedInVol;
        }

        double outputVol = 100.0;
        if (data.TryGetProperty("output", out var outputProp) &&
            outputProp.ValueKind == JsonValueKind.Object &&
            outputProp.TryGetProperty("volume", out var outVolProp) &&
            outVolProp.TryGetDouble(out double parsedOutVol))
        {
            outputVol = parsedOutVol;
        }

        string? modeType = null;
        if (data.TryGetProperty("mode", out var modeProp) &&
            modeProp.ValueKind == JsonValueKind.Object &&
            modeProp.TryGetProperty("type", out var typeProp) &&
            typeProp.ValueKind == JsonValueKind.String)
        {
            modeType = typeProp.GetString();
        }

        return new DiscordVoiceSettings(mute, deaf, inputVol, outputVol, modeType);
    }

    private void UpdateVoiceState(bool isMuted, bool isDeafened)
    {
        bool EffectiveMuted = isMuted || isDeafened;
        bool changed = _isMuted != EffectiveMuted || _isDeafened != isDeafened;
        _isMuted = EffectiveMuted;
        _isDeafened = isDeafened;

        if (changed)
        {
            VoiceStateChanged?.Invoke(_isMuted, _isDeafened);
        }
    }

    private void UpdateVideoState(bool isCameraEnabled, bool isScreenSharing)
    {
        bool changed = _isCameraEnabled != isCameraEnabled || _isScreenSharing != isScreenSharing;
        _isCameraEnabled = isCameraEnabled;
        _isScreenSharing = isScreenSharing;

        if (changed)
        {
            VideoStateChanged?.Invoke(_isCameraEnabled, _isScreenSharing);
        }
    }

    private void UpdateVoiceChannelState(string? channelId, string? channelName, string? guildId)
    {
        bool changed = !string.Equals(_currentVoiceChannelId, channelId, StringComparison.OrdinalIgnoreCase) ||
                       !string.Equals(_currentGuildId, guildId, StringComparison.OrdinalIgnoreCase);
        _currentVoiceChannelId = channelId;
        _currentVoiceChannelName = channelName ?? (string.IsNullOrWhiteSpace(channelId) ? null : _currentVoiceChannelName);
        _currentGuildId = guildId ?? (string.IsNullOrWhiteSpace(channelId) ? null : _currentGuildId);

        if (changed)
        {
            VoiceChannelChanged?.Invoke(_currentVoiceChannelId, _currentGuildId);
        }
    }

    private void SetConnectedState(bool connected)
    {
        if (_isConnected == connected) return;
        _isConnected = connected;
        if (!connected)
        {
            _isAuthenticated = false;
            _isCameraEnabled = false;
            _isScreenSharing = false;
            _isPushToTalkActive = false;
            _currentVoiceChannelId = null;
            _currentVoiceChannelName = null;
            _currentGuildId = null;
            VoiceChannelChanged?.Invoke(null, null);
        }
        ConnectionStateChanged?.Invoke(connected);
    }

    private static bool IsNamedPipeAvailable(string pipeName)
    {
        try
        {
            string[] matches = Directory.GetFiles(@"\\.\pipe\", pipeName);
            return matches.Length > 0;
        }
        catch
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            DisconnectAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignoruj
        }

        _writeLock.Dispose();
    }
}
