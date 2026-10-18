namespace MasterHub.Core.Integrations.Voicemod;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Audio;
using MasterHub.Core.Profiles;
using MasterHub.Core.SystemControl;

public class VoicemodService : IVoicemodService
{
    private readonly IProfileService _profileService;
    private readonly ISystemControlService _systemControl;
    private readonly IAudioService? _audioService;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _stateLock = new();

    public static readonly int[] OfficialVoicemodPorts =
    [
        59129, 20000, 39273, 42152, 43782, 46667, 35679, 37170, 38501, 33952, 30546
    ];

    private VoicemodSettingsModel _settings = new();
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _connectionCts;
    private Timer? _reconnectTimer;
    private bool _isConnecting;
    private TaskCompletionSource<(bool Success, string Message)>? _registerTcs;
    private int _activePort;
    private readonly List<VoicemodVoiceItem> _voices = new();
    private readonly List<VoicemodSoundItem> _sounds = new();

    public bool IsConnected { get; private set; }
    public bool IsVoiceChangerEnabled { get; private set; }
    public bool IsHearMyVoiceEnabled { get; private set; }
    public bool IsMicMuted { get; private set; }
    public bool IsBackgroundEffectsEnabled { get; private set; } = true;
    public string? CurrentVoiceId { get; private set; }
    public string? CurrentVoiceName { get; private set; }

    public IReadOnlyList<VoicemodVoiceItem> AvailableVoices
    {
        get
        {
            lock (_stateLock)
            {
                return _voices.Count > 0 ? _voices.ToList() : VoicemodDefaults.StandardVoices;
            }
        }
    }

    public IReadOnlyList<VoicemodSoundItem> AvailableSounds
    {
        get
        {
            lock (_stateLock)
            {
                return _sounds.Count > 0 ? _sounds.ToList() : VoicemodDefaults.StandardSounds;
            }
        }
    }

    public VoicemodState CurrentState => new(
        IsConnected,
        IsVoiceChangerEnabled,
        IsHearMyVoiceEnabled,
        IsMicMuted,
        CurrentVoiceId,
        CurrentVoiceName,
        IsBackgroundEffectsEnabled);

    public event Action<bool>? ConnectionStateChanged;
    public event Action<VoicemodState>? StateChanged;
    public event Action<IReadOnlyList<VoicemodVoiceItem>>? VoicesLoaded;
    public event Action<IReadOnlyList<VoicemodSoundItem>>? SoundsLoaded;
    public event Action<string>? SoundPlayed;

    public VoicemodService(
        IProfileService profileService,
        ISystemControlService systemControl,
        IAudioService? audioService = null)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _systemControl = systemControl ?? throw new ArgumentNullException(nameof(systemControl));
        _audioService = audioService;

        var loaded = _profileService.GetVoicemodSettings();
        if (loaded != null)
        {
            _settings = loaded;
        }

