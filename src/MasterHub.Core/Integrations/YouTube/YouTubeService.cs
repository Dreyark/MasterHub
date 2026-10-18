using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.YouTube;

/// <summary>
/// Implementacja serwisu YouTube Live (Etap 4.9).
/// Obsługuje oficjalne YouTube Data API v3 (videos?part=liveStreamingDetails,snippet,statistics oraz search?eventType=live)
/// oraz bezkluczowy tryb publiczny (odczyt metadanych ytInitialPlayerResponse ze strony transmisji).
/// </summary>
public sealed class YouTubeService : IYouTubeService
{
    private const string YouTubeApiBaseUrl = "https://www.googleapis.com/youtube/v3";

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly object _syncLock = new();

    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private bool _disposed;

    private bool _isLive;
    private int _concurrentViewers;
    private int _likeCount;
    private string _streamTitle = string.Empty;
    private string _channelTitle = string.Empty;
    private string _targetIdentifier = string.Empty;
    private string _apiKey = string.Empty;
    private YouTubeStreamStatus? _lastStatus;

    public bool IsLive => _isLive;
    public int ConcurrentViewers => _concurrentViewers;
    public int LikeCount => _likeCount;
    public string StreamTitle => _streamTitle;
    public string ChannelTitle => _channelTitle;
    public string TargetIdentifier => _targetIdentifier;
    public string ApiKey => _apiKey;
    public bool IsPolling => _pollingCts != null && !_pollingCts.IsCancellationRequested;
    public YouTubeStreamStatus? LastStatus => _lastStatus;

    public event Action<YouTubeStreamStatus>? StatusUpdated;
    public event Action<bool>? LiveStateChanged;
    public event Action<string>? LogMessage;

