namespace MasterHub.Core.Integrations.Spotify;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Profiles;
using MasterHub.Core.SystemControl;

public class SpotifyService : ISpotifyService
{
    private const string AccountsBaseUrl = "https://accounts.spotify.com";
    private const string ApiBaseUrl = "https://api.spotify.com/v1";
    private const string DefaultRedirectUri = "http://127.0.0.1:8888/callback/";

    private readonly IProfileService _profileService;
    private readonly ISystemControlService _systemControl;
    private readonly HttpClient _httpClient;
    private readonly object _lock = new();

    private SpotifySettingsModel _settings = new();
    private Timer? _pollTimer;
    private bool _isPolling;

    public bool IsConnected => !string.IsNullOrEmpty(_settings.AccessToken) &&
                               (_settings.TokenExpiresAtUtc == null || _settings.TokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(1) || !string.IsNullOrEmpty(_settings.RefreshToken));

    public bool IsPlaying { get; private set; }
    public bool IsShuffle { get; private set; }
    public SpotifyRepeatMode RepeatMode { get; private set; } = SpotifyRepeatMode.Off;
    public bool IsCurrentTrackLiked { get; private set; }
    public string? CurrentTrackId { get; private set; }
    public string? CurrentTrackTitle { get; private set; }
    public string? CurrentTrackArtist { get; private set; }
    public string? CurrentAlbumTitle { get; private set; }
    public string? CurrentAlbumArtUrl { get; private set; }
    public int VolumePercent { get; private set; } = 100;

    public SpotifyPlaybackState CurrentState => new(
        IsPlaying,
        IsShuffle,
        RepeatMode,
        IsCurrentTrackLiked,
        CurrentTrackId,
        CurrentTrackTitle,
        CurrentTrackArtist,
        CurrentAlbumTitle,
        CurrentAlbumArtUrl,
        VolumePercent,
        IsConnected);

    public event Action<bool>? ConnectionStateChanged;
    public event Action<SpotifyPlaybackState>? PlaybackStateChanged;

