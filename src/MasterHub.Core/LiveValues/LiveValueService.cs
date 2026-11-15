using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.LiveValues;

/// <summary>
/// Fetches the values shown by live-value keys (T-11). <b>Demand driven</b> like the system monitor (F-36): only the sources of
/// keys that are on screen are fetched, nothing runs while there are none or while the deck is in standby, and a timer
/// exists only while something is wanted. Keys with the same source share one fetch.
/// </summary>
public interface ILiveValueService : IDisposable
{
    /// <summary>Tells the service which sources are on screen right now (everything else stops being fetched).</summary>
    void SetDemand(IReadOnlyCollection<LiveValueSettings> visible);

    /// <summary>True while the deck sleeps: nothing is fetched.</summary>
    bool Paused { get; set; }

    /// <summary>The latest reading of a source (<see cref="LiveReading.Loading"/> before the first fetch).</summary>
    LiveReading GetReading(LiveValueSettings settings);

    /// <summary>Fetches a source now (a press on the key), ignoring its interval.</summary>
    Task RefreshNowAsync(LiveValueSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Raised (on a background thread) with the source id whenever a reading changed.</summary>
    event Action<string>? ReadingChanged;

    /// <summary>Whether this exact command line was approved to run (a changed character needs a new approval).</summary>
    bool IsCommandApproved(string commandLine);

    /// <summary>Approves an exact command line; the next fetch runs it.</summary>
    void ApproveCommand(string commandLine);
}

/// <inheritdoc cref="ILiveValueService"/>
public sealed class LiveValueService : ILiveValueService
{
    /// <summary>Hard caps: response size, request time, and the shortest interval.</summary>
    public const int MaxResponseBytes = 1_000_000;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan CalendarDownloadEvery = TimeSpan.FromMinutes(5);

    private sealed class Entry
    {
        public required LiveValueSettings Settings { get; set; }
        public LiveReading Reading { get; set; } = LiveReading.Loading;
        public DateTimeOffset NextDue { get; set; }
        public DateTimeOffset LastDemand { get; set; }
        public bool Demanded { get; set; }
        public WeatherPlace? Place { get; set; }
        public List<IcsEvent>? Calendar { get; set; }
        public DateTimeOffset CalendarFetched { get; set; }
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly HttpClient _http;
    private readonly LiveTokenStore? _tokens;
    private readonly LiveCommandApprovalStore _approvals;
    private readonly Func<string, string?>? _homeAssistantState;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<string, CancellationToken, Task<string?>> _runCommand;
    private readonly Timer _timer;
    private bool _timerArmed;
    private bool _paused;
    private bool _disposed;
    private int _ticking;

