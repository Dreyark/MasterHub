using MasterHub.Core.Profiles;

namespace MasterHub.Core.Tests;

/// <summary>T-01: rule precedence, return to the last picked profile, toggle, and "no hook without rules".</summary>
public class SmartProfileTests
{
    private static SmartProfileRule Rule(string process, string profile, string? title = null, bool enabled = true) =>
        new() { ProcessName = process, ProfileId = profile, WindowTitle = title, IsEnabled = enabled };

    private static (SmartProfileService Service, List<string> Switches) Create(params SmartProfileRule[] rules)
    {
        var service = new SmartProfileService();
        var switches = new List<string>();
        service.AutoSwitchProfileRequested += switches.Add;
        service.UpdateRules(rules);
        return (service, switches);
    }

    // ── precedence ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TitleSpecificRule_BeatsProcessOnlyRule_RegardlessOfOrder()
    {
        var rules = new[]
        {
            Rule("chrome", "browser"),
            Rule("chrome", "youtube", title: "YouTube")
        };

        Assert.Equal("youtube", SmartProfileService.FindMatchingRule(rules, "chrome", "Cats - YouTube")!.ProfileId);
        Assert.Equal("browser", SmartProfileService.FindMatchingRule(rules, "chrome", "Inbox")!.ProfileId);
        Assert.Null(SmartProfileService.FindMatchingRule(rules, "notepad", "x"));
    }

    [Fact]
    public void SameSpecificity_FirstRuleWins_DisabledRulesAreIgnored()
    {
        var rules = new[]
        {
            Rule("obs64", "off", enabled: false),
            Rule("obs64", "first"),
            Rule("obs64", "second")
        };

        Assert.Equal("first", SmartProfileService.FindMatchingRule(rules, "obs64", "OBS")!.ProfileId);
    }

    // ── switching ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Matching_SwitchesOnce_ThenReturnsToTheLastPickedProfile()
    {
        var (service, switches) = Create(Rule("obs64", "obs-profile"));
        service.FallbackProfileId = "picked-by-user"; // the app keeps this at the last manual choice, not at Home

        service.ApplyForeground("obs64", "OBS Studio");
        service.ApplyForeground("obs64", "OBS Studio - Settings"); // same app, new title: no second switch
        service.ApplyForeground("notepad", "Untitled");

        Assert.Equal(new[] { "obs-profile", "picked-by-user" }, switches);
    }

    [Fact]
    public void NoReturn_WhenNothingWasSwitched_OrNoFallbackIsSet()
    {
        var (service, switches) = Create(Rule("obs64", "obs-profile"));

        service.ApplyForeground("notepad", "Untitled"); // never matched: nothing to return from
        service.ApplyForeground("obs64", "OBS");
        service.ApplyForeground("notepad", "Untitled"); // matched before, but there is no fallback

        Assert.Equal(new[] { "obs-profile" }, switches);
    }

    [Fact]
    public void SwitchingBetweenTwoAppProfiles_GoesDirectly()
    {
        var (service, switches) = Create(Rule("obs64", "obs-profile"), Rule("spotify", "music"));
        service.FallbackProfileId = "home";

        service.ApplyForeground("obs64", "OBS");
        service.ApplyForeground("spotify", "Spotify");

        Assert.Equal(new[] { "obs-profile", "music" }, switches);
    }

    [Fact]
    public void ToggleOff_StopsSwitching_AndRaisesTheChangeEvent()
    {
        var (service, switches) = Create(Rule("obs64", "obs-profile"));
        var toggles = new List<bool>();
        service.IsEnabledChanged += toggles.Add;

        service.IsEnabled = false;
        service.ApplyForeground("obs64", "OBS");
        Assert.Empty(switches);

        service.IsEnabled = true;
        service.IsEnabled = true; // no change, no event
        service.ApplyForeground("obs64", "OBS");

        Assert.Equal(new[] { "obs-profile" }, switches);
        Assert.Equal(new[] { false, true }, toggles);
    }

    [Fact]
    public void ForegroundChanged_IsReportedOnlyWhenTheWindowChanges()
    {
        var (service, _) = Create(Rule("obs64", "p"));
        var seen = new List<string>();
        service.ForegroundWindowChanged += (p, t) => seen.Add($"{p}|{t}");

        service.ApplyForeground("obs64", "A");
        service.ApplyForeground("obs64", "A");
        service.ApplyForeground("obs64", "B");

        Assert.Equal(new[] { "obs64|A", "obs64|B" }, seen);
        Assert.Equal("obs64", service.LastForegroundProcess);
        Assert.Equal("B", service.LastForegroundTitle);
    }

    [Fact]
    public async Task ConcurrentEvaluations_NeverSwitchTwice()
    {
        var (service, switches) = Create(Rule("obs64", "p"));
        var gate = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            gate.Wait();
            service.ApplyForeground("obs64", "OBS");
        })).ToArray();
        gate.Set();
        await Task.WhenAll(tasks);

        Assert.Single(switches);
    }

    [Fact]
    public void UpdatingRules_ResetsTheMatchedState()
    {
        var (service, switches) = Create(Rule("obs64", "p"));
        service.ApplyForeground("obs64", "OBS");

        service.UpdateRules(new[] { Rule("obs64", "p") });
        service.ApplyForeground("notepad", "x");
        service.ApplyForeground("obs64", "OBS");

        Assert.Equal(new[] { "p", "p" }, switches);
    }

    // ── zero idle cost ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoHookRuns_WithoutEnabledRules_OrWhileToggledOff()
    {
        using var service = new SmartProfileService();
        Assert.False(service.IsWatching);

        service.UpdateRules(new[] { Rule("obs64", "p", enabled: false) });
        Assert.False(service.IsWatching);

        service.UpdateRules(new[] { Rule("obs64", "p") });
        Assert.True(service.IsWatching);

        service.IsEnabled = false;
        Assert.False(service.IsWatching);

        service.IsEnabled = true;
        Assert.True(service.IsWatching);

        service.Stop();
        Assert.False(service.IsWatching);
    }

    [Fact]
    public void WindowTitleRules_ReconfigureTheWatcher_WithoutLeakingThreads()
    {
        using var service = new SmartProfileService();

        for (int i = 0; i < 5; i++)
        {
            service.UpdateRules(new[] { Rule("chrome", "p", title: i % 2 == 0 ? "YouTube" : null) });
            Assert.True(service.IsWatching);
        }

        service.UpdateRules(Array.Empty<SmartProfileRule>());
        Assert.False(service.IsWatching);
    }
}