    public YouTubeService(HttpMessageHandler? httpMessageHandler = null)
    {
        if (httpMessageHandler != null)
        {
            _httpClient = new HttpClient(httpMessageHandler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _disposeHttpClient = true;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _disposeHttpClient = true;
        }

        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MasterHub/1.0");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept-Language",
            "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
    }

    public void Configure(string? channelOrVideoId, string? apiKey = null)
    {
        _targetIdentifier = (channelOrVideoId ?? string.Empty).Trim();
        if (apiKey != null)
        {
            _apiKey = apiKey.Trim();
        }
    }

    public async Task<YouTubeStreamStatus> FetchStreamStatusAsync(
        string? channelOrVideoId = null,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        string rawTarget = !string.IsNullOrWhiteSpace(channelOrVideoId)
            ? channelOrVideoId.Trim()
            : _targetIdentifier;

        string effectiveApiKey = apiKey != null
            ? apiKey.Trim()
            : _apiKey;

        if (string.IsNullOrWhiteSpace(rawTarget))
        {
            var emptyResult = new YouTubeStreamStatus(
                Success: false,
                IsLive: false,
                ConcurrentViewers: 0,
                LikeCount: 0,
                StreamTitle: string.Empty,
                ChannelTitle: string.Empty,
                VideoId: null,
                ErrorMessage: "Nie podano identyfikatora kanału ani transmisji YouTube.");
            ApplyStatus(emptyResult);
            return emptyResult;
        }

        try
        {
            var parsed = ParseTargetIdentifier(rawTarget);
            YouTubeStreamStatus status;

            if (!string.IsNullOrWhiteSpace(effectiveApiKey))
            {
                status = await FetchViaDataApiV3Async(parsed, effectiveApiKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                status = await FetchViaPublicPageAsync(parsed, cancellationToken).ConfigureAwait(false);
            }

            ApplyStatus(status);
            return status;
        }
        catch (OperationCanceledException)
        {
            var cancelled = new YouTubeStreamStatus(
                Success: false,
                IsLive: _isLive,
                ConcurrentViewers: _concurrentViewers,
                LikeCount: _likeCount,
                StreamTitle: _streamTitle,
                ChannelTitle: _channelTitle,
                VideoId: _lastStatus?.VideoId,
                ErrorMessage: "Operacja odpytywania YouTube została anulowana.");
            return cancelled;
        }
        catch (Exception ex)
        {
            string err = $"Błąd komunikacji z YouTube: {ex.Message}";
            LogMessage?.Invoke($"[YOUTUBE] ⚠️ {err}");
            var failResult = new YouTubeStreamStatus(
                Success: false,
                IsLive: false,
                ConcurrentViewers: 0,
                LikeCount: 0,
                StreamTitle: string.Empty,
                ChannelTitle: string.Empty,
                VideoId: null,
                ErrorMessage: err);
            ApplyStatus(failResult);
            return failResult;
        }
    }

    private async Task<YouTubeStreamStatus> FetchViaDataApiV3Async(
        (string Identifier, bool IsChannelId, bool IsHandle, bool IsVideoId) target,
        string apiKey,
        CancellationToken cancellationToken)
    {
        string? videoId = null;
        string resolvedChannelTitle = string.Empty;

        if (target.IsVideoId)
        {
            videoId = target.Identifier;
        }
        else
        {
            string? channelId = target.IsChannelId ? target.Identifier : null;

            if (target.IsHandle || (!target.IsChannelId && !target.IsVideoId))
            {
                string handleParam = target.Identifier.TrimStart('@');
                string channelUrl = $"{YouTubeApiBaseUrl}/channels?part=id,snippet&forHandle={Uri.EscapeDataString(handleParam)}&key={Uri.EscapeDataString(apiKey)}";

                using var chResp = await _httpClient.GetAsync(channelUrl, cancellationToken).ConfigureAwait(false);
                string chBody = await chResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!chResp.IsSuccessStatusCode)
                {
                    string apiErr = ExtractApiErrorMessage(chBody, (int)chResp.StatusCode);
                    return new YouTubeStreamStatus(false, false, 0, 0, string.Empty, string.Empty, null, apiErr);
                }

                using var chDoc = JsonDocument.Parse(chBody);
                if (chDoc.RootElement.TryGetProperty("items", out var chItems) &&
                    chItems.ValueKind == JsonValueKind.Array &&
                    chItems.GetArrayLength() > 0)
                {
                    var firstCh = chItems[0];
                    if (firstCh.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                    {
                        channelId = idProp.GetString();
                    }
                    if (firstCh.TryGetProperty("snippet", out var snip) &&
                        snip.TryGetProperty("title", out var chTitleProp) &&
                        chTitleProp.ValueKind == JsonValueKind.String)
                    {
                        resolvedChannelTitle = chTitleProp.GetString() ?? string.Empty;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(channelId))
            {
                return new YouTubeStreamStatus(
                    Success: false,
                    IsLive: false,
                    ConcurrentViewers: 0,
                    LikeCount: 0,
                    StreamTitle: string.Empty,
                    ChannelTitle: string.Empty,
                    VideoId: null,
                    ErrorMessage: $"Nie znaleziono kanału YouTube dla: {target.Identifier}");
            }

            string searchUrl = $"{YouTubeApiBaseUrl}/search?part=id,snippet&channelId={Uri.EscapeDataString(channelId)}&eventType=live&type=video&maxResults=1&key={Uri.EscapeDataString(apiKey)}";
            using var searchResp = await _httpClient.GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);
            string searchBody = await searchResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!searchResp.IsSuccessStatusCode)
            {
                string apiErr = ExtractApiErrorMessage(searchBody, (int)searchResp.StatusCode);
                return new YouTubeStreamStatus(false, false, 0, 0, string.Empty, resolvedChannelTitle, null, apiErr);
            }

            using var searchDoc = JsonDocument.Parse(searchBody);
            if (searchDoc.RootElement.TryGetProperty("items", out var searchItems) &&
                searchItems.ValueKind == JsonValueKind.Array &&
                searchItems.GetArrayLength() > 0)
            {
                var firstItem = searchItems[0];
                if (firstItem.TryGetProperty("id", out var idObj) &&
                    idObj.ValueKind == JsonValueKind.Object &&
                    idObj.TryGetProperty("videoId", out var vidProp) &&
                    vidProp.ValueKind == JsonValueKind.String)
                {
                    videoId = vidProp.GetString();
                }

                if (firstItem.TryGetProperty("snippet", out var sSnip) &&
                    sSnip.TryGetProperty("channelTitle", out var sChTitle) &&
                    sChTitle.ValueKind == JsonValueKind.String)
                {
                    resolvedChannelTitle = sChTitle.GetString() ?? resolvedChannelTitle;
                }
            }

            if (string.IsNullOrWhiteSpace(videoId))
            {
                // Kanał istnieje, ale aktualnie nie prowadzi transmisji na żywo (OFFLINE)
                return new YouTubeStreamStatus(
                    Success: true,
                    IsLive: false,
                    ConcurrentViewers: 0,
                    LikeCount: 0,
                    StreamTitle: string.Empty,
                    ChannelTitle: resolvedChannelTitle,
                    VideoId: null,
                    ErrorMessage: null);
            }
        }

        string videosUrl = $"{YouTubeApiBaseUrl}/videos?part=liveStreamingDetails,snippet,statistics&id={Uri.EscapeDataString(videoId!)}&key={Uri.EscapeDataString(apiKey)}";
        using var vidResp = await _httpClient.GetAsync(videosUrl, cancellationToken).ConfigureAwait(false);
        string vidBody = await vidResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!vidResp.IsSuccessStatusCode)
        {
            string apiErr = ExtractApiErrorMessage(vidBody, (int)vidResp.StatusCode);
            return new YouTubeStreamStatus(false, false, 0, 0, string.Empty, resolvedChannelTitle, videoId, apiErr);
        }

        using var vidDoc = JsonDocument.Parse(vidBody);
        if (!vidDoc.RootElement.TryGetProperty("items", out var vidItems) ||
            vidItems.ValueKind != JsonValueKind.Array ||
            vidItems.GetArrayLength() == 0)
        {
            return new YouTubeStreamStatus(
                Success: true,
                IsLive: false,
                ConcurrentViewers: 0,
                LikeCount: 0,
                StreamTitle: string.Empty,
                ChannelTitle: resolvedChannelTitle,
                VideoId: videoId,
                ErrorMessage: null);
        }

        var item = vidItems[0];
        string streamTitle = string.Empty;
        string channelTitle = resolvedChannelTitle;
        string liveBroadcastContent = "none";

        if (item.TryGetProperty("snippet", out var snippet) && snippet.ValueKind == JsonValueKind.Object)
        {
            if (snippet.TryGetProperty("title", out var tProp) && tProp.ValueKind == JsonValueKind.String)
            {
                streamTitle = tProp.GetString() ?? string.Empty;
            }
            if (snippet.TryGetProperty("channelTitle", out var cProp) && cProp.ValueKind == JsonValueKind.String)
            {
                channelTitle = cProp.GetString() ?? channelTitle;
            }
            if (snippet.TryGetProperty("liveBroadcastContent", out var lbcProp) && lbcProp.ValueKind == JsonValueKind.String)
            {
                liveBroadcastContent = lbcProp.GetString() ?? "none";
            }
        }

        int concurrentViewers = 0;
        bool hasActualStart = false;
        bool hasActualEnd = false;
        bool hasConcurrentViewersField = false;

        if (item.TryGetProperty("liveStreamingDetails", out var liveDetails) && liveDetails.ValueKind == JsonValueKind.Object)
        {
            if (liveDetails.TryGetProperty("concurrentViewers", out var cvProp))
            {
                hasConcurrentViewersField = true;
                if (cvProp.ValueKind == JsonValueKind.String && int.TryParse(cvProp.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCv))
                {
                    concurrentViewers = Math.Max(0, parsedCv);
                }
                else if (cvProp.ValueKind == JsonValueKind.Number && cvProp.TryGetInt32(out int numCv))
                {
                    concurrentViewers = Math.Max(0, numCv);
                }
            }

            if (liveDetails.TryGetProperty("actualStartTime", out var startProp) &&
                startProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(startProp.GetString()))
            {
                hasActualStart = true;
            }

            if (liveDetails.TryGetProperty("actualEndTime", out var endProp) &&
                endProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(endProp.GetString()))
            {
                hasActualEnd = true;
            }
        }

        int likeCount = 0;
        if (item.TryGetProperty("statistics", out var stats) && stats.ValueKind == JsonValueKind.Object)
        {
            if (stats.TryGetProperty("likeCount", out var lcProp))
            {
                if (lcProp.ValueKind == JsonValueKind.String && int.TryParse(lcProp.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLikes))
                {
                    likeCount = Math.Max(0, parsedLikes);
                }
                else if (lcProp.ValueKind == JsonValueKind.Number && lcProp.TryGetInt32(out int numLikes))
                {
                    likeCount = Math.Max(0, numLikes);
                }
            }
        }

        bool isLive = !hasActualEnd && (
            string.Equals(liveBroadcastContent, "live", StringComparison.OrdinalIgnoreCase) ||
            hasConcurrentViewersField ||
            hasActualStart);

        if (!isLive)
        {
            concurrentViewers = 0;
        }

        return new YouTubeStreamStatus(
            Success: true,
            IsLive: isLive,
            ConcurrentViewers: concurrentViewers,
            LikeCount: likeCount,
            StreamTitle: streamTitle,
            ChannelTitle: channelTitle,
            VideoId: videoId,
            ErrorMessage: null);
    }

    private async Task<YouTubeStreamStatus> FetchViaPublicPageAsync(
        (string Identifier, bool IsChannelId, bool IsHandle, bool IsVideoId) target,
        CancellationToken cancellationToken)
    {
        string url;
        if (target.IsChannelId)
        {
            url = $"https://www.youtube.com/channel/{Uri.EscapeDataString(target.Identifier)}/live";
        }
        else if (target.IsHandle)
        {
            string handle = target.Identifier.StartsWith('@') ? target.Identifier : "@" + target.Identifier;
            url = $"https://www.youtube.com/{handle}/live";
        }
        else
        {
            url = $"https://www.youtube.com/watch?v={Uri.EscapeDataString(target.Identifier)}";
        }

        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new YouTubeStreamStatus(
                Success: false,
                IsLive: false,
                ConcurrentViewers: 0,
                LikeCount: 0,
                StreamTitle: string.Empty,
                ChannelTitle: string.Empty,
                VideoId: target.IsVideoId ? target.Identifier : null,
                ErrorMessage: $"Kod odpowiedzi HTTP {(int)response.StatusCode}");
        }

        string html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        bool isLive = Regex.IsMatch(html, @"""isLive""\s*:\s*true", RegexOptions.CultureInvariant) ||
                      Regex.IsMatch(html, @"""isLiveNow""\s*:\s*true", RegexOptions.CultureInvariant);

        int viewers = 0;
        var concurrentMatch = Regex.Match(html, @"""concurrentViewers""\s*:\s*""?(\d+)""?", RegexOptions.CultureInvariant);
        if (concurrentMatch.Success && int.TryParse(concurrentMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cv))
        {
            viewers = cv;
        }
        else if (isLive)
        {
            var viewCountMatch = Regex.Match(html, @"""viewCount""\s*:\s*""(\d+)""", RegexOptions.CultureInvariant);
            if (viewCountMatch.Success && int.TryParse(viewCountMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int vc))
            {
                viewers = vc;
            }
        }

        string? videoId = target.IsVideoId ? target.Identifier : null;
        var videoIdMatch = Regex.Match(html, @"""videoDetails""\s*:\s*\{[^}]*""videoId""\s*:\s*""([a-zA-Z0-9_-]{11})""", RegexOptions.CultureInvariant);
        if (videoIdMatch.Success)
        {
            videoId = videoIdMatch.Groups[1].Value;
        }

        string title = string.Empty;
        var titleMatch = Regex.Match(html, @"""videoDetails""\s*:\s*\{[^}]*""title""\s*:\s*""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.CultureInvariant);
        if (titleMatch.Success)
        {
            title = Regex.Unescape(titleMatch.Groups[1].Value);
        }

        string author = string.Empty;
        var authorMatch = Regex.Match(html, @"""videoDetails""\s*:\s*\{[^}]*""author""\s*:\s*""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.CultureInvariant);
        if (authorMatch.Success)
        {
            author = Regex.Unescape(authorMatch.Groups[1].Value);
        }

        if (!isLive)
        {
            viewers = 0;
        }

        return new YouTubeStreamStatus(
            Success: true,
            IsLive: isLive,
            ConcurrentViewers: viewers,
            LikeCount: 0,
            StreamTitle: title,
            ChannelTitle: author,
            VideoId: videoId,
            ErrorMessage: null);
    }

    private void ApplyStatus(YouTubeStreamStatus status)
    {
        bool previousLive = _isLive;
        bool liveChanged = false;

        lock (_syncLock)
        {
            _lastStatus = status;
            if (status.Success)
            {
                liveChanged = (_isLive != status.IsLive);
                _isLive = status.IsLive;
                _concurrentViewers = status.IsLive ? Math.Max(0, status.ConcurrentViewers) : 0;
                _likeCount = Math.Max(0, status.LikeCount);
                _streamTitle = status.StreamTitle ?? string.Empty;
                _channelTitle = status.ChannelTitle ?? string.Empty;
            }
            else
            {
                liveChanged = _isLive;
                _isLive = false;
                _concurrentViewers = 0;
            }
        }

        if (liveChanged)
        {
            LiveStateChanged?.Invoke(_isLive);
            if (_isLive)
            {
                LogMessage?.Invoke($"[YOUTUBE] 🔴 Transmisja NA ŻYWO! Widzowie: {_concurrentViewers} ({_channelTitle})");
            }
            else if (previousLive)
            {
                LogMessage?.Invoke("[YOUTUBE] ⚫ Transmisja YouTube zakończona (OFFLINE).");
            }
        }

        StatusUpdated?.Invoke(status);
    }

    public void StartPolling(int intervalSeconds = 30)
    {
        StopPolling();

        int clampedSeconds = Math.Clamp(intervalSeconds, 5, 3600);
        var cts = new CancellationTokenSource();
        _pollingCts = cts;

        _pollingTask = Task.Run(async () =>
        {
            try
            {
                await FetchStreamStatusAsync(cancellationToken: cts.Token).ConfigureAwait(false);

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(clampedSeconds));
                while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
                {
                    await FetchStreamStatusAsync(cancellationToken: cts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normalne zatrzymanie pętli odpytywania
            }
        }, cts.Token);
    }

    public void StopPolling()
    {
        var cts = Interlocked.Exchange(ref _pollingCts, null);
        if (cts != null)
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { }
        }
        _pollingTask = null;
    }

    /// <summary>
    /// Rozpoznaje czy podany ciąg wejściowy to pełny adres URL YouTube, ID kanału (UC...), @handle czy 11-znakowe ID wideo.
    /// </summary>
    public static (string Identifier, bool IsChannelId, bool IsHandle, bool IsVideoId) ParseTargetIdentifier(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return (string.Empty, false, false, false);
        }

        string input = rawInput.Trim();

        // 1. Sprawdź czy to pełny URL YouTube
        if (input.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            input.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            // watch?v=VIDEO_ID
            var watchMatch = Regex.Match(input, @"[?&]v=([a-zA-Z0-9_-]{11})", RegexOptions.CultureInvariant);
            if (watchMatch.Success)
            {
                return (watchMatch.Groups[1].Value, false, false, true);
            }

            // youtu.be/VIDEO_ID lub youtube.com/live/VIDEO_ID lub youtube.com/embed/VIDEO_ID
            var shortMatch = Regex.Match(input, @"(?:youtu\.be/|youtube\.com/(?:live|embed|v)/)([a-zA-Z0-9_-]{11})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (shortMatch.Success)
            {
                return (shortMatch.Groups[1].Value, false, false, true);
            }

            // youtube.com/channel/UC...
            var channelMatch = Regex.Match(input, @"youtube\.com/channel/(UC[a-zA-Z0-9_-]{20,28})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (channelMatch.Success)
            {
                return (channelMatch.Groups[1].Value, true, false, false);
            }

            // youtube.com/@Handle
            var handleMatch = Regex.Match(input, @"youtube\.com/@([a-zA-Z0-9_.-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (handleMatch.Success)
            {
                return ("@" + handleMatch.Groups[1].Value, false, true, false);
            }
        }

        // 2. Sprawdź czy to @handle
        if (input.StartsWith('@'))
        {
            return (input, false, true, false);
        }

        // 3. Sprawdź czy to Channel ID zaczynający się od UC (24 znaki standardowo)
        if (input.StartsWith("UC", StringComparison.Ordinal) && input.Length >= 22 && input.Length <= 30)
        {
            return (input, true, false, false);
        }

        // 4. Sprawdź czy to dokładnie 11-znakowe Video ID
        if (input.Length == 11 && Regex.IsMatch(input, @"^[a-zA-Z0-9_-]{11}$", RegexOptions.CultureInvariant))
        {
            return (input, false, false, true);
        }

        // 5. W przeciwnym razie traktuj jako @handle / nazwę kanału
        return (input, false, true, false);
    }

    /// <summary>
    /// Formatuje liczbę widzów do zwięzłej postaci na ekran LCD 72x72 (np. "840", "1.4K", "25.8K", "1.2M").
    /// </summary>
    public static string FormatViewerCount(int viewers)
    {
        if (viewers < 0) viewers = 0;
        if (viewers >= 1_000_000)
        {
            return (viewers / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M";
        }
        if (viewers >= 10_000)
        {
            return (viewers / 1_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "K";
        }
        if (viewers >= 1_000)
        {
            return (viewers / 1_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "K";
        }
        return viewers.ToString(CultureInfo.InvariantCulture);
    }

    private static string ExtractApiErrorMessage(string responseBody, int statusCode)
    {
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("error", out var errObj) &&
                    errObj.ValueKind == JsonValueKind.Object &&
                    errObj.TryGetProperty("message", out var msgProp) &&
                    msgProp.ValueKind == JsonValueKind.String)
                {
                    return $"YouTube API ({statusCode}): {msgProp.GetString()}";
                }
            }
            catch { }
        }
        return $"Błąd HTTP {statusCode} podczas zapytania do YouTube Data API v3.";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPolling();
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