    public LiveValueService(
        LiveTokenStore? tokens = null,
        LiveCommandApprovalStore? approvals = null,
        Func<string, string?>? homeAssistantState = null,
        HttpMessageHandler? handler = null,
        Func<DateTimeOffset>? now = null,
        Func<string, CancellationToken, Task<string?>>? runCommand = null)
    {
        _tokens = tokens;
        _approvals = approvals ?? new LiveCommandApprovalStore();
        _homeAssistantState = homeAssistantState;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _runCommand = runCommand ?? RunCommandAsync;
        _http = handler != null ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
        _http.Timeout = RequestTimeout;
        _timer = new Timer(_ => _ = TickAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event Action<string>? ReadingChanged;

    public bool IsCommandApproved(string commandLine) => _approvals.IsApproved(commandLine);

    public void ApproveCommand(string commandLine) => _approvals.Approve(commandLine);

    /// <summary>True while the timer is armed (something is on screen and the deck is awake) – for tests and diagnostics.</summary>
    public bool IsPolling
    {
        get { lock (_lock) { return _timerArmed; } }
    }

    /// <summary>How many fetches ran so far (for tests: shows that unseen sources are never fetched).</summary>
    public int FetchCount { get; private set; }

    public bool Paused
    {
        get { lock (_lock) { return _paused; } }
        set
        {
            lock (_lock) { _paused = value; Rearm(); }
        }
    }

    public void SetDemand(IReadOnlyCollection<LiveValueSettings> visible)
    {
        lock (_lock)
        {
            var now = _now();
            foreach (var entry in _entries.Values) entry.Demanded = false;

            foreach (var settings in visible)
            {
                string id = settings.SourceId();
                if (!_entries.TryGetValue(id, out var entry))
                {
                    entry = new Entry { Settings = settings, NextDue = now };
                    entry.Reading = settings.IsConfigured ? LiveReading.Loading : LiveReading.SetUp;
                    _entries[id] = entry;
                }
                entry.Settings = settings;
                entry.Demanded = true;
                entry.LastDemand = now;
            }

            // forget sources that were off screen for a long time
            foreach (var stale in _entries.Where(e => !e.Value.Demanded && now - e.Value.LastDemand > TimeSpan.FromMinutes(30)).Select(e => e.Key).ToList())
                _entries.Remove(stale);

            Rearm();
        }
    }

    public LiveReading GetReading(LiveValueSettings settings)
    {
        if (!settings.IsConfigured) return LiveReading.SetUp;
        lock (_lock)
        {
            return _entries.TryGetValue(settings.SourceId(), out var entry) ? entry.Reading : LiveReading.Loading;
        }
    }

    public async Task RefreshNowAsync(LiveValueSettings settings, CancellationToken cancellationToken = default)
    {
        Entry? entry;
        lock (_lock)
        {
            if (_disposed || !_entries.TryGetValue(settings.SourceId(), out entry)) return;
        }
        await FetchEntryAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    // ── the single worker ───────────────────────────────────────────────────────────────────

    private void Rearm()
    {
        bool wanted = !_disposed && !_paused && _entries.Values.Any(e => e.Demanded);
        if (wanted == _timerArmed) return;

        _timerArmed = wanted;
        _timer.Change(wanted ? 1000 : Timeout.Infinite, wanted ? 1000 : Timeout.Infinite);
    }

    /// <summary>One pass over the sources that are on screen and due. Public so tests can drive it without waiting for the timer.</summary>
    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _ticking, 1, 0) != 0) return; // never two passes at once
        try
        {
            List<Entry> due;
            lock (_lock)
            {
                if (_paused || _disposed) return;
                var now = _now();
                due = _entries.Values.Where(e => e.Demanded && e.NextDue <= now).ToList();
            }

            foreach (var entry in due)
            {
                if (cancellationToken.IsCancellationRequested) break;
                await FetchEntryAsync(entry, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    private async Task FetchEntryAsync(Entry entry, CancellationToken ct)
    {
        LiveReading reading;
        var settings = entry.Settings;
        try
        {
            FetchCount++;
            reading = await FetchAsync(entry, settings, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            reading = LiveReading.Error; // timeout, DNS, TLS, bad JSON…: the key shows "–"
        }

        bool changed;
        lock (_lock)
        {
            changed = entry.Reading != reading;
            entry.Reading = reading;
            int seconds = reading.State == LiveReadingState.Error ? Math.Min(settings.EffectiveRefreshSeconds, 30) : settings.EffectiveRefreshSeconds;
            entry.NextDue = _now().AddSeconds(seconds);
        }

        if (changed)
        {
            try { ReadingChanged?.Invoke(settings.SourceId()); } catch { }
        }
    }

    private async Task<LiveReading> FetchAsync(Entry entry, LiveValueSettings s, CancellationToken ct)
    {
        if (!s.IsConfigured) return LiveReading.SetUp;

        switch (s.Kind)
        {
            case LiveValueSourceKind.WebAddress:
            {
                string text = await GetTextAsync(s.Url, s.TokenName, ct).ConfigureAwait(false);
                string? raw = string.IsNullOrWhiteSpace(s.JsonPath) ? FirstLine(text) : JsonPathReader.Read(text, s.JsonPath);
                return raw == null ? LiveReading.Error : new LiveReading(LiveReadingState.Ok, raw, LiveValueFormatter.TryNumber(raw));
            }

            case LiveValueSourceKind.Weather:
                return await FetchWeatherAsync(entry, s, ct).ConfigureAwait(false);

            case LiveValueSourceKind.CalendarNext:
                return await FetchCalendarAsync(entry, s, ct).ConfigureAwait(false);

            case LiveValueSourceKind.HomeAssistant:
            {
                string? state = _homeAssistantState?.Invoke(s.EntityId);
                return state == null ? LiveReading.Error : new LiveReading(LiveReadingState.Ok, state, LiveValueFormatter.TryNumber(state));
            }

            case LiveValueSourceKind.Command:
            {
                if (!_approvals.IsApproved(s.Command)) return LiveReading.NeedsApproval;
                string? line = await _runCommand(s.Command, ct).ConfigureAwait(false);
                return line == null ? LiveReading.Error : new LiveReading(LiveReadingState.Ok, line, LiveValueFormatter.TryNumber(line));
            }
        }

        return LiveReading.SetUp;
    }

    private async Task<LiveReading> FetchWeatherAsync(Entry entry, LiveValueSettings s, CancellationToken ct)
    {
        var place = entry.Place ?? WeatherPlace.Parse(s.City);
        if (place == null)
        {
            // a plain city name: look it up once (Open-Meteo geocoding) and remember the place for this session
            place = OpenMeteo.ParseGeocoding(await GetTextAsync(OpenMeteo.GeocodingUrl(s.City), "", ct).ConfigureAwait(false));
            if (place == null) return LiveReading.Error;
        }
        entry.Place = place;

        var forecast = OpenMeteo.ParseForecast(await GetTextAsync(OpenMeteo.ForecastUrl(place), "", ct).ConfigureAwait(false));
        if (forecast == null) return LiveReading.Error;
        return new LiveReading(LiveReadingState.Ok, $"{forecast.Value.Temperature:0}°", forecast.Value.Temperature, forecast.Value.Word);
    }

    private async Task<LiveReading> FetchCalendarAsync(Entry entry, LiveValueSettings s, CancellationToken ct)
    {
        var now = _now();
        // The calendar itself is downloaded every 5 minutes; in between only the schedule is re-evaluated.
        if (entry.Calendar == null || now - entry.CalendarFetched >= CalendarDownloadEvery)
        {
            string url = s.Url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + s.Url["webcal://".Length..] : s.Url;
            entry.Calendar = IcsParser.Parse(await GetTextAsync(url, s.TokenName, ct).ConfigureAwait(false));
            entry.CalendarFetched = now;
        }

        var next = IcsSchedule.Next(entry.Calendar, now, TimeSpan.FromDays(14));
        if (next == null) return new LiveReading(LiveReadingState.Ok, "–", null, null);

        bool running = next.IsRunning(now);
        double minutes = running ? 0 : Math.Max(0, (next.Start - now).TotalMinutes);
        return new LiveReading(LiveReadingState.Ok, IcsSchedule.Describe(next.Start - now, running), minutes, next.Event.Summary);
    }

    // ── HTTP / command helpers ─────────────────────────────────────────────────────────────

    private async Task<string> GetTextAsync(string url, string tokenName, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("only http(s) addresses are allowed");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        string? token = string.IsNullOrWhiteSpace(tokenName) ? null : _tokens?.Get(tokenName);
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new InvalidOperationException("response too large");

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cts.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) throw new InvalidOperationException("response too large");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string? FirstLine(string text)
    {
        string? line = text.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault(l => l.Length > 0);
        return line == null ? null : (line.Length > 60 ? line[..60] : line);
    }

    /// <summary>Runs an approved command line hidden (<c>cmd /C</c>) and returns its first output line; at most 10 s.</summary>
    private static async Task<string?> RunCommandAsync(string commandLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("cmd.exe", "/C " + commandLine)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CommandTimeout);
        try
        {
            string output = await process.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return FirstLine(output);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _timerArmed = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        _timer.Dispose();
        _http.Dispose();
    }
}
