namespace MasterHub.Core.Integrations.Vlc;

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Profiles;
using MasterHub.Core.SystemControl;

public class VlcService : IVlcService
{
    private readonly IProfileService _profileService;
    private readonly ISystemControlService _systemControl;
    private readonly HttpClient _httpClient;
    private readonly object _lock = new();

    private VlcSettingsModel _settings = new();
    private Timer? _pollTimer;
    private bool _isPolling;

    public bool IsConnected { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool IsFullscreen { get; private set; }
    public int VolumePercent { get; private set; } = 100;
    public string? CurrentTitle { get; private set; }
    public string? CurrentArtist { get; private set; }
    public string? VlcVersion { get; private set; }
    public VlcPlaybackStateEnum PlaybackState { get; private set; } = VlcPlaybackStateEnum.Stopped;

    public VlcPlaybackState CurrentState => new(
        IsConnected,
        PlaybackState,
        IsPlaying,
        IsFullscreen,
        VolumePercent,
        CurrentTitle,
        CurrentArtist,
        VlcVersion);

    public event Action<bool>? ConnectionStateChanged;
    public event Action<VlcPlaybackState>? PlaybackStateChanged;

    public VlcService(IProfileService profileService, ISystemControlService systemControl)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _systemControl = systemControl ?? throw new ArgumentNullException(nameof(systemControl));

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        var loaded = _profileService.GetVlcSettings();
        if (loaded != null)
        {
            _settings = loaded;
        }

        if (_settings.AutoConnect)
        {
            StartMonitoring();
        }
    }

    public void Configure(VlcSettingsModel settings)
    {
        lock (_lock)
        {
            _settings = settings ?? new VlcSettingsModel();
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

    public async Task<(bool Success, string Message)> TestConnectionDetailedAsync(CancellationToken ct = default)
    {
        string host;
        int port;
        string password;

        lock (_lock)
        {
            host = string.IsNullOrWhiteSpace(_settings.Host) ? "127.0.0.1" : _settings.Host.Trim();
            port = _settings.Port <= 0 ? 8080 : _settings.Port;
            password = _settings.Password ?? string.Empty;
        }

        string requestUri = $"http://{host}:{port}/requests/status.json";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
            var authBytes = Encoding.ASCII.GetBytes($":{password}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));

            using var resp = await _httpClient.SendAsync(req, cts.Token).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                SetConnected(false);
                return (false, "Błąd 401 Unauthorized: Niepoprawne hasło VLC Web Interface.");
            }

            if (!resp.IsSuccessStatusCode)
            {
                SetConnected(false);
                return (false, $"Błąd HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}");
            }

            string json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            ParseVlcStatus(json);
            SetConnected(true);

            return (true, $"Połączono pomyślnie z VLC {VlcVersion ?? "Media Player"} (Stan: {PlaybackState}).");
        }
        catch (OperationCanceledException)
        {
            SetConnected(false);
            return (false, $"Przekroczono limit czasu połączenia z VLC ({host}:{port}).");
        }
        catch (HttpRequestException ex)
        {
            SetConnected(false);
            return (false, $"Nie można połączyć się z VLC na {host}:{port}. Upewnij się, że VLC działa i ma włączony interfejs HTTP: {ex.Message}");
        }
        catch (Exception ex)
        {
            SetConnected(false);
            return (false, $"Błąd połączenia z VLC: {ex.Message}");
        }
    }

    public Task DisconnectAsync()
    {
        StopMonitoring();
        SetConnected(false);
        CurrentTitle = null;
        CurrentArtist = null;
        IsPlaying = false;
        PlaybackState = VlcPlaybackStateEnum.Stopped;
        NotifyPlaybackChanged();
        return Task.CompletedTask;
    }

    public async Task<bool> PlayPauseAsync(CancellationToken ct = default)
    {
        bool success = await SendVlcCommandAsync("pl_pause", ct).ConfigureAwait(false);
        if (!success && _settings.FallbackToDesktopHotkeys)
        {
            _systemControl.PlayPause();
            IsPlaying = !IsPlaying;
            PlaybackState = IsPlaying ? VlcPlaybackStateEnum.Playing : VlcPlaybackStateEnum.Paused;
            NotifyPlaybackChanged();
            return true;
        }

        if (success)
        {
            _ = Task.Run(() => RefreshPlaybackStateAsync(CancellationToken.None));
        }
        return success;
    }

    public async Task<bool> StopAsync(CancellationToken ct = default)
    {
        bool success = await SendVlcCommandAsync("pl_stop", ct).ConfigureAwait(false);
        if (!success && _settings.FallbackToDesktopHotkeys)
        {
            _systemControl.StopTrack();
            IsPlaying = false;
            PlaybackState = VlcPlaybackStateEnum.Stopped;
            NotifyPlaybackChanged();
            return true;
        }

        if (success)
        {
            _ = Task.Run(() => RefreshPlaybackStateAsync(CancellationToken.None));
        }
        return success;
    }

    public async Task<bool> NextTrackAsync(CancellationToken ct = default)
    {
        bool success = await SendVlcCommandAsync("pl_next", ct).ConfigureAwait(false);
        if (!success && _settings.FallbackToDesktopHotkeys)
        {
            _systemControl.NextTrack();
            return true;
        }

        if (success)
        {
            _ = Task.Run(() => RefreshPlaybackStateAsync(CancellationToken.None));
        }
        return success;
    }

    public async Task<bool> PreviousTrackAsync(CancellationToken ct = default)
    {
        bool success = await SendVlcCommandAsync("pl_previous", ct).ConfigureAwait(false);
        if (!success && _settings.FallbackToDesktopHotkeys)
        {
            _systemControl.PreviousTrack();
            return true;
        }

        if (success)
        {
            _ = Task.Run(() => RefreshPlaybackStateAsync(CancellationToken.None));
        }
        return success;
    }

    public async Task<bool> ToggleFullscreenAsync(CancellationToken ct = default)
    {
        bool success = await SendVlcCommandAsync("fullscreen", ct).ConfigureAwait(false);
        if (!success && _settings.FallbackToDesktopHotkeys)
        {
            _systemControl.SendHotkey("F");
            IsFullscreen = !IsFullscreen;
            NotifyPlaybackChanged();
            return true;
        }

        if (success)
        {
            _ = Task.Run(() => RefreshPlaybackStateAsync(CancellationToken.None));
        }
        return success;
    }

    public async Task<VlcPlaybackState?> RefreshPlaybackStateAsync(CancellationToken ct = default)
    {
        string host;
        int port;
        string password;

        lock (_lock)
        {
            host = string.IsNullOrWhiteSpace(_settings.Host) ? "127.0.0.1" : _settings.Host.Trim();
            port = _settings.Port <= 0 ? 8080 : _settings.Port;
            password = _settings.Password ?? string.Empty;
        }

        string requestUri = $"http://{host}:{port}/requests/status.json";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
            var authBytes = Encoding.ASCII.GetBytes($":{password}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                ParseVlcStatus(json);
                SetConnected(true);
                return CurrentState;
            }

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                SetConnected(false);
            }
        }
        catch
        {
            if (IsConnected)
            {
                SetConnected(false);
            }
        }