        if (_settings.AutoConnect)
        {
            StartMonitoring();
        }
    }

    public void Configure(VoicemodSettingsModel settings)
    {
        lock (_stateLock)
        {
            _settings = settings ?? new VoicemodSettingsModel();
        }

        if (_settings.AutoConnect)
        {
            StartMonitoring();
        }
        else if (!IsConnected)
        {
            StopMonitoring();
        }
    }

    public async Task<(bool Success, string Message)> ConnectAsync(CancellationToken ct = default)
    {
        string host;
        int preferredPort;
        string apiKey;

        lock (_stateLock)
        {
            host = string.IsNullOrWhiteSpace(_settings.Host) ? "127.0.0.1" : _settings.Host.Trim();
            preferredPort = _settings.Port <= 0 ? 59129 : _settings.Port;
            apiKey = _settings.ApiKey?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Equals("MasterHub-StreamDeck", StringComparison.OrdinalIgnoreCase))
            {
                apiKey = "loupedeck";
            }
        }

        var candidatePorts = new List<int> { preferredPort };
        foreach (int p in OfficialVoicemodPorts)
        {
            if (!candidatePorts.Contains(p))
            {
                candidatePorts.Add(p);
            }
        }

        await DisconnectInternalAsync().ConfigureAwait(false);

        foreach (int port in candidatePorts)
        {
            if (ct.IsCancellationRequested) break;

            ClientWebSocket? ws = null;
            try
            {
                ws = new ClientWebSocket();
                using var portCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, portCts.Token);

                Uri uri = new($"ws://{host}:{port}/v1/");
                await ws.ConnectAsync(uri, linkedCts.Token).ConfigureAwait(false);

                _webSocket = ws;
                _connectionCts = new CancellationTokenSource();
                _activePort = port;
                _registerTcs = new TaskCompletionSource<(bool Success, string Message)>(TaskCreationOptions.RunContinuationsAsynchronously);

                // Start receiving loop
                _ = Task.Run(() => ReceiveLoopAsync(_webSocket, _connectionCts.Token));

                // Send registerClient as first message
                await SendJsonMessageAsync("registerClient", new { clientKey = apiKey }, CancellationToken.None).ConfigureAwait(false);

                // Await registration / handshake (up to 2.5 seconds)
                var registerTask = _registerTcs.Task;
                var completedTask = await Task.WhenAny(registerTask, Task.Delay(2500, ct)).ConfigureAwait(false);

                if (completedTask == registerTask)
                {
                    var (authSuccess, authMsg) = await registerTask.ConfigureAwait(false);
                    if (authSuccess)
                    {
                        lock (_stateLock)
                        {
                            _settings.Port = port;
                        }
                        SetConnected(true);
                        return (true, $"Połączono z Voicemod na {host}:{port} ({authMsg}).");
                    }
                    else
                    {
                        // Registration explicitly failed
                        await DisconnectInternalAsync().ConfigureAwait(false);
                        return (false, authMsg);
                    }
                }
                else
                {
                    // No explicit rejection within 2.5s and socket is open
                    lock (_stateLock)
                    {
                        _settings.Port = port;
                    }
                    SetConnected(true);
                    _ = Task.Run(async () =>
                    {
                        await RefreshStateAsync(CancellationToken.None).ConfigureAwait(false);
                        await GetVoicesAsync(CancellationToken.None).ConfigureAwait(false);
                        await GetSoundsAsync(CancellationToken.None).ConfigureAwait(false);
                    });
                    return (true, $"Połączono z Voicemod na {host}:{port}.");
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { ws?.Dispose(); } catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VoicemodService] Port {port} connection failed: {ex.Message}");
                try { ws?.Dispose(); } catch { }
            }
        }

        SetConnected(false);
        return (false, $"Nie udało się połączyć z Voicemod na żadnym ze zdefiniowanych portów ({string.Join(", ", candidatePorts)}). Upewnij się, że Voicemod jest uruchomiony na komputerze.");
    }

    public async Task DisconnectAsync()
    {
        StopMonitoring();
        await DisconnectInternalAsync().ConfigureAwait(false);
        SetConnected(false);
    }

    private async Task DisconnectInternalAsync()
    {
        _registerTcs?.TrySetCanceled();
        _registerTcs = null;

        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = null;

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open || _webSocket.State == WebSocketState.CloseReceived)
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", timeoutCts.Token).ConfigureAwait(false);
                }
            }
            catch { }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
            }
        }
    }

    public async Task<bool> SelectVoiceAsync(string voiceIdOrName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(voiceIdOrName)) return false;

        string targetId = voiceIdOrName.Trim();

        lock (_stateLock)
        {
            var match = _voices.FirstOrDefault(v =>
                string.Equals(v.Id, targetId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v.Name, targetId, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                targetId = match.Id;
            }
        }

        if (IsConnected)
        {
            bool ok = await SendJsonMessageAsync("loadVoice", new { voiceID = targetId }, ct).ConfigureAwait(false);
            if (ok)
            {
                CurrentVoiceId = targetId;
                UpdateCurrentVoiceName();
                NotifyStateChanged();
                return true;
            }
        }

        return false;
    }

    public async Task<bool> ToggleVoiceChangerAsync(CancellationToken ct = default)
    {
        if (IsConnected)
        {
            bool ok = await SendJsonMessageAsync("toggleVoiceChanger", null, ct).ConfigureAwait(false);
            if (ok) return true;
        }

        if (_settings.FallbackToDesktopHotkeys)
        {
            IsVoiceChangerEnabled = !IsVoiceChangerEnabled;
            NotifyStateChanged();
            return true;
        }

        return false;
    }

    public async Task<bool> ToggleHearMyVoiceAsync(CancellationToken ct = default)
    {
        if (IsConnected)
        {
            bool ok = await SendJsonMessageAsync("toggleHearMyVoice", null, ct).ConfigureAwait(false);
            if (ok) return true;
        }

        if (_settings.FallbackToDesktopHotkeys)
        {
            IsHearMyVoiceEnabled = !IsHearMyVoiceEnabled;
            NotifyStateChanged();
            return true;
        }

        return false;
    }

    public async Task<bool> ToggleMuteMicAsync(CancellationToken ct = default)
    {
        if (IsConnected)
        {
            bool ok = await SendJsonMessageAsync("toggleMuteMic", null, ct).ConfigureAwait(false);
            if (ok) return true;
        }

        if (_settings.FallbackToDesktopHotkeys)
        {
            if (_audioService != null)
            {
                IsMicMuted = _audioService.ToggleMasterRecordingMute();
            }
            else
            {
                IsMicMuted = !IsMicMuted;
            }
            NotifyStateChanged();
            return true;
        }

        return false;
    }

    public async Task<bool> ToggleBackgroundEffectsAsync(CancellationToken ct = default)
    {
        if (IsConnected)
        {
            bool ok = await SendJsonMessageAsync("toggleBackground", null, ct).ConfigureAwait(false);
            if (!ok)
            {
                ok = await SendJsonMessageAsync("toggleBackgroundEffect", null, ct).ConfigureAwait(false);
            }
            if (!ok)
            {
                ok = await SendJsonMessageAsync("toggleBackgroundEffects", null, ct).ConfigureAwait(false);
            }
            if (ok) return true;
        }

        if (_settings.FallbackToDesktopHotkeys)
        {
            IsBackgroundEffectsEnabled = !IsBackgroundEffectsEnabled;
            NotifyStateChanged();
            return true;
        }

        return false;
    }

    public async Task<bool> SetBackgroundEffectsAsync(bool enabled, CancellationToken ct = default)
    {
        if (IsConnected)
        {
            bool ok = await SendJsonMessageAsync("setBackgroundEffect", new { value = enabled }, ct).ConfigureAwait(false);
            if (!ok)
            {
                ok = await SendJsonMessageAsync("setBackgroundEffects", new { value = enabled }, ct).ConfigureAwait(false);
            }
            if (ok)
            {
                IsBackgroundEffectsEnabled = enabled;
                NotifyStateChanged();
                return true;
            }
        }

        IsBackgroundEffectsEnabled = enabled;
        NotifyStateChanged();
        return true;
    }

    public async Task<IReadOnlyList<VoicemodVoiceItem>> GetVoicesAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return AvailableVoices;

        await SendJsonMessageAsync("getVoices", null, ct).ConfigureAwait(false);
        return AvailableVoices;
    }

    public async Task<VoicemodState> RefreshStateAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return CurrentState;

        await SendJsonMessageAsync("getVoiceChangerStatus", null, ct).ConfigureAwait(false);
        await SendJsonMessageAsync("getHearMyselfStatus", null, ct).ConfigureAwait(false);
        await SendJsonMessageAsync("getHearMyVoiceStatus", null, ct).ConfigureAwait(false);
        await SendJsonMessageAsync("getMuteMicStatus", null, ct).ConfigureAwait(false);
        await SendJsonMessageAsync("getBackgroundEffectStatus", null, ct).ConfigureAwait(false);
        await SendJsonMessageAsync("getCurrentVoice", null, ct).ConfigureAwait(false);

        return CurrentState;
    }

    public async Task<bool> PlaySoundAsync(string soundFileNameOrName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(soundFileNameOrName)) return false;

        string targetFileName = soundFileNameOrName.Trim();

        lock (_stateLock)
        {
            var match = _sounds.FirstOrDefault(s =>
                string.Equals(s.FileName, targetFileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, targetFileName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                targetFileName = match.FileName;
            }
        }

        if (IsConnected)
        {
            bool ok = await SendJsonMessageAsync("playMeme", new { FileName = targetFileName, fileName = targetFileName, IsKeyDown = true }, ct).ConfigureAwait(false);
            if (ok)
            {
                SoundPlayed?.Invoke(targetFileName);
                return true;
            }
        }

        if (_settings.FallbackToDesktopHotkeys)
        {
            try
            {
                if (File.Exists(targetFileName))
                {
                    _systemControl.LaunchProcess(targetFileName);
                    SoundPlayed?.Invoke(targetFileName);
                    return true;
                }
            }
            catch { }

            try
            {
                Console.Beep();
            }
            catch { }

            SoundPlayed?.Invoke(targetFileName);
            return true;
        }

        return false;
    }

    public async Task<bool> StopAllSoundsAsync(CancellationToken ct = default)
    {
        if (IsConnected)
        {
            bool ok = await SendJsonMessageAsync("stopAllMemeSounds", null, ct).ConfigureAwait(false);
            if (ok) return true;
        }

        return true;
    }

    public async Task<IReadOnlyList<VoicemodSoundItem>> GetSoundsAsync(CancellationToken ct = default)
    {
        if (!IsConnected) return AvailableSounds;

        await SendJsonMessageAsync("getMemes", null, ct).ConfigureAwait(false);
        return AvailableSounds;
    }

    public void StartMonitoring()
    {
        lock (_stateLock)
        {
            _reconnectTimer?.Dispose();
            int interval = Math.Max(2, _settings.ReconnectIntervalSeconds);
            _reconnectTimer = new Timer(async _ => await ReconnectCycleAsync().ConfigureAwait(false), null, TimeSpan.Zero, TimeSpan.FromSeconds(interval));
        }
    }

    public void StopMonitoring()
    {
        lock (_stateLock)
        {
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
        }
    }

    private async Task ReconnectCycleAsync()
    {
        if (_isConnecting || IsConnected) return;

        _isConnecting = true;
        try
        {
            await ConnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoicemodService] Reconnect error: {ex.Message}");
        }
        finally
        {
            _isConnecting = false;
        }
    }

    private async Task<bool> SendJsonMessageAsync(string action, object? payload, CancellationToken ct, string? customId = null)
    {
        var ws = _webSocket;
        if (ws == null || ws.State != WebSocketState.Open) return false;

        var messageObj = new Dictionary<string, object?>
        {
            ["id"] = customId ?? Guid.NewGuid().ToString(),
            ["action"] = action,
            ["payload"] = payload ?? new { }
        };

        string json = JsonSerializer.Serialize(messageObj);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (ws.State != WebSocketState.Open) return false;
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
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

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                ms.SetLength(0);

                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        SetConnected(false);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                string json = Encoding.UTF8.GetString(ms.ToArray());
                ProcessIncomingMessage(json);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoicemodService] ReceiveLoop error: {ex.Message}");
        }
        finally
        {
            SetConnected(false);
        }
    }

    private void ProcessIncomingMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string action = "";
            if (root.TryGetProperty("actionType", out var actTypeProp) && actTypeProp.ValueKind == JsonValueKind.String)
            {
                action = actTypeProp.GetString() ?? "";
            }
            if (string.IsNullOrEmpty(action) && root.TryGetProperty("action", out var actProp) && actProp.ValueKind == JsonValueKind.String)
            {
                action = actProp.GetString() ?? "";
            }

            bool hasActionObj = root.TryGetProperty("actionObject", out var actionObj) && actionObj.ValueKind != JsonValueKind.Null && actionObj.ValueKind != JsonValueKind.Undefined;
            bool hasPayload = root.TryGetProperty("payload", out var payload) && payload.ValueKind != JsonValueKind.Null && payload.ValueKind != JsonValueKind.Undefined;

            // Jeśli otrzymano jakikolwiek poprawny komunikat z Voicemod API podczas oczekiwania na rejestrację
            if (!string.IsNullOrEmpty(action) && !action.Equals("registerClient", StringComparison.OrdinalIgnoreCase))
            {
                _registerTcs?.TrySetResult((true, "Authorized"));
            }

            switch (action)
            {
                case "registerClient":
                    int code = 200;
                    string desc = "Authorized";
                    if (hasActionObj && actionObj.TryGetProperty("status", out var stAction))
                    {
                        if (stAction.TryGetProperty("code", out var cd)) code = cd.GetInt32();
                        if (stAction.TryGetProperty("description", out var ds)) desc = ds.GetString() ?? desc;
                    }
                    else if (hasPayload && payload.TryGetProperty("status", out var stPayload))
                    {
                        if (stPayload.TryGetProperty("code", out var cd2)) code = cd2.GetInt32();
                        if (stPayload.TryGetProperty("description", out var ds2)) desc = ds2.GetString() ?? desc;
                    }

                    if (code == 200)
                    {
                        SetConnected(true);
                        _registerTcs?.TrySetResult((true, desc));
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(100).ConfigureAwait(false);
                            await RefreshStateAsync().ConfigureAwait(false);
                            await GetVoicesAsync().ConfigureAwait(false);
                            await GetSoundsAsync().ConfigureAwait(false);
                        });
                    }
                    else
                    {
                        _registerTcs?.TrySetResult((false, $"Voicemod odrzucił autoryzację ({code}: {desc})."));
                        SetConnected(false);
                    }
                    break;

                case "toggleVoiceChanger":
                case "getVoiceChangerStatus":
                case "voiceChangerStatus":
                    if (TryExtractBoolean(root, actionObj, payload, out bool vcVal))
                    {
                        IsVoiceChangerEnabled = vcVal;
                        NotifyStateChanged();
                    }
                    break;

                case "voiceChangerDisabledEvent":
                    IsVoiceChangerEnabled = false;
                    NotifyStateChanged();
                    break;

                case "voiceChangerEnabledEvent":
                    IsVoiceChangerEnabled = true;
                    NotifyStateChanged();
                    break;

                case "toggleHearMyVoice":
                case "getHearMyselfStatus":
                case "getHearMyVoiceStatus":
                case "hearMyVoiceStatus":
                case "hearMyselfStatus":
                    if (TryExtractBoolean(root, actionObj, payload, out bool hearVal))
                    {
                        IsHearMyVoiceEnabled = hearVal;
                        NotifyStateChanged();
                    }
                    break;

                case "hearMyVoiceDisabledEvent":
                case "hearMyselfDisabledEvent":
                    IsHearMyVoiceEnabled = false;
                    NotifyStateChanged();
                    break;

                case "hearMyVoiceEnabledEvent":
                case "hearMyselfEnabledEvent":
                    IsHearMyVoiceEnabled = true;
                    NotifyStateChanged();
                    break;

                case "toggleMuteMic":
                case "getMuteMicStatus":
                case "muteMicStatus":
                case "toggleMute":
                    if (TryExtractBoolean(root, actionObj, payload, out bool muteVal))
                    {
                        IsMicMuted = muteVal;
                        NotifyStateChanged();
                    }
                    break;

                case "muteMicDisabledEvent":
                case "muteMicrophoneDisabledEvent":
                    IsMicMuted = false;
                    NotifyStateChanged();
                    break;

                case "muteMicEnabledEvent":
                case "muteMicrophoneEnabledEvent":
                    IsMicMuted = true;
                    NotifyStateChanged();
                    break;

                case "toggleBackground":
                case "getBackgroundEffectStatus":
                case "backgroundEffectStatus":
                case "getBackgroundStatus":
                case "backgroundStatus":
                case "getBackgroundEffectsStatus":
                case "toggleBackgroundEffect":
                case "toggleBackgroundEffects":
                    if (TryExtractBoolean(root, actionObj, payload, out bool bgVal))
                    {
                        IsBackgroundEffectsEnabled = bgVal;
                        NotifyStateChanged();
                    }
                    break;

                case "backgroundEffectDisabledEvent":
                case "backgroundEffectsDisabledEvent":
                case "backgroundDisabledEvent":
                    IsBackgroundEffectsEnabled = false;
                    NotifyStateChanged();
                    break;

                case "backgroundEffectEnabledEvent":
                case "backgroundEffectsEnabledEvent":
                case "backgroundEnabledEvent":
                    IsBackgroundEffectsEnabled = true;
                    NotifyStateChanged();
                    break;

                case "getCurrentVoice":
                case "voiceLoadedEvent":
                case "voiceChangedEvent":
                    string? newVoiceId = null;
                    if (hasActionObj && (actionObj.TryGetProperty("voiceID", out var vId1) || actionObj.TryGetProperty("voiceId", out vId1) || actionObj.TryGetProperty("currentVoice", out vId1)))
                    {
                        newVoiceId = vId1.GetString();
                    }
                    else if (hasPayload && (payload.TryGetProperty("voiceID", out var vId2) || payload.TryGetProperty("voiceId", out vId2) || payload.TryGetProperty("currentVoice", out vId2)))
                    {
                        newVoiceId = vId2.GetString();
                    }
                    else if (root.TryGetProperty("voiceID", out var vId3) || root.TryGetProperty("currentVoice", out vId3))
                    {
                        newVoiceId = vId3.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(newVoiceId))
                    {
                        CurrentVoiceId = newVoiceId;
                        UpdateCurrentVoiceName();
                        NotifyStateChanged();
                    }
                    break;

                case "getVoices":
                case "voicesLoadedEvent":
                    JsonElement voicesArray = default;
                    if (hasActionObj && actionObj.TryGetProperty("voices", out var vArr1) && vArr1.ValueKind == JsonValueKind.Array)
                    {
                        voicesArray = vArr1;
                    }
                    else if (hasPayload && payload.TryGetProperty("voices", out var vArr2) && vArr2.ValueKind == JsonValueKind.Array)
                    {
                        voicesArray = vArr2;
                    }
                    else if (hasActionObj && actionObj.ValueKind == JsonValueKind.Array)
                    {
                        voicesArray = actionObj;
                    }
                    else if (hasPayload && payload.ValueKind == JsonValueKind.Array)
                    {
                        voicesArray = payload;
                    }

                    string? curVoice = null;
                    if (root.TryGetProperty("currentVoice", out var rCv) && rCv.ValueKind == JsonValueKind.String)
                    {
                        curVoice = rCv.GetString();
                    }
                    else if (hasActionObj && actionObj.TryGetProperty("currentVoice", out var aCv) && aCv.ValueKind == JsonValueKind.String)
                    {
                        curVoice = aCv.GetString();
                    }
                    else if (hasPayload && payload.TryGetProperty("currentVoice", out var pCv) && pCv.ValueKind == JsonValueKind.String)
                    {
                        curVoice = pCv.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(curVoice))
                    {
                        CurrentVoiceId = curVoice;
                    }

                    if (voicesArray.ValueKind == JsonValueKind.Array)
                    {
                        var list = new List<VoicemodVoiceItem>();
                        foreach (var v in voicesArray.EnumerateArray())
                        {
                            string id = v.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                            string name = "";
                            if (v.TryGetProperty("friendlyName", out var fnProp))
                                name = fnProp.GetString() ?? "";
                            else if (v.TryGetProperty("name", out var nProp))
                                name = nProp.GetString() ?? "";
                            else
                                name = id;

                            bool fav = v.TryGetProperty("favorited", out var fProp) && fProp.GetBoolean();
                            if (!string.IsNullOrWhiteSpace(id))
                            {
                                list.Add(new VoicemodVoiceItem(id, name, fav));
                            }
                        }

                        if (list.Count > 0)
                        {
                            lock (_stateLock)
                            {
                                _voices.Clear();
                                _voices.AddRange(list);
                            }

                            UpdateCurrentVoiceName();
                            VoicesLoaded?.Invoke(AvailableVoices);
                            NotifyStateChanged();
                        }
                    }
                    break;

                case "playMeme":
                case "playMemeEvent":
                case "memePlayedEvent":
                    string playedSound = "";
                    if (hasActionObj && (actionObj.TryGetProperty("FileName", out var ps1) || actionObj.TryGetProperty("fileName", out ps1)))
                    {
                        playedSound = ps1.GetString() ?? "";
                    }
                    else if (hasPayload && (payload.TryGetProperty("FileName", out var ps2) || payload.TryGetProperty("fileName", out ps2)))
                    {
                        playedSound = ps2.GetString() ?? "";
                    }
                    else if (hasActionObj && (actionObj.TryGetProperty("Name", out var ps3) || actionObj.TryGetProperty("name", out ps3)))
                    {
                        playedSound = ps3.GetString() ?? "";
                    }
                    else if (hasPayload && (payload.TryGetProperty("Name", out var ps4) || payload.TryGetProperty("name", out ps4)))
                    {
                        playedSound = ps4.GetString() ?? "";
                    }

                    if (!string.IsNullOrEmpty(playedSound))
                    {
                        SoundPlayed?.Invoke(playedSound);
                    }
                    break;

                case "stopAllMemeSounds":
                case "stopAllMemeSoundsEvent":
                case "stopAllSoundsEvent":
                    // Soundboard playback stopped
                    break;

                case "getMemes":
                case "memesLoadedEvent":
                case "getMemeList":
                case "getBitmapMeme":
                    JsonElement arrayElem = default;
                    if (hasActionObj && actionObj.TryGetProperty("listOfMemes", out var lom1) && lom1.ValueKind == JsonValueKind.Array)
                    {
                        arrayElem = lom1;
                    }
                    else if (hasPayload && payload.TryGetProperty("listOfMemes", out var lom2) && lom2.ValueKind == JsonValueKind.Array)
                    {
                        arrayElem = lom2;
                    }
                    else if (hasActionObj && actionObj.TryGetProperty("memes", out var m1) && m1.ValueKind == JsonValueKind.Array)
                    {
                        arrayElem = m1;
                    }
                    else if (hasPayload && payload.TryGetProperty("memes", out var m2) && m2.ValueKind == JsonValueKind.Array)
                    {
                        arrayElem = m2;
                    }
                    else if (hasPayload && payload.ValueKind == JsonValueKind.Array)
                    {
                        arrayElem = payload;
                    }
                    else if (hasActionObj && actionObj.ValueKind == JsonValueKind.Array)
                    {
                        arrayElem = actionObj;
                    }

                    if (arrayElem.ValueKind == JsonValueKind.Array)
                    {
                        var soundList = new List<VoicemodSoundItem>();
                        foreach (var item in arrayElem.EnumerateArray())
                        {
                            string fn = item.TryGetProperty("FileName", out var fnP1) ? fnP1.GetString() ?? "" :
                                        (item.TryGetProperty("fileName", out var fnP2) ? fnP2.GetString() ?? "" :
                                        (item.TryGetProperty("filename", out var fnP3) ? fnP3.GetString() ?? "" :
                                        (item.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "")));

                            string name = item.TryGetProperty("Name", out var nP1) ? nP1.GetString() ?? fn :
                                          (item.TryGetProperty("name", out var nP2) ? nP2.GetString() ?? fn : fn);

                            string? type = item.TryGetProperty("Type", out var tP1) ? tP1.GetString() :
                                           (item.TryGetProperty("type", out var tP2) ? tP2.GetString() : null);

                            bool isCore = (item.TryGetProperty("IsCore", out var icP1) && icP1.GetBoolean()) ||
                                          (item.TryGetProperty("isCore", out var icP2) && icP2.GetBoolean());

                            if (!string.IsNullOrWhiteSpace(fn) || !string.IsNullOrWhiteSpace(name))
                            {
                                soundList.Add(new VoicemodSoundItem(string.IsNullOrEmpty(fn) ? name : fn, name, type, isCore));
                            }
                        }

                        if (soundList.Count > 0)
                        {
                            lock (_stateLock)
                            {
                                _sounds.Clear();
                                _sounds.AddRange(soundList);
                            }

                            SoundsLoaded?.Invoke(AvailableSounds);
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoicemodService] Error parsing message: {ex.Message}");
        }
    }

    private static bool TryExtractBoolean(JsonElement root, JsonElement actionObj, JsonElement payload, out bool value)
    {
        if (TryExtractBoolFromElement(actionObj, out value)) return true;
        if (TryExtractBoolFromElement(payload, out value)) return true;
        if (TryExtractBoolFromElement(root, out value)) return true;
        value = false;
        return false;
    }

    private static bool TryExtractBoolFromElement(JsonElement element, out bool value)
    {
        value = false;
        if (element.ValueKind == JsonValueKind.True) { value = true; return true; }
        if (element.ValueKind == JsonValueKind.False) { value = false; return true; }
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("value", out var v))
            {
                if (v.ValueKind == JsonValueKind.True) { value = true; return true; }
                if (v.ValueKind == JsonValueKind.False) { value = false; return true; }
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n)) { value = n != 0; return true; }
                if (v.ValueKind == JsonValueKind.String)
                {
                    string s = v.GetString() ?? "";
                    if (bool.TryParse(s, out bool b)) { value = b; return true; }
                    if (s == "1") { value = true; return true; }
                    if (s == "0") { value = false; return true; }
                }
            }
            if (element.TryGetProperty("status", out var st))
            {
                if (st.ValueKind == JsonValueKind.True) { value = true; return true; }
                if (st.ValueKind == JsonValueKind.False) { value = false; return true; }
            }
        }
        return false;
    }

    private static bool ExtractBoolean(JsonElement element)
    {
        return TryExtractBoolFromElement(element, out bool v) && v;
    }

    private void UpdateCurrentVoiceName()
    {
        if (string.IsNullOrWhiteSpace(CurrentVoiceId))
        {
            CurrentVoiceName = null;
            return;
        }

        lock (_stateLock)
        {
            var match = _voices.FirstOrDefault(v => string.Equals(v.Id, CurrentVoiceId, StringComparison.OrdinalIgnoreCase));
            CurrentVoiceName = match?.Name ?? CurrentVoiceId;
        }
    }

    private void SetConnected(bool connected)
    {
        if (IsConnected != connected)
        {
            IsConnected = connected;
            ConnectionStateChanged?.Invoke(connected);
            NotifyStateChanged();
        }
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(CurrentState);
    }

    public void Dispose()
    {
        StopMonitoring();
        _ = DisconnectInternalAsync();
        _sendLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
