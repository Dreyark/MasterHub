using System;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Profiles;

namespace MasterHub.Core.Updates;

/// <summary>
/// The update check (T-32). <b>Off by default, and without a repository it does nothing at all – no network call.</b> It only looks at the
/// public GitHub Releases list of the repository the user entered, never installs anything and sends nothing about the user.
/// </summary>
public sealed class UpdateSettings
{
    /// <summary>Check at start and every 6 hours.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary><c>owner/name</c> of the GitHub repository whose releases are looked at. Empty = the feature is inactive.</summary>
    public string Repository { get; set; } = "";

    public DateTime? LastCheckUtc { get; set; }

    /// <summary>The ETag of the last answer: the next check is conditional, and "not modified" does not count against the rate limit.</summary>
    public string ETag { get; set; } = "";

    /// <summary>What the last successful answer said (kept so a 304 can still tell the user about the update).</summary>
    public string LatestVersion { get; set; } = "";
    public string LatestUrl { get; set; } = "";
    public string LatestNotes { get; set; } = "";
}

public enum UpdateOutcome
{
    /// <summary>Automatic checks are off (and the user did not ask).</summary>
    Disabled = 0,
    /// <summary>No (valid) repository entered.</summary>
    NotConfigured = 1,
    UpToDate = 2,
    Available = 3,
    Failed = 4
}

public sealed record UpdateResult(UpdateOutcome Outcome, string? LatestVersion, string? Url, string? Notes, string? Problem, DateTime CheckedAtUtc)
{
    public bool IsUpdate => Outcome == UpdateOutcome.Available;
}

public interface IUpdateService
{
    /// <summary>The last result of this run, or <c>null</c> when nothing was checked yet.</summary>
    UpdateResult? Last { get; }

    event Action<UpdateResult>? Changed;

    /// <summary>Whether an automatic check is due now (switched on, a repository, and none within the last 6 hours).</summary>
    bool IsCheckDue();

    /// <param name="userAsked"><c>true</c> for "Check now": it works even when the automatic check is off (but still needs a repository).</param>
    Task<UpdateResult> CheckAsync(bool userAsked, CancellationToken ct = default);
}

