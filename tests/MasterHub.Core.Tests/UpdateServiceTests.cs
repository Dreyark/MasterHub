using System.Net;
using System.Text;
using MasterHub.Core.Profiles;
using MasterHub.Core.Security;
using MasterHub.Core.Updates;

namespace MasterHub.Core.Tests;

/// <summary>T-32: the update check – quiet by default, careful about what it asks and shows.</summary>
public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_update_" + Guid.NewGuid().ToString("N"));
    private readonly ProfileService _profiles;
    private readonly StubHandler _handler = new();
    private DateTime _now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        public List<HttpRequestMessage> Seen { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    public UpdateServiceTests() => _profiles = new ProfileService(_dir, new SecretProtectionService());

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private UpdateSettings Settings => _profiles.GetAppSettings().Updates;

    private UpdateService Service(string version = "1.2.0") => new(_profiles, _handler, Version.Parse(version), () => _now);

    private static HttpResponseMessage Release(string tag, string url = "https://github.com/acme/deck/releases/tag/v9", string body = "Notes", string etag = "\"abc\"")
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new { tag_name = tag, html_url = url, body });
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return response;
    }

    private void Configure(bool enabled = true, string repository = "acme/deck")
    {
        Settings.Enabled = enabled;
        Settings.Repository = repository;
    }

    // ── quiet by default ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ByDefault_NoNetworkCallIsMade()
    {
        var service = Service();

        var automatic = await service.CheckAsync(userAsked: false);

        Assert.Equal(UpdateOutcome.NotConfigured, automatic.Outcome);
        Assert.False(service.IsCheckDue());
        Assert.Empty(_handler.Seen);
    }

    [Fact]
    public async Task WithARepositoryButSwitchedOff_AnAutomaticCheckStaysQuiet_ButCheckNowWorks()
    {
        Configure(enabled: false);
        _handler.Respond = _ => Release("v2.0.0");
        var service = Service();

        Assert.Equal(UpdateOutcome.Disabled, (await service.CheckAsync(userAsked: false)).Outcome);
        Assert.Empty(_handler.Seen);

        Assert.Equal(UpdateOutcome.Available, (await service.CheckAsync(userAsked: true)).Outcome);
        Assert.Single(_handler.Seen);
    }

    [Theory]
    [InlineData("")]
    [InlineData("acme")]
    [InlineData("acme/deck/extra")]
    [InlineData("https://github.com/acme/deck")]
    [InlineData("acme/de ck")]
    [InlineData("../x/y")]
    public async Task ABadRepository_NeverCausesACall(string repository)
    {
        Configure(repository: repository);

        var result = await Service().CheckAsync(userAsked: true);

        Assert.Equal(UpdateOutcome.NotConfigured, result.Outcome);
        Assert.Empty(_handler.Seen);
    }

    // ── versions ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("v1.3.0", "1.2.0", UpdateOutcome.Available)]
    [InlineData("1.2.1", "1.2.0", UpdateOutcome.Available)]
    [InlineData("v2.0", "1.9.9", UpdateOutcome.Available)]
    [InlineData("v1.2.0", "1.2.0", UpdateOutcome.UpToDate)]
    [InlineData("v1.2.0", "1.2.0.5", UpdateOutcome.UpToDate)]   // the fourth number of the running build is ignored
    [InlineData("v1.1.0", "1.2.0", UpdateOutcome.UpToDate)]
    [InlineData("v1.10.0", "1.9.0", UpdateOutcome.Available)]   // numbers, not text
    [InlineData("nightly", "1.2.0", UpdateOutcome.UpToDate)]    // not a version: nothing to offer
    public async Task TheLatestRelease_IsComparedAsNumbers(string tag, string running, UpdateOutcome expected)
    {
        Configure();
        _handler.Respond = _ => Release(tag);

        var result = await Service(running).CheckAsync(false);

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task AnAvailableUpdate_CarriesVersionPageAndNotes()
    {
        Configure();
        _handler.Respond = _ => Release("v1.5.0", "https://github.com/acme/deck/releases/tag/v1.5.0", "What changed");

        var result = await Service().CheckAsync(false);

        Assert.True(result.IsUpdate);
        Assert.Equal("1.5.0", result.LatestVersion);
        Assert.Equal("https://github.com/acme/deck/releases/tag/v1.5.0", result.Url);
        Assert.Equal("What changed", result.Notes);
    }

    [Theory]
    [InlineData("https://evil.example.com/download.exe")]
    [InlineData("http://github.com/acme/deck/releases/tag/v9")]
    [InlineData("https://github.com/someone-else/deck/releases/tag/v9")]
    [InlineData("javascript:alert(1)")]
    public async Task APageThatIsNotOnTheRepository_IsReplacedByTheReleasesPage(string url)
    {
        Configure();
        _handler.Respond = _ => Release("v9.0.0", url);

        var result = await Service().CheckAsync(false);

        Assert.Equal("https://github.com/acme/deck/releases", result.Url);   // the app only ever opens this
    }

    [Fact]
    public async Task LongNotes_AreCut()
    {
        Configure();
        _handler.Respond = _ => Release("v9.0.0", body: new string('x', UpdateService.MaxNotes * 3));

        var result = await Service().CheckAsync(false);

        Assert.Equal(UpdateService.MaxNotes, result.Notes!.Length);
    }

    // ── the request ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheRequest_AsksForTheLatestReleaseOfThatRepository_WithAGenericClientName()
    {
        Configure();
        _handler.Respond = _ => Release("v1.0.0");

        await Service().CheckAsync(false);

        var request = Assert.Single(_handler.Seen);
        Assert.Equal("https://api.github.com/repos/acme/deck/releases/latest", request.RequestUri!.ToString());
        Assert.Equal("MasterHub-update-check", request.Headers.UserAgent.ToString());
        Assert.False(request.Headers.Contains("Authorization"));
        Assert.False(request.Headers.Contains("Cookie"));
    }

    // ── ETag, 304 ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheSecondCheck_IsConditional_AndA304StillKnowsTheUpdate()
    {
        Configure();
        _handler.Respond = _ => Release("v3.0.0", etag: "\"v3\"");
        var service = Service();
        await service.CheckAsync(false);

        _handler.Respond = r => r.Headers.TryGetValues("If-None-Match", out var v) && v.Single() == "\"v3\""
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : Release("v0.0.1");
        var again = await service.CheckAsync(false);

        Assert.Equal(UpdateOutcome.Available, again.Outcome);   // taken from what was remembered
        Assert.Equal("3.0.0", again.LatestVersion);
        Assert.Equal(2, _handler.Seen.Count);
    }

    [Fact]
    public async Task WhatWasLearned_SurvivesARestart()
    {
        Configure();
        _handler.Respond = _ => Release("v3.0.0", etag: "\"v3\"");
        await Service().CheckAsync(false);

        var reloaded = new ProfileService(_dir, new SecretProtectionService()).GetAppSettings().Updates;

        Assert.Equal("\"v3\"", reloaded.ETag);
        Assert.Equal("v3.0.0", reloaded.LatestVersion);
        Assert.Equal(_now, reloaded.LastCheckUtc);
    }

    // ── every 6 hours ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAutomaticCheck_IsDueEverySixHours_NotBefore()
    {
        Configure();
        _handler.Respond = _ => Release("v1.0.0");
        var service = Service();
        Assert.True(service.IsCheckDue());   // never checked

        await service.CheckAsync(false);

        _now = _now.AddHours(5).AddMinutes(59);
        Assert.False(service.IsCheckDue());
        _now = _now.AddMinutes(2);
        Assert.True(service.IsCheckDue());
    }

    [Fact]
    public async Task AClockSetBack_DoesNotBlockChecksForever()
    {
        Configure();
        _handler.Respond = _ => Release("v1.0.0");
        var service = Service();
        await service.CheckAsync(false);

        _now = _now.AddDays(-3);   // the last check is "in the future"

        Assert.True(service.IsCheckDue());
    }

    // ── trouble ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARateLimit_IsExplained_WithTheTimeToTryAgain()
    {
        Configure();
        long reset = new DateTimeOffset(_now.AddMinutes(30)).ToUnixTimeSeconds();
        _handler.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.Add("x-ratelimit-reset", reset.ToString());
            return response;
        };

        var result = await Service().CheckAsync(true);

        Assert.Equal(UpdateOutcome.Failed, result.Outcome);
        Assert.Contains("limit", result.Problem);
        Assert.Contains("Try again after", result.Problem);
    }

    [Fact]
    public async Task NoConnection_IsReportedNotThrown_AndTheKnownUpdateIsKept()
    {
        Configure();
        _handler.Respond = _ => Release("v3.0.0");
        var service = Service();
        await service.CheckAsync(false);

        _handler.Respond = _ => throw new HttpRequestException("offline");
        var result = await service.CheckAsync(false);

        Assert.Equal(UpdateOutcome.Failed, result.Outcome);
        Assert.Contains("No connection", result.Problem);
        Assert.Equal("v3.0.0", Settings.LatestVersion);   // what was learned is not thrown away
    }

    [Fact]
    public async Task AnAnswerThatIsNotJson_IsReported()
    {
        Configure();
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>captive portal</html>") };

        var result = await Service().CheckAsync(false);

        Assert.Equal(UpdateOutcome.Failed, result.Outcome);
        Assert.Contains("could not be read", result.Problem);
    }

    [Fact]
    public async Task NoReleaseYet_IsNotAnError()
    {
        Configure();
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var result = await Service().CheckAsync(false);

        Assert.Equal(UpdateOutcome.UpToDate, result.Outcome);
    }

    [Fact]
    public async Task EveryResult_IsPublished()
    {
        Configure();
        _handler.Respond = _ => Release("v9.0.0");
        var service = Service();
        var seen = new List<UpdateOutcome>();
        service.Changed += r => seen.Add(r.Outcome);

        await service.CheckAsync(false);

        Assert.Equal(new[] { UpdateOutcome.Available }, seen);
        Assert.Equal(UpdateOutcome.Available, service.Last!.Outcome);
    }
}