        return CurrentState;
    }

    public void StartMonitoring()
    {
        lock (_lock)
        {
            _pollTimer?.Dispose();
            int interval = Math.Max(1, _settings.StatusPollIntervalSeconds);
            _pollTimer = new Timer(async _ => await PollCycleAsync().ConfigureAwait(false), null, TimeSpan.Zero, TimeSpan.FromSeconds(interval));
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

    private async Task PollCycleAsync()
    {
        if (_isPolling) return;
        _isPolling = true;

        try
        {
            await RefreshPlaybackStateAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VlcService] Poll error: {ex.Message}");
        }
        finally
        {
            _isPolling = false;
        }
    }

    private async Task<bool> SendVlcCommandAsync(string command, CancellationToken ct)
    {
        string host;
        int port;
        string password;

        lock (_lock)
        {
            host = string.IsNullOrWhiteSpace(_settings.Host) ? "127.0.0.1" : _settings.Host.Trim();
            port = _settings.Port <= 0 ? 8080 : _settings.Port;
            password = _settings.Password ?? string.Empty;
        }

        string requestUri = $"http://{host}:{port}/requests/status.json?command={command}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
            var authBytes = Encoding.ASCII.GetBytes($":{password}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                ParseVlcStatus(json);
                SetConnected(true);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private void ParseVlcStatus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("version", out var verProp))
            {
                VlcVersion = verProp.GetString();
            }

            if (root.TryGetProperty("state", out var stateProp))
            {
                string stateStr = stateProp.GetString() ?? "";
                if (stateStr.Equals("playing", StringComparison.OrdinalIgnoreCase))
                {
                    PlaybackState = VlcPlaybackStateEnum.Playing;
                    IsPlaying = true;
                }
                else if (stateStr.Equals("paused", StringComparison.OrdinalIgnoreCase))
                {
                    PlaybackState = VlcPlaybackStateEnum.Paused;
                    IsPlaying = false;
                }
                else
                {
                    PlaybackState = VlcPlaybackStateEnum.Stopped;
                    IsPlaying = false;
                }
            }

            if (root.TryGetProperty("fullscreen", out var fsProp))
            {
                if (fsProp.ValueKind == JsonValueKind.True)
                {
                    IsFullscreen = true;
                }
                else if (fsProp.ValueKind == JsonValueKind.False)
                {
                    IsFullscreen = false;
                }
                else if (fsProp.ValueKind == JsonValueKind.Number && fsProp.TryGetInt32(out int fsNum))
                {
                    IsFullscreen = fsNum != 0;
                }
                else if (fsProp.ValueKind == JsonValueKind.String)
                {
                    string fsVal = fsProp.GetString() ?? "";
                    IsFullscreen = fsVal == "1" || fsVal.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }

            if (root.TryGetProperty("volume", out var volProp) && volProp.TryGetInt32(out int vol))
            {
                VolumePercent = Math.Clamp((int)Math.Round(vol / 256.0 * 100), 0, 200);
            }

            string? parsedTitle = null;
            string? parsedArtist = null;

            if (root.TryGetProperty("information", out var infoProp) &&
                infoProp.TryGetProperty("category", out var catProp) &&
                catProp.TryGetProperty("meta", out var metaProp))
            {
                if (metaProp.TryGetProperty("title", out var titleProp))
                {
                    parsedTitle = titleProp.GetString();
                }
                if (string.IsNullOrWhiteSpace(parsedTitle) && metaProp.TryGetProperty("filename", out var fileProp))
                {
                    parsedTitle = fileProp.GetString();
                }
                if (metaProp.TryGetProperty("artist", out var artistProp))
                {
                    parsedArtist = artistProp.GetString();
                }
            }

            CurrentTitle = parsedTitle;
            CurrentArtist = parsedArtist;

            NotifyPlaybackChanged();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VlcService] Error parsing VLC status: {ex.Message}");
        }
    }

    private void SetConnected(bool connected)
    {
        if (IsConnected != connected)
        {
            IsConnected = connected;
            ConnectionStateChanged?.Invoke(connected);
            NotifyPlaybackChanged();
        }
    }

    private void NotifyPlaybackChanged()
    {
        PlaybackStateChanged?.Invoke(CurrentState);
    }

    public void Dispose()
    {
        StopMonitoring();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