/// <inheritdoc cref="IUpdateService"/>
public sealed partial class UpdateService : IUpdateService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    public const int MaxNotes = 4000;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}/[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex RepositoryRegex();

    private readonly IProfileService _profiles;
    private readonly HttpClient _http;
    private readonly Version _current;
    private readonly Func<DateTime> _clock;
    private readonly object _lock = new();
    private UpdateResult? _last;

    /// <param name="handler">Tests give a fake handler.</param>
    /// <param name="current">The running version (default: this assembly's).</param>
    public UpdateService(IProfileService profiles, HttpMessageHandler? handler = null, Version? current = null, Func<DateTime>? clock = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(15);
        // a generic client name: nothing about the user or the machine
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MasterHub-update-check");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _current = current ?? typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public event Action<UpdateResult>? Changed;

    public UpdateResult? Last
    {
        get { lock (_lock) return _last; }
    }

    private UpdateSettings Settings => _profiles.GetAppSettings().Updates;

    public static bool IsValidRepository(string? repository) => RepositoryRegex().IsMatch((repository ?? "").Trim());

    public bool IsCheckDue()
    {
        var s = Settings;
        if (!s.Enabled || !IsValidRepository(s.Repository)) return false;
        return s.LastCheckUtc is not { } last || _clock() - last >= Interval || last > _clock();
    }

    public async Task<UpdateResult> CheckAsync(bool userAsked, CancellationToken ct = default)
    {
        var s = Settings;
        string repo = (s.Repository ?? "").Trim();
        var now = _clock();

        if (!IsValidRepository(repo)) return Publish(new UpdateResult(UpdateOutcome.NotConfigured, null, null, null, null, now));
        if (!s.Enabled && !userAsked) return Publish(new UpdateResult(UpdateOutcome.Disabled, null, null, null, null, now));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
            if (s.ETag.Length > 0 && s.LatestVersion.Length > 0) request.Headers.TryAddWithoutValidation("If-None-Match", s.ETag);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
                return Remember(Compare(s.LatestVersion, s.LatestUrl, s.LatestNotes, repo, now), now, null);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Remember(new UpdateResult(UpdateOutcome.UpToDate, null, null, null, null, now), now, null);   // nothing published yet

            if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
                return Publish(new UpdateResult(UpdateOutcome.Failed, null, null, null, RateLimitProblem(response), now));

            if (!response.IsSuccessStatusCode)
                return Publish(new UpdateResult(UpdateOutcome.Failed, null, null, null, $"GitHub answered {(int)response.StatusCode}.", now));

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tag = Text(root, "tag_name");
            string url = Text(root, "html_url");
            string notes = Text(root, "body");
            if (notes.Length > MaxNotes) notes = notes[..MaxNotes];

            string? etag = response.Headers.ETag?.ToString();
            var result = Compare(tag, url, notes, repo, now);
            return Remember(result, now, etag, tag, SafeUrl(url, repo), notes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // offline, timed out or an answer we do not understand: say so, keep what we knew, try again later
            return Publish(new UpdateResult(UpdateOutcome.Failed, null, null, null, ex is JsonException ? "GitHub sent an answer that could not be read." : "No connection to GitHub.", now));
        }
    }

    private UpdateResult Compare(string tag, string url, string notes, string repo, DateTime now)
    {
        if (!TryParseVersion(tag, out var latest))
            return new UpdateResult(UpdateOutcome.UpToDate, null, null, null, null, now);

        if (Normalize(latest) > Normalize(_current))
            return new UpdateResult(UpdateOutcome.Available, Normalize(latest).ToString(3), SafeUrl(url, repo), notes, null, now);

        return new UpdateResult(UpdateOutcome.UpToDate, Normalize(latest).ToString(3), null, null, null, now);
    }

    private UpdateResult Remember(UpdateResult result, DateTime now, string? etag, string? tag = null, string? url = null, string? notes = null)
    {
        var s = Settings;
        s.LastCheckUtc = now;
        if (etag != null)
        {
            s.ETag = etag;
            s.LatestVersion = tag ?? "";
            s.LatestUrl = url ?? "";
            s.LatestNotes = notes ?? "";
        }

        try { _profiles.SaveAppSettings(_profiles.GetAppSettings()); } catch { /* the check itself worked; the cache is a convenience */ }
        return Publish(result);
    }

    private UpdateResult Publish(UpdateResult result)
    {
        lock (_lock) _last = result;
        try { Changed?.Invoke(result); } catch { }
        return result;
    }

    /// <summary>"v1.2.3", "1.2", "1.2.3-beta" → 1.2.3 / 1.2 / 1.2.3 (the numbers before a suffix).</summary>
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        string t = (tag ?? "").Trim().TrimStart('v', 'V');
        int cut = t.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) t = t[..cut];
        if (!t.Contains('.')) return false;
        return Version.TryParse(t, out version!);
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>The release page must be on github.com in this repository; anything else the answer says is replaced by the releases page.</summary>
    private static string SafeUrl(string url, string repo)
    {
        string fallback = $"https://github.com/{repo}/releases";
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com" &&
               uri.AbsolutePath.StartsWith($"/{repo}/", StringComparison.OrdinalIgnoreCase)
            ? uri.ToString()
            : fallback;
    }

    private string RateLimitProblem(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-ratelimit-reset", out var values) && long.TryParse(values.FirstOrDefault(), out long reset))
        {
            var at = DateTimeOffset.FromUnixTimeSeconds(reset).ToLocalTime();
            return $"GitHub's limit for checks was reached. Try again after {at:HH:mm}.";
        }

        return "GitHub's limit for checks was reached. Try again later.";
    }

    private static string Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
}