    public SpotifyService(IProfileService profileService, ISystemControlService systemControl)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
        _systemControl = systemControl ?? throw new ArgumentNullException(nameof(systemControl));

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
    }

    public void Configure(SpotifySettingsModel settings)
    {
        lock (_lock)
        {
            _settings = settings ?? new SpotifySettingsModel();

            if (_settings.AutoConnect)
            {
                StartMonitoring();
            }
            else
            {
                StopMonitoring();
            }
        }
    }

    public async Task<(bool Success, string Message)> StartPkceLoginAsync(Action<string> openBrowserCallback, CancellationToken ct = default)
    {
        string clientId = _settings.ClientId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return (false, "Wprowadź Client ID ze Spotify Developer Dashboard w ustawieniach.");
        }

        HttpListener? listener = null;
        try
        {
            string codeVerifier = GenerateCodeVerifier();
            string codeChallenge = GenerateCodeChallenge(codeVerifier);

            listener = new HttpListener();
            listener.Prefixes.Add(DefaultRedirectUri);
            listener.Start();

            string scope = Uri.EscapeDataString("user-modify-playback-state user-read-playback-state user-read-currently-playing user-library-read user-library-modify playlist-read-private playlist-read-collaborative");
            string authUrl = $"{AccountsBaseUrl}/authorize?client_id={Uri.EscapeDataString(clientId)}&response_type=code&redirect_uri={Uri.EscapeDataString(DefaultRedirectUri)}&code_challenge_method=S256&code_challenge={Uri.EscapeDataString(codeChallenge)}&scope={scope}";

            openBrowserCallback?.Invoke(authUrl);

            var contextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromMinutes(2), ct));

            if (completedTask != contextTask)
            {
                return (false, "Przekroczono limit czasu oczekiwania na logowanie w przeglądarce (2 minuty).");
            }

            var context = await contextTask;
            string? code = context.Request.QueryString["code"];
            string? error = context.Request.QueryString["error"];

            // Wyślij stronę z podziękowaniem do przeglądarki użytkownika
            byte[] responseBytes;
            if (!string.IsNullOrEmpty(code))
            {
                string html = "<html><body style=\"font-family:Segoe UI,sans-serif;text-align:center;padding:60px;background:#121212;color:#FFFFFF;\"><h1 style=\"color:#1DB954;font-size:32px;\">✔ Połączono z Spotify!</h1><p style=\"font-size:16px;color:#B3B3B3;\">Możesz teraz bezpiecznie zamknąć tę kartę przeglądarki i wrócić do MasterHub Studio.</p></body></html>";
                responseBytes = Encoding.UTF8.GetBytes(html);
                context.Response.StatusCode = 200;
            }
            else
            {
                string html = $"<html><body style=\"font-family:Segoe UI,sans-serif;text-align:center;padding:60px;background:#121212;color:#FFFFFF;\"><h1 style=\"color:#EF4444;font-size:32px;\">✖ Anulowano logowanie</h1><p style=\"font-size:16px;color:#B3B3B3;\">{error ?? "Brak kodu autoryzacji."}</p></body></html>";
                responseBytes = Encoding.UTF8.GetBytes(html);
                context.Response.StatusCode = 400;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes, ct);
            context.Response.OutputStream.Close();

            if (string.IsNullOrEmpty(code))
            {
                return (false, $"Odmowa dostępu ze strony Spotify: {error ?? "Nieznany błąd"}");
            }

            // Wymiana Authorization Code na Access Token i Refresh Token
            var tokenReq = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = DefaultRedirectUri,
                ["client_id"] = clientId,
                ["code_verifier"] = codeVerifier
            };

            if (!string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                tokenReq["client_secret"] = _settings.ClientSecret.Trim();
            }

            using var postContent = new FormUrlEncodedContent(tokenReq);
            using var tokenResp = await _httpClient.PostAsync($"{AccountsBaseUrl}/api/token", postContent, ct);

            string tokenJson = await tokenResp.Content.ReadAsStringAsync(ct);
            if (!tokenResp.IsSuccessStatusCode)
            {
                return (false, $"Błąd pobierania tokena Spotify: {(int)tokenResp.StatusCode} {tokenJson}");
            }

            using var doc = JsonDocument.Parse(tokenJson);
            string accessToken = doc.RootElement.GetProperty("access_token").GetString() ?? "";
            string refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var rf) ? rf.GetString() ?? "" : "";
            int expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

            _settings.AccessToken = accessToken;
            if (!string.IsNullOrEmpty(refreshToken))
            {
                _settings.RefreshToken = refreshToken;
            }
            _settings.TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn - 60);

            _profileService.SaveSpotifySettings(_settings);
            ConnectionStateChanged?.Invoke(true);

            await RefreshPlaybackStateAsync(ct);
            StartMonitoring();

            return (true, "Połączono pomyślnie z kontem Spotify!");
        }
        catch (Exception ex)
        {
            return (false, $"Błąd logowania Spotify: {ex.Message}");
        }
        finally
        {
            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch { }
        }
    }

    public Task DisconnectAsync()
    {
        lock (_lock)
        {
            StopMonitoring();
            _settings.AccessToken = "";
            _settings.RefreshToken = "";
            _settings.TokenExpiresAtUtc = null;
            _profileService.SaveSpotifySettings(_settings);

            IsPlaying = false;
            CurrentTrackId = null;
            CurrentTrackTitle = null;
            CurrentTrackArtist = null;
            CurrentAlbumTitle = null;
            CurrentAlbumArtUrl = null;

            ConnectionStateChanged?.Invoke(false);
            PlaybackStateChanged?.Invoke(CurrentState);
        }
        return Task.CompletedTask;
    }

    private async Task<bool> EnsureAccessTokenAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(_settings.AccessToken) &&
            _settings.TokenExpiresAtUtc.HasValue &&
            _settings.TokenExpiresAtUtc.Value > DateTime.UtcNow.AddMinutes(1))
        {
            return true;
        }

        string refreshToken = _settings.RefreshToken?.Trim() ?? "";
        string clientId = _settings.ClientId?.Trim() ?? "";
        if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(clientId))
        {
            return false;
        }

        try
        {
            var reqData = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId
            };

            if (!string.IsNullOrWhiteSpace(_settings.ClientSecret))
            {
                reqData["client_secret"] = _settings.ClientSecret.Trim();
            }

            using var content = new FormUrlEncodedContent(reqData);
            using var resp = await _httpClient.PostAsync($"{AccountsBaseUrl}/api/token", content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return false;
            }

            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            _settings.AccessToken = doc.RootElement.GetProperty("access_token").GetString() ?? "";

            if (doc.RootElement.TryGetProperty("refresh_token", out var newRf))
            {
                string r = newRf.GetString() ?? "";
                if (!string.IsNullOrEmpty(r)) _settings.RefreshToken = r;
            }

            int expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            _settings.TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn - 60);

            _profileService.SaveSpotifySettings(_settings);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SpotifyService] Błąd odświeżania tokena: {ex.Message}");
            return false;
        }
    }

    public async Task<SpotifyPlaybackState?> RefreshPlaybackStateAsync(CancellationToken ct = default)
    {
        bool hasAuth = await EnsureAccessTokenAsync(ct);
        if (!hasAuth)
        {
            return CurrentState;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/player");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

            using var resp = await _httpClient.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.NoContent)
            {
                IsPlaying = false;
                PlaybackStateChanged?.Invoke(CurrentState);
                return CurrentState;
            }

            if (!resp.IsSuccessStatusCode)
            {
                return CurrentState;
            }

            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("is_playing", out var ip))
            {
                IsPlaying = ip.GetBoolean();
            }

            if (root.TryGetProperty("shuffle_state", out var sh))
            {
                IsShuffle = sh.GetBoolean();
            }

            if (root.TryGetProperty("repeat_state", out var rep))
            {
                string rStr = rep.GetString() ?? "off";
                RepeatMode = rStr.ToLowerInvariant() switch
                {
                    "track" => SpotifyRepeatMode.Track,
                    "context" => SpotifyRepeatMode.Context,
                    _ => SpotifyRepeatMode.Off
                };
            }

            if (root.TryGetProperty("device", out var dev) && dev.TryGetProperty("volume_percent", out var vol))
            {
                VolumePercent = vol.GetInt32();
            }

            string? newTrackId = null;
            if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
            {
                if (item.TryGetProperty("id", out var idProp)) newTrackId = idProp.GetString();
                if (item.TryGetProperty("name", out var nameProp)) CurrentTrackTitle = nameProp.GetString();

                if (item.TryGetProperty("artists", out var artists) && artists.GetArrayLength() > 0)
                {
                    CurrentTrackArtist = artists[0].GetProperty("name").GetString();
                }

                if (item.TryGetProperty("album", out var album))
                {
                    if (album.TryGetProperty("name", out var albName)) CurrentAlbumTitle = albName.GetString();
                    if (album.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
                    {
                        CurrentAlbumArtUrl = images[0].GetProperty("url").GetString();
                    }
                }
            }

            // Jeśli identyfikator utworu się zmienił, sprawdź czy jest w Ulubionych (Liked Songs)
            if (!string.IsNullOrEmpty(newTrackId) && newTrackId != CurrentTrackId)
            {
                CurrentTrackId = newTrackId;
                IsCurrentTrackLiked = await CheckIfTrackIsLikedAsync(newTrackId, ct);
            }
            else if (string.IsNullOrEmpty(newTrackId))
            {
                CurrentTrackId = null;
                IsCurrentTrackLiked = false;
            }

            PlaybackStateChanged?.Invoke(CurrentState);
            return CurrentState;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SpotifyService] Błąd RefreshPlaybackState: {ex.Message}");
            return CurrentState;
        }
    }

    private async Task<bool> CheckIfTrackIsLikedAsync(string trackId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(trackId) || string.IsNullOrEmpty(_settings.AccessToken)) return false;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/tracks/contains?ids={Uri.EscapeDataString(trackId)}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

            using var resp = await _httpClient.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.GetArrayLength() > 0)
                {
                    return doc.RootElement[0].GetBoolean();
                }
            }
        }
        catch { }

        return false;
    }

    public async Task<bool> ToggleShuffleAsync(CancellationToken ct = default)
    {
        bool targetState = !IsShuffle;
        bool hasAuth = await EnsureAccessTokenAsync(ct);

        if (hasAuth)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Put, $"{ApiBaseUrl}/me/player/shuffle?state={targetState.ToString().ToLowerInvariant()}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

                using var resp = await _httpClient.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    IsShuffle = targetState;
                    PlaybackStateChanged?.Invoke(CurrentState);
                    TriggerFastStateRefresh();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpotifyService] Błąd API ToggleShuffle: {ex.Message}");
            }
        }

        // Fallback do skrótu pulpitu Windows (Spotify Desktop: Ctrl+S)
        if (_settings.FallbackToDesktopHotkeys)
        {
            _systemControl.SendHotkey("Ctrl+S");
            IsShuffle = targetState;
            PlaybackStateChanged?.Invoke(CurrentState);
            return true;
        }

        return false;
    }

    public async Task<bool> SetShuffleAsync(bool enable, CancellationToken ct = default)
    {
        if (IsShuffle == enable) return true;
        return await ToggleShuffleAsync(ct);
    }

    public async Task<SpotifyRepeatMode> ToggleRepeatAsync(CancellationToken ct = default)
    {
        var nextMode = RepeatMode switch
        {
            SpotifyRepeatMode.Off => SpotifyRepeatMode.Context,
            SpotifyRepeatMode.Context => SpotifyRepeatMode.Track,
            _ => SpotifyRepeatMode.Off
        };

        string stateStr = nextMode switch
        {
            SpotifyRepeatMode.Track => "track",
            SpotifyRepeatMode.Context => "context",
            _ => "off"
        };

        bool hasAuth = await EnsureAccessTokenAsync(ct);
        if (hasAuth)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Put, $"{ApiBaseUrl}/me/player/repeat?state={stateStr}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

                using var resp = await _httpClient.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    RepeatMode = nextMode;
                    PlaybackStateChanged?.Invoke(CurrentState);
                    TriggerFastStateRefresh();
                    return nextMode;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpotifyService] Błąd API ToggleRepeat: {ex.Message}");
            }
        }

        // Fallback do skrótu pulpitu Windows (Spotify Desktop: Ctrl+R)
        if (_settings.FallbackToDesktopHotkeys)
        {
            _systemControl.SendHotkey("Ctrl+R");
            RepeatMode = nextMode;
            PlaybackStateChanged?.Invoke(CurrentState);
            return nextMode;
        }

        return RepeatMode;
    }

    public async Task<bool> SetRepeatModeAsync(SpotifyRepeatMode mode, CancellationToken ct = default)
    {
        if (RepeatMode == mode) return true;
        var res = await ToggleRepeatAsync(ct);
        return res == mode;
    }

    public async Task<bool> ToggleLikeTrackAsync(CancellationToken ct = default)
    {
        if (IsCurrentTrackLiked)
        {
            return await UnlikeCurrentTrackAsync(ct);
        }
        else
        {
            return await LikeCurrentTrackAsync(ct);
        }
    }

    public async Task<bool> LikeCurrentTrackAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentTrackId)) return false;
        bool hasAuth = await EnsureAccessTokenAsync(ct);
        if (!hasAuth) return false;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, $"{ApiBaseUrl}/me/tracks?ids={Uri.EscapeDataString(CurrentTrackId)}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                IsCurrentTrackLiked = true;
                PlaybackStateChanged?.Invoke(CurrentState);
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SpotifyService] Błąd LikeTrack: {ex.Message}");
        }

        return false;
    }

    public async Task<bool> UnlikeCurrentTrackAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentTrackId)) return false;
        bool hasAuth = await EnsureAccessTokenAsync(ct);
        if (!hasAuth) return false;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiBaseUrl}/me/tracks?ids={Uri.EscapeDataString(CurrentTrackId)}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

            using var resp = await _httpClient.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                IsCurrentTrackLiked = false;
                PlaybackStateChanged?.Invoke(CurrentState);
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SpotifyService] Błąd UnlikeTrack: {ex.Message}");
        }

        return false;
    }

    public async Task<bool> PlayPauseAsync(CancellationToken ct = default)
    {
        bool hasAuth = await EnsureAccessTokenAsync(ct);
        if (hasAuth)
        {
            try
            {
                string endpoint = IsPlaying ? "pause" : "play";
                using var req = new HttpRequestMessage(HttpMethod.Put, $"{ApiBaseUrl}/me/player/{endpoint}");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

                using var resp = await _httpClient.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    IsPlaying = !IsPlaying;
                    PlaybackStateChanged?.Invoke(CurrentState);
                    TriggerFastStateRefresh();
                    return true;
                }
            }
            catch { }
        }

        _systemControl.PlayPause();
        IsPlaying = !IsPlaying;
        PlaybackStateChanged?.Invoke(CurrentState);
        return true;
    }

    public async Task<bool> NextTrackAsync(CancellationToken ct = default)
    {
        bool hasAuth = await EnsureAccessTokenAsync(ct);
        if (hasAuth)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/player/next");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

                using var resp = await _httpClient.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    TriggerFastStateRefresh();
                    return true;
                }
            }
            catch { }
        }

        _systemControl.NextTrack();
        return true;
    }

    public async Task<bool> PreviousTrackAsync(CancellationToken ct = default)
    {
        bool hasAuth = await EnsureAccessTokenAsync(ct);
        if (hasAuth)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/player/previous");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

                using var resp = await _httpClient.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    TriggerFastStateRefresh();
                    return true;
                }
            }
            catch { }
        }

        _systemControl.PreviousTrack();
        return true;
    }

    public static string NormalizeSpotifyUri(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        string s = input.Trim();

        // Jeśli to już czysty URI Spotify (np. spotify:track:..., spotify:playlist:...)
        if (s.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            return s;
        }

        // Jeśli to link webowy z open.spotify.com
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
            (uri.Host.Equals("open.spotify.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".spotify.com", StringComparison.OrdinalIgnoreCase)))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
            {
                string type = segments[0].ToLowerInvariant();
                string id = segments[1];
                return $"spotify:{type}:{id}";
            }
        }

        return s;
    }

    public async Task<bool> AddToQueueAsync(string trackUriOrUrl, CancellationToken ct = default)
    {
        string spotifyUri = NormalizeSpotifyUri(trackUriOrUrl);
        if (string.IsNullOrWhiteSpace(spotifyUri))
        {
            return false;
        }

        // Jeśli użytkownik wpisał sam identyfikator bez prefixu, domyślnie traktujemy jako track
        if (!spotifyUri.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            spotifyUri = $"spotify:track:{spotifyUri}";
        }

        bool hasAuth = await EnsureAccessTokenAsync(ct);
        if (!hasAuth)
        {
            return false;
        }

        try
        {
            string url = $"{ApiBaseUrl}/me/player/queue?uri={Uri.EscapeDataString(spotifyUri)}";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

            using var resp = await _httpClient.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                TriggerFastStateRefresh();
                return true;
            }

            Debug.WriteLine($"[SpotifyService] AddToQueueAsync HTTP {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync(ct)}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SpotifyService] Błąd AddToQueueAsync: {ex.Message}");
        }

        return false;
    }

    public async Task<bool> PlayUriAsync(string uriOrUrl, CancellationToken ct = default)
    {
        string spotifyUri = NormalizeSpotifyUri(uriOrUrl);
        if (string.IsNullOrWhiteSpace(spotifyUri))
        {
            return await PlayPauseAsync(ct);
        }

        bool hasAuth = await EnsureAccessTokenAsync(ct);
        if (hasAuth)
        {
            try
            {
                string jsonBody;
                if (spotifyUri.StartsWith("spotify:track:", StringComparison.OrdinalIgnoreCase) ||
                    spotifyUri.StartsWith("spotify:episode:", StringComparison.OrdinalIgnoreCase))
                {
                    jsonBody = JsonSerializer.Serialize(new { uris = new[] { spotifyUri } });
                }
                else
                {
                    jsonBody = JsonSerializer.Serialize(new { context_uri = spotifyUri });
                }

                using var req = new HttpRequestMessage(HttpMethod.Put, $"{ApiBaseUrl}/me/player/play");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                using var resp = await _httpClient.SendAsync(req, ct);
                if (resp.IsSuccessStatusCode)
                {
                    IsPlaying = true;
                    PlaybackStateChanged?.Invoke(CurrentState);
                    TriggerFastStateRefresh();
                    return true;
                }

                Debug.WriteLine($"[SpotifyService] PlayUriAsync Web API zwróciło {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync(ct)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpotifyService] Błąd PlayUriAsync Web API: {ex.Message}");
            }
        }

        // Dual-Layer Desktop Fallback:
        // Otwórz URI bezpośrednio przez system Windows (protokół spotify: otwiera aplikację desktopową)
        if (_settings.FallbackToDesktopHotkeys)
        {
            try
            {
                _systemControl.OpenWebsite(spotifyUri);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpotifyService] Błąd fallback PlayUri: {ex.Message}");
            }
        }

        return false;
    }

    public async Task<IReadOnlyList<SpotifyPlaylistItem>> GetUserPlaylistsAsync(CancellationToken ct = default)
    {
        bool hasAuth = await EnsureAccessTokenAsync(ct);
        if (!hasAuth)
        {
            return Array.Empty<SpotifyPlaylistItem>();
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/playlists?limit=50");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

            using var resp = await _httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return Array.Empty<SpotifyPlaylistItem>();
            }

            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<SpotifyPlaylistItem>();
            }

            var list = new List<SpotifyPlaylistItem>();
            foreach (var elem in items.EnumerateArray())
            {
                string id = elem.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                string name = elem.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                string uri = elem.TryGetProperty("uri", out var uProp) ? uProp.GetString() ?? "" : "";
                int count = 0;
                if (elem.TryGetProperty("tracks", out var tr) && tr.TryGetProperty("total", out var tot))
                {
                    count = tot.GetInt32();
                }

                string? imgUrl = null;
                if (elem.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array && imgs.GetArrayLength() > 0)
                {
                    imgUrl = imgs[0].GetProperty("url").GetString();
                }

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                {
                    list.Add(new SpotifyPlaylistItem(id, name, uri, imgUrl, count));
                }
            }

            return list;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SpotifyService] Błąd GetUserPlaylistsAsync: {ex.Message}");
            return Array.Empty<SpotifyPlaylistItem>();
        }
    }

    private void TriggerFastStateRefresh()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400);
                await RefreshPlaybackStateAsync();
                await Task.Delay(1200);
                await RefreshPlaybackStateAsync();
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
                _isPolling = true;
                try
                {
                    await RefreshPlaybackStateAsync();
                }
                catch { }
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

    private static string GenerateCodeVerifier()
    {
        byte[] bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public void Dispose()
    {
        StopMonitoring();
        _httpClient?.Dispose();
    }
}
