using System.Net;
using System.Text;
using MasterHub.Core.LiveValues;
using MasterHub.Core.Security;

namespace MasterHub.Core.Tests;

/// <summary>T-11: live values – JSON path, formatting and thresholds, ICS schedule, Open-Meteo, command approval, demand-driven fetching.</summary>
public sealed class LiveValueTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_live_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ── JSON path ───────────────────────────────────────────────────────────────────────────

    private const string Json = """{"data":{"items":[{"price":123.45,"name":"BTC"},{"price":9}],"ok":true,"label":"Fine"}}""";

    [Theory]
    [InlineData("data.items.0.price", "123.45")]
    [InlineData("data.items[0].price", "123.45")]
    [InlineData("data.items[1].price", "9")]
    [InlineData("data.items.0.name", "BTC")]
    [InlineData("data.ok", "true")]
    [InlineData("DATA.Label", "Fine")]          // case-insensitive as a convenience
    [InlineData("data.items.5.price", null)]
    [InlineData("data.nothing", null)]
    [InlineData("data.label.x", null)]
    public void JsonPath_BothSyntaxes(string path, string? expected) => Assert.Equal(expected, JsonPathReader.Read(Json, path));

    [Fact]
    public void JsonPath_EmptyPath_IsThePlainText_AndBadJsonIsNull()
    {
        Assert.Equal("hello", JsonPathReader.Read("  hello ", ""));
        Assert.Null(JsonPathReader.Read("not json", "a.b"));
    }

    // ── formatting & thresholds ────────────────────────────────────────────────────────────

    [Fact]
    public void Render_AppliesBeforeAfterAndDecimals()
    {
        var s = new LiveValueSettings { Before = "$", After = " USD", Decimals = 1 };
        Assert.Equal("$123.5 USD", LiveValueFormatter.Render(new LiveReading(LiveReadingState.Ok, "123.456", 123.456), s).Text);
        Assert.Equal("$123.5 USD", LiveValueFormatter.Render(new LiveReading(LiveReadingState.Ok, "123,456"), s).Text);

        var raw = new LiveValueSettings { Decimals = -1 };
        Assert.Equal("12.3456", LiveValueFormatter.Render(new LiveReading(LiveReadingState.Ok, "12.3456"), raw).Text);
        Assert.Equal("$text USD", LiveValueFormatter.Render(new LiveReading(LiveReadingState.Ok, "text"), s).Text);
    }

    [Theory]
    [InlineData(WorseWhen.Higher, 70, 90, 50, LiveSeverity.Normal)]
    [InlineData(WorseWhen.Higher, 70, 90, 70, LiveSeverity.Warning)]
    [InlineData(WorseWhen.Higher, 70, 90, 95, LiveSeverity.Alert)]
    [InlineData(WorseWhen.Lower, 30, 10, 50, LiveSeverity.Normal)]
    [InlineData(WorseWhen.Lower, 30, 10, 30, LiveSeverity.Warning)]
    [InlineData(WorseWhen.Lower, 30, 10, 5, LiveSeverity.Alert)]
    public void Thresholds_FollowTheDirection(WorseWhen worse, double warn, double alert, double value, LiveSeverity expected)
    {
        var s = new LiveValueSettings { WarnAt = warn, AlertAt = alert, Worse = worse };
        Assert.Equal(expected, LiveValueFormatter.Evaluate(value, s));
    }

    [Fact]
    public void Thresholds_AreOptional_AndMaximumGivesTheRingPercentage()
    {
        Assert.Equal(LiveSeverity.Normal, LiveValueFormatter.Evaluate(1000, new LiveValueSettings()));
        Assert.Equal(LiveSeverity.Alert, LiveValueFormatter.Evaluate(100, new LiveValueSettings { AlertAt = 50 }));

        var s = new LiveValueSettings { Maximum = 200 };
        Assert.Equal(0.5, LiveValueFormatter.Render(new LiveReading(LiveReadingState.Ok, "100"), s).Percent);
        Assert.Equal(1.0, LiveValueFormatter.Render(new LiveReading(LiveReadingState.Ok, "500"), s).Percent); // clamped
    }

    [Fact]
    public void NonOkStates_ShowTheKeyWords()
    {
        var s = new LiveValueSettings();
        Assert.Equal("Set up", LiveValueFormatter.Render(LiveReading.SetUp, s).Text);
        Assert.Equal("…", LiveValueFormatter.Render(LiveReading.Loading, s).Text);
        Assert.Equal("–", LiveValueFormatter.Render(LiveReading.Error, s).Text);
        Assert.Equal("Allow?", LiveValueFormatter.Render(LiveReading.NeedsApproval, s).Text);
    }

    [Fact]
    public void SourceId_IgnoresFormatting_ButNotTheSource_AndNeverContainsASecret()
    {
        var a = new LiveValueSettings { Kind = LiveValueSourceKind.WebAddress, Url = "https://x/y", TokenName = "mytoken", Before = "$" };
        var b = new LiveValueSettings { Kind = LiveValueSourceKind.WebAddress, Url = "https://x/y", TokenName = "mytoken", After = "%", WarnAt = 3 };
        var c = new LiveValueSettings { Kind = LiveValueSourceKind.WebAddress, Url = "https://x/other", TokenName = "mytoken" };

        Assert.Equal(a.SourceId(), b.SourceId());   // one fetch serves both keys
        Assert.NotEqual(a.SourceId(), c.SourceId());
        Assert.StartsWith("live:", a.SourceId());
        Assert.DoesNotContain("mytoken", a.SourceId());
    }

    [Fact]
    public void RefreshInterval_HasAFloor_AndWeatherIsSlow()
    {
        Assert.Equal(5, new LiveValueSettings { RefreshSeconds = 1 }.EffectiveRefreshSeconds);
        Assert.Equal(600, new LiveValueSettings { Kind = LiveValueSourceKind.Weather, RefreshSeconds = 30 }.EffectiveRefreshSeconds);
    }

    // ── ICS ─────────────────────────────────────────────────────────────────────────────────

    private static string Ics(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("\r\n", events) + "\r\nEND:VCALENDAR\r\n";

    private static string Event(string uid, string start, string end, string summary, params string[] extra) =>
        $"BEGIN:VEVENT\r\nUID:{uid}\r\nSUMMARY:{summary}\r\nDTSTART:{start}\r\nDTEND:{end}\r\n" + string.Join("", extra.Select(e => e + "\r\n")) + "END:VEVENT";

    private static DateTimeOffset Utc(int y, int mo, int d, int h = 0, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    [Fact]
    public void Ics_SingleEvent_NextAndNow()
    {
        var ics = IcsParser.Parse(Ics(Event("a", "20261113T100000Z", "20261113T110000Z", "Standup")));
        Assert.Single(ics);

        var local = TimeZoneInfo.Local;
        // evaluate relative to the event's own instant, so the test does not depend on the machine's time zone
        var startAt = ics[0].StartAt;

        var before = IcsSchedule.Next(ics, startAt.AddMinutes(-25), TimeSpan.FromDays(1))!;
        Assert.Equal("Standup", before.Event.Summary);
        Assert.Equal("25 min", IcsSchedule.Describe(before.Start - startAt.AddMinutes(-25), before.IsRunning(startAt.AddMinutes(-25))));

        var during = IcsSchedule.Next(ics, startAt.AddMinutes(10), TimeSpan.FromDays(1))!;
        Assert.True(during.IsRunning(startAt.AddMinutes(10)));
        Assert.Equal("Now", IcsSchedule.Describe(TimeSpan.Zero, true));

        Assert.Null(IcsSchedule.Next(ics, startAt.AddHours(3), TimeSpan.FromDays(1))); // over
        Assert.NotNull(local);
    }

    [Fact]
    public void Ics_AllDayEvents_AreSkipped()
    {
        var ics = IcsParser.Parse(Ics(
            "BEGIN:VEVENT\r\nUID:d\r\nSUMMARY:Holiday\r\nDTSTART;VALUE=DATE:20261113\r\nDTEND;VALUE=DATE:20261114\r\nEND:VEVENT"));

        Assert.True(ics.Single().AllDay);
        Assert.Null(IcsSchedule.Next(ics, ics[0].StartAt.AddHours(1), TimeSpan.FromDays(2)));
    }

    [Fact]
    public void Ics_CancelledEvent_IsSkipped()
    {
        var ics = IcsParser.Parse(Ics(Event("c", "20261113T100000Z", "20261113T110000Z", "Dropped", "STATUS:CANCELLED")));
        Assert.Null(IcsSchedule.Next(ics, ics[0].StartAt.AddHours(-1), TimeSpan.FromDays(1)));
    }

    [Fact]
    public void Ics_DailyRepeat_FindsTheNextOccurrence_AndHonoursExdate()
    {
        var ics = IcsParser.Parse(Ics(Event("r", "20261101T100000Z", "20261101T103000Z", "Daily", "RRULE:FREQ=DAILY", "EXDATE:20261114T100000Z")));
        var master = ics.Single();
        var firstAt = master.StartAt;

        // on Nov 12 after the meeting: the next one is Nov 13 …
        var now = firstAt.AddDays(11).AddHours(1);
        var next = IcsSchedule.Next(ics, now, TimeSpan.FromDays(3))!;
        Assert.Equal(firstAt.AddDays(12), next.Start);

        // … and with Nov 14 excluded, after Nov 13's meeting the next is Nov 15
        var after13 = firstAt.AddDays(12).AddHours(1);
        Assert.Equal(firstAt.AddDays(14), IcsSchedule.Next(ics, after13, TimeSpan.FromDays(5))!.Start);
    }

    [Fact]
    public void Ics_WeeklyByDay_CountAndUntil()
    {
        // Mondays and Wednesdays, 3 occurrences in total
        var ics = IcsParser.Parse(Ics(Event("w", "20261102T090000Z", "20261102T100000Z", "Class", "RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=3")));
        var m = ics.Single();
        var starts = IcsSchedule.Occurrences(m, m.StartAt.AddDays(-1), m.StartAt.AddDays(60)).ToList();

        Assert.Equal(3, starts.Count);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Monday }, starts.Select(s => s.DayOfWeek));

        var until = IcsParser.Parse(Ics(Event("u", "20261102T090000Z", "20261102T100000Z", "Until", "RRULE:FREQ=DAILY;UNTIL=20261104T235959Z")));
        Assert.Equal(3, IcsSchedule.Occurrences(until[0], until[0].StartAt.AddDays(-1), until[0].StartAt.AddDays(30)).Count());
    }

    [Fact]
    public void Ics_MovedAndCancelledOccurrences_UseTheOverrides()
    {
        string master = Event("m", "20261101T100000Z", "20261101T110000Z", "Weekly sync", "RRULE:FREQ=WEEKLY");
        string moved = Event("m", "20261109T140000Z", "20261109T150000Z", "Weekly sync (moved)", "RECURRENCE-ID:20261108T100000Z");
        string cancelled = Event("m", "20261115T100000Z", "20261115T110000Z", "Weekly sync", "RECURRENCE-ID:20261115T100000Z", "STATUS:CANCELLED");
        var ics = IcsParser.Parse(Ics(master, moved, cancelled));

        var first = ics.First(e => e.RecurrenceId == null);
        var now = first.StartAt.AddDays(6); // Nov 7: the Nov 8 occurrence is next, but it was moved to Nov 9 14:00
        var next = IcsSchedule.Next(ics, now, TimeSpan.FromDays(7))!;
        Assert.Equal("Weekly sync (moved)", next.Event.Summary);

        // the Nov 15 occurrence is cancelled: from Nov 10 the next one is Nov 22
        var later = IcsSchedule.Next(ics, first.StartAt.AddDays(9), TimeSpan.FromDays(14))!;
        Assert.Equal(first.StartAt.AddDays(21), later.Start);
    }

    [Fact]
    public void Ics_FoldedLinesAndDuration_AreRead()
    {
        string text = "BEGIN:VEVENT\r\nUID:f\r\nSUMMARY:A very long\r\n  title\r\nDTSTART:20261113T100000Z\r\nDURATION:PT1H30M\r\nEND:VEVENT\r\n";
        var e = IcsParser.Parse(text).Single();

        Assert.Equal("A very long title", e.Summary);
        Assert.Equal(TimeSpan.FromMinutes(90), e.Duration);
    }

    [Theory]
    [InlineData(0, true, "Now")]
    [InlineData(30, false, "1 min")]
    [InlineData(12 * 60, false, "12 min")]
    [InlineData(2 * 3600 + 5 * 60, false, "2 h 5 min")]
    [InlineData(3 * 3600, false, "3 h")]
    [InlineData(3 * 86400, false, "3 d")]
    public void Describe_IsCompact(int seconds, bool running, string expected) =>
        Assert.Equal(expected, IcsSchedule.Describe(TimeSpan.FromSeconds(seconds), running).Replace("<1 min", "1 min"));

    // ── Open-Meteo ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OpenMeteo_MapsTheForecast_AndParsesPlaces()
    {
        var f = OpenMeteo.ParseForecast("""{"current":{"temperature_2m":17.6,"weather_code":61}}""")!.Value;
        Assert.Equal(18, f.Temperature);
        Assert.Equal("Rain", f.Word);
        Assert.Null(OpenMeteo.ParseForecast("{}"));

        Assert.Equal("Clear", OpenMeteo.WordFor(0));
        Assert.Equal("Storm", OpenMeteo.WordFor(95));
        Assert.Equal("", OpenMeteo.WordFor(1234));

        var place = OpenMeteo.ParseGeocoding("""{"results":[{"name":"Warsaw","latitude":52.2298,"longitude":21.0118}]}""")!;
        Assert.Equal("Warsaw|52.2298|21.0118", place.Format());
        Assert.Equal(place, WeatherPlace.Parse(place.Format()));
        Assert.Null(WeatherPlace.Parse("Warsaw"));
        Assert.Null(WeatherPlace.Parse("X|999|10"));
        Assert.Null(OpenMeteo.ParseGeocoding("""{"results":[]}"""));
    }

    // ── command approval & token store ─────────────────────────────────────────────────────

    [Fact]
    public void CommandApproval_IsPerExactCommandLine_AndCanBeRevoked()
    {
        var store = new LiveCommandApprovalStore(Path.Combine(_dir, "live-commands.json"));
        Assert.False(store.IsApproved("echo 1"));

        store.Approve("echo 1");
        Assert.True(store.IsApproved("echo 1"));
        Assert.False(store.IsApproved("echo 1 "));          // one more character = a new command
        Assert.False(store.IsApproved("ECHO 1"));

        // a second instance (a restart) still knows it; deleting the line revokes it
        Assert.True(new LiveCommandApprovalStore(Path.Combine(_dir, "live-commands.json")).IsApproved("echo 1"));
        store.Revoke("echo 1");
        Assert.False(store.IsApproved("echo 1"));
    }

    private sealed class Plain : ISecretProtectionService
    {
        public bool IsProtected(string? value) => value?.StartsWith("enc:v1:") == true;
        public string? Protect(string? plainText) => string.IsNullOrEmpty(plainText) || IsProtected(plainText) ? plainText : "enc:v1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        public string? Unprotect(string? cipherText) => IsProtected(cipherText) ? Encoding.UTF8.GetString(Convert.FromBase64String(cipherText!["enc:v1:".Length..])) : cipherText;
    }

    [Fact]
    public void Tokens_AreStoredProtected_NotInThePlainFile()
    {
        string path = Path.Combine(_dir, "live-tokens.json");
        var store = new LiveTokenStore(new Plain(), path);

        store.Set("crypto", "s3cr3t-token");

        Assert.Equal("s3cr3t-token", store.Get("crypto"));
        Assert.DoesNotContain("s3cr3t-token", File.ReadAllText(path));
        Assert.Equal(new[] { "crypto" }, store.Names());

        store.Set("crypto", "");
        Assert.Null(store.Get("crypto"));
    }

    // ── the service ─────────────────────────────────────────────────────────────────────────

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<(string Url, string? Auth)> Requests { get; } = new();
        public Func<string, string> Body { get; set; } = _ => "";
        public int Status { get; set; } = 200;
        public Func<byte[]>? Large { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString()));
            var content = Large != null ? new ByteArrayContent(Large()) : new StringContent(Body(request.RequestUri!.ToString()), Encoding.UTF8);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)Status) { Content = content });
        }
    }

    private (LiveValueService Service, FakeHandler Http, Func<DateTimeOffset> Now, Action<TimeSpan> Advance) Create(FakeHandler? http = null, LiveTokenStore? tokens = null,
        Func<string, string?>? ha = null, Func<string, CancellationToken, Task<string?>>? command = null)
    {
        http ??= new FakeHandler();
        var clock = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        var service = new LiveValueService(tokens, new LiveCommandApprovalStore(Path.Combine(_dir, "cmds.json")), ha, http, () => clock, command);
        return (service, http, () => clock, d => clock += d);
    }

    private static LiveValueSettings Web(string url = "https://api.test/price", string path = "data.price", int refresh = 60) =>
        new() { Kind = LiveValueSourceKind.WebAddress, Url = url, JsonPath = path, RefreshSeconds = refresh };

    [Fact]
    public async Task WebSource_IsFetchedOnce_AndSharedByKeysWithTheSameSource()
    {
        var (service, http, _, _) = Create();
        http.Body = _ => """{"data":{"price":42.5}}""";
        var keyA = Web();
        var keyB = Web(); keyB.Before = "$"; keyB.WarnAt = 40; // same source, different look

        service.SetDemand(new[] { keyA, keyB });
        await service.TickAsync();

        Assert.Single(http.Requests);
        Assert.Equal("42.5", service.GetReading(keyA).Raw);
        Assert.Equal(service.GetReading(keyA), service.GetReading(keyB));
        Assert.Equal(LiveSeverity.Warning, LiveValueFormatter.Render(service.GetReading(keyB), keyB).Severity);
    }

    [Fact]
    public async Task OnlyKeysOnScreen_AreFetched_NothingRunsWithoutDemand_OrInStandby()
    {
        var (service, http, _, _) = Create();
        service.SetDemand(Array.Empty<LiveValueSettings>());
        Assert.False(service.IsPolling);                      // no timer at all
        await service.TickAsync();
        Assert.Empty(http.Requests);

        var visible = Web("https://api.test/visible");
        var hidden = Web("https://api.test/hidden");
        service.SetDemand(new[] { visible });
        Assert.True(service.IsPolling);
        await service.TickAsync();
        Assert.Equal(new[] { "https://api.test/visible" }, http.Requests.Select(r => r.Url));
        Assert.Equal(LiveReadingState.Loading, service.GetReading(hidden).State);

        service.Paused = true;                                // the deck sleeps
        Assert.False(service.IsPolling);
        http.Requests.Clear();
        await service.TickAsync();
        Assert.Empty(http.Requests);

        service.Paused = false;
        Assert.True(service.IsPolling);

        service.SetDemand(Array.Empty<LiveValueSettings>());  // the page was left
        Assert.False(service.IsPolling);
    }

    [Fact]
    public async Task Interval_IsRespected_AndPressRefreshesAtOnce()
    {
        var (service, http, _, advance) = Create();
        http.Body = _ => "7";
        var key = Web(path: "", refresh: 60);
        service.SetDemand(new[] { key });

        await service.TickAsync();
        await service.TickAsync();                    // not due yet
        Assert.Single(http.Requests);

        advance(TimeSpan.FromSeconds(61));
        await service.TickAsync();
        Assert.Equal(2, http.Requests.Count);

        await service.RefreshNowAsync(key);           // a press does not wait
        Assert.Equal(3, http.Requests.Count);
    }

    [Fact]
    public async Task Failures_ShowTheDash_ThenRecover()
    {
        var (service, http, _, advance) = Create();
        http.Status = 500;
        var key = Web();
        service.SetDemand(new[] { key });

        await service.TickAsync();
        Assert.Equal(LiveReadingState.Error, service.GetReading(key).State);

        http.Status = 200;
        http.Body = _ => """{"data":{"price":1}}""";
        advance(TimeSpan.FromSeconds(31));            // errors are retried sooner (≤ 30 s)
        await service.TickAsync();
        Assert.Equal(LiveReadingState.Ok, service.GetReading(key).State);
    }

    [Fact]
    public async Task OversizeResponse_AndNonHttpAddresses_AreRefused()
    {
        var (service, http, _, _) = Create();
        http.Large = () => new byte[LiveValueService.MaxResponseBytes + 1];
        var big = Web(path: "");
        var file = Web("file:///C:/secret.txt", path: "");
        service.SetDemand(new[] { big, file });

        await service.TickAsync();

        Assert.Equal(LiveReadingState.Error, service.GetReading(big).State);
        Assert.Equal(LiveReadingState.Error, service.GetReading(file).State);
        Assert.DoesNotContain(http.Requests, r => r.Url.StartsWith("file:"));
    }

    [Fact]
    public async Task BearerToken_IsSentFromTheStore()
    {
        var tokens = new LiveTokenStore(new Plain(), Path.Combine(_dir, "t.json"));
        tokens.Set("api", "abc123");
        var (service, http, _, _) = Create(tokens: tokens);
        http.Body = _ => """{"data":{"price":1}}""";
        var key = Web(); key.TokenName = "api";

        service.SetDemand(new[] { key });
        await service.TickAsync();

        Assert.Equal("Bearer abc123", http.Requests.Single().Auth);
    }

    [Fact]
    public async Task Command_NeedsApproval_ThenRunsOnlyTheApprovedLine()
    {
        var ran = new List<string>();
        var (service, _, _, advance) = Create(command: (c, _) => { ran.Add(c); return Task.FromResult<string?>("42"); });
        var key = new LiveValueSettings { Kind = LiveValueSourceKind.Command, Command = "echo 42", RefreshSeconds = 5 };
        service.SetDemand(new[] { key });

        await service.TickAsync();
        Assert.Equal(LiveReadingState.NeedsApproval, service.GetReading(key).State);
        Assert.Empty(ran);

        new LiveCommandApprovalStore(Path.Combine(_dir, "cmds.json")).Approve("echo 42");
        advance(TimeSpan.FromSeconds(31));
        await service.TickAsync();

        Assert.Equal(new[] { "echo 42" }, ran);
        Assert.Equal("42", service.GetReading(key).Raw);
    }

    [Fact]
    public async Task HomeAssistant_ReadsTheEntityState()
    {
        var (service, _, _, _) = Create(ha: id => id == "sensor.temp" ? "21.5" : null);
        var key = new LiveValueSettings { Kind = LiveValueSourceKind.HomeAssistant, EntityId = "sensor.temp" };
        var missing = new LiveValueSettings { Kind = LiveValueSourceKind.HomeAssistant, EntityId = "sensor.nope" };

        service.SetDemand(new[] { key, missing });
        await service.TickAsync();

        Assert.Equal(21.5, service.GetReading(key).Numeric);
        Assert.Equal(LiveReadingState.Error, service.GetReading(missing).State);
    }

    [Fact]
    public async Task Weather_ResolvesACityOnce_ThenShowsTemperatureAndWord()
    {
        var (service, http, _, _) = Create();
        http.Body = url => url.Contains("geocoding")
            ? """{"results":[{"name":"Warsaw","latitude":52.23,"longitude":21.01}]}"""
            : """{"current":{"temperature_2m":4.4,"weather_code":71}}""";
        var key = new LiveValueSettings { Kind = LiveValueSourceKind.Weather, City = "Warsaw" };

        service.SetDemand(new[] { key });
        await service.TickAsync();

        var reading = service.GetReading(key);
        Assert.Equal("4°", reading.Raw);
        Assert.Equal("Snow", reading.Caption);
        Assert.Equal(2, http.Requests.Count);
        Assert.Contains("latitude=52.23", http.Requests[1].Url);
    }

    [Fact]
    public async Task Calendar_IsDownloadedEvery5Minutes_ButReEvaluatedInBetween()
    {
        var (service, http, now, advance) = Create();
        // an event 20 minutes after the fake "now" (12:00 UTC)
        http.Body = _ => Ics(Event("e", "20261113T122000Z", "20261113T130000Z", "Review"));
        var key = new LiveValueSettings { Kind = LiveValueSourceKind.CalendarNext, Url = "webcal://cal.test/me.ics", RefreshSeconds = 60 };

        service.SetDemand(new[] { key });
        await service.TickAsync();
        Assert.Equal("https://cal.test/me.ics", http.Requests.Single().Url);       // webcal:// becomes https://
        Assert.Equal("20 min", service.GetReading(key).Raw);
        Assert.Equal("Review", service.GetReading(key).Caption);

        advance(TimeSpan.FromMinutes(2));
        await service.TickAsync();
        Assert.Single(http.Requests);                                              // not downloaded again
        Assert.Equal("18 min", service.GetReading(key).Raw);                       // but re-evaluated

        advance(TimeSpan.FromMinutes(4));
        await service.TickAsync();
        Assert.Equal(2, http.Requests.Count);                                      // 6 min after the download: fetched again
        Assert.NotNull(now);
    }

    [Fact]
    public async Task Unconfigured_ShowsSetUp_WithoutAnyRequest()
    {
        var (service, http, _, _) = Create();
        var key = new LiveValueSettings { Kind = LiveValueSourceKind.WebAddress };

        service.SetDemand(new[] { key });
        await service.TickAsync();

        Assert.Equal(LiveReadingState.SetUp, service.GetReading(key).State);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task ReadingChanged_IsRaisedOnlyWhenTheValueChanged()
    {
        var (service, http, _, advance) = Create();
        string value = "1";
        http.Body = _ => value;
        var key = Web(path: "", refresh: 5);
        int changes = 0;
        service.ReadingChanged += _ => changes++;
        service.SetDemand(new[] { key });

        await service.TickAsync();
        advance(TimeSpan.FromSeconds(6));
        await service.TickAsync();           // same value
        value = "2";
        advance(TimeSpan.FromSeconds(6));
        await service.TickAsync();           // new value

        Assert.Equal(2, changes);
    }
}
