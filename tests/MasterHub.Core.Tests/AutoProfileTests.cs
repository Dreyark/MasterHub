using MasterHub.Core.Actions;
using MasterHub.Core.Apps;
using MasterHub.Core.Audio;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Auto;
using MasterHub.Core.Profiles.Templates;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;

namespace MasterHub.Core.Tests;

/// <summary>T-37: profiles that prepare themselves (switched off, offered once), the suggestions, and the module sketches.</summary>
public sealed class AutoProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_auto_" + Guid.NewGuid().ToString("N"));
    private readonly ProfileService _profiles;
    private readonly ProfileTemplateService _templates;
    private readonly AssistSettingsStore _store;

    public AutoProfileTests()
    {
        _profiles = new ProfileService(_dir, new SecretProtectionService());
        _templates = new ProfileTemplateService(_profiles);
        _store = new AssistSettingsStore(_profiles);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class FakeInstalled : IInstalledAppsService
    {
        public IReadOnlyList<InstalledApp> Apps { get; set; } = Array.Empty<InstalledApp>();
        public bool IsScanning => false;
        public DateTime? LastScanUtc => null;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public event Action? Changed { add { } remove { } }
    }

    private static InstalledApp Program(string name, string exe) =>
        new("start:" + exe, name, InstalledAppKind.StartMenu, @"C:\apps\" + exe + ".lnk", @"C:\apps\" + exe + ".exe");

    private static InstalledApp Game(string name, string exe) =>
        new("steam:" + exe, name, InstalledAppKind.Steam, "steam://rungameid/1", $@"C:\games\{exe}.exe");

    private AutoProfileService Auto(params InstalledApp[] apps) =>
        new(_profiles, _templates, new FakeInstalled { Apps = apps }, _store, () => "en");

    private void TurnOn() => _store.Update(s => { s.Asked = true; s.Enabled = true; });

    private List<ProfileModel> Prepared() => _profiles.GetAllProfiles().Where(p => p.AutoMade).ToList();

    // ── prepared profiles ───────────────────────────────────────────────────────────────────

    [Fact]
    public void WithTheAssistantOff_NothingIsMade()
    {
        var auto = Auto(Program("OBS Studio", "obs64"), Game("Quest", "quest"));

        Assert.Equal(0, auto.PrepareForInstalledApps());
        Assert.False(auto.OnAppInFront("quest"));
        Assert.Empty(Prepared());
        Assert.Empty(_store.Read().Offered);
    }

    [Fact]
    public void InstalledProgramsWithAReadyMadeProfile_GetOne_SwitchedOff()
    {
        TurnOn();
        var active = _profiles.GetActiveProfile().Id;
        var auto = Auto(Program("OBS Studio", "obs64"), Program("Discord", "Discord"), Program("Notepad", "notepad"));

        int made = auto.PrepareForInstalledApps();

        Assert.Equal(2, made);   // OBS and Discord/Teams; Notepad has no ready-made profile
        var prepared = Prepared();
        Assert.Equal(2, prepared.Count);
        Assert.Contains(prepared, p => p.Name == "OBS streaming");
        Assert.All(prepared, p =>
        {
            Assert.NotEmpty(p.SmartRules);
            Assert.All(p.SmartRules, r => Assert.False(r.IsEnabled));   // arrives switched off
            Assert.True(AutoProfileService.IsReady(p));
        });
        Assert.Equal(active, _profiles.GetActiveProfile().Id);   // nothing took over the deck
    }

    [Fact]
    public void OnlyTheInstalledProgramsOfATemplate_AreLinked()
    {
        TurnOn();
        Auto(Program("Discord", "Discord")).PrepareForInstalledApps();

        var profile = Assert.Single(Prepared());
        Assert.Equal(new[] { "discord" }, profile.SmartRules.Select(r => r.ProcessName).ToArray());   // not Teams, which is not here
    }

    [Fact]
    public void ANotYetInstalledProgram_IsNotRecordedAsOffered_SoItIsLookedAtAgain()
    {
        TurnOn();
        var installed = new FakeInstalled { Apps = new[] { Program("Notepad", "notepad") } };
        var auto = new AutoProfileService(_profiles, _templates, installed, _store, () => "en");

        Assert.Equal(0, auto.PrepareForInstalledApps());
        Assert.Empty(_store.Read().Offered);

        installed.Apps = new[] { Program("OBS Studio", "obs64") };
        Assert.Equal(1, auto.PrepareForInstalledApps());
    }

    [Fact]
    public void AProfileTheUserDeleted_IsNeverMadeAgain()
    {
        TurnOn();
        var auto = Auto(Program("OBS Studio", "obs64"));
        auto.PrepareForInstalledApps();
        var made = Assert.Single(Prepared());

        _profiles.DeleteProfile(made.Id);

        Assert.Equal(0, auto.PrepareForInstalledApps());
        Assert.Equal(0, Auto(Program("OBS Studio", "obs64")).PrepareForInstalledApps());   // not after a restart either
        Assert.Empty(Prepared());
    }

    [Fact]
    public void PreparingTwice_MakesOneProfile()
    {
        TurnOn();
        var auto = Auto(Program("OBS Studio", "obs64"));

        Assert.Equal(1, auto.PrepareForInstalledApps());
        Assert.Equal(0, auto.PrepareForInstalledApps());
        Assert.Single(Prepared());
    }

    [Fact]
    public void ANewlyMadeProfile_NeverTakesPartInAutoSwitching_UntilItIsSwitchedOn()
    {
        TurnOn();
        var auto = Auto(Program("OBS Studio", "obs64"));
        auto.PrepareForInstalledApps();
        var rules = _profiles.GetAllProfiles().SelectMany(p => p.SmartRules).ToList();

        Assert.Null(SmartProfileService.FindMatchingRule(rules, "obs64", "OBS 30"));

        var made = Assert.Single(Prepared());
        Assert.True(auto.SwitchOn(made.Id));

        rules = _profiles.GetAllProfiles().SelectMany(p => p.SmartRules).ToList();
        var match = SmartProfileService.FindMatchingRule(rules, "obs64", "OBS 30");
        Assert.NotNull(match);
        Assert.Equal(made.Id, match!.ProfileId);
    }

    // ── switching on, the "new" mark ────────────────────────────────────────────────────────

    [Fact]
    public void SwitchOn_TurnsTheRulesOn_ClearsTheFlag_AndTheNewMark()
    {
        TurnOn();
        var auto = Auto(Program("OBS Studio", "obs64"));
        auto.PrepareForInstalledApps();
        var made = Assert.Single(Prepared());
        Assert.Contains(made.Id, auto.UnseenProfileIds());

        Assert.True(auto.SwitchOn(made.Id));

        var now = _profiles.GetAllProfiles().Single(p => p.Id == made.Id);
        Assert.False(now.AutoMade);
        Assert.All(now.SmartRules, r => Assert.True(r.IsEnabled));
        Assert.False(AutoProfileService.IsReady(now));
        Assert.Empty(auto.UnseenProfileIds());
        Assert.False(auto.SwitchOn(made.Id));   // already on: nothing to do
    }

    [Fact]
    public void ANewProfile_HasTheNewMark_UntilItWasSeen_AndAProfileThatIsGoneIsNotNew()
    {
        TurnOn();
        var auto = Auto(Program("OBS Studio", "obs64"), Program("Discord", "Discord"));
        auto.PrepareForInstalledApps();
        var both = Prepared();
        Assert.Equal(2, auto.UnseenProfileIds().Count);

        _profiles.DeleteProfile(both[0].Id);
        Assert.Equal(new[] { both[1].Id }, auto.UnseenProfileIds().ToArray());

        auto.MarkSeen();
        Assert.Empty(auto.UnseenProfileIds());
    }

    // ── games ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AGame_GetsAProfile_TheFirstTimeItComesToTheFront_Once()
    {
        TurnOn();
        var auto = Auto(Game("Quest of Tests", "quest"));

        Assert.False(auto.OnAppInFront("notepad"));   // not a game
        Assert.True(auto.OnAppInFront("Quest"));      // case does not matter
        Assert.False(auto.OnAppInFront("quest"));     // only once

        var made = Assert.Single(Prepared());
        Assert.Equal("Quest of Tests", made.Name);
        Assert.Equal("quest", Assert.Single(made.SmartRules).ProcessName);
        Assert.False(made.SmartRules[0].IsEnabled);
        Assert.Contains(made.Keys, k => k.HotkeySendMode == HotkeySendMode.ForGames);   // the game action bar
    }

    [Fact]
    public void AGameProfileTheUserDeleted_IsNotMadeAgain()
    {
        TurnOn();
        var auto = Auto(Game("Quest of Tests", "quest"));
        auto.OnAppInFront("quest");
        _profiles.DeleteProfile(Assert.Single(Prepared()).Id);

        Assert.False(auto.OnAppInFront("quest"));
        Assert.Empty(Prepared());
    }

    [Fact]
    public void AGame_ThatAlreadyHasAProfileMadeByHand_IsLeftAlone()
    {
        TurnOn();
        var mine = _profiles.CreateProfile("My quest", setAsActive: false);
        mine.SmartRules.Add(new SmartProfileRule { ProfileId = mine.Id, ProcessName = "quest.exe" });
        _profiles.SaveProfile(mine);

        Assert.False(Auto(Game("Quest of Tests", "quest")).OnAppInFront("quest"));
        Assert.Empty(Prepared());
    }

    // ── sketches for one program ────────────────────────────────────────────────────────────

    [Fact]
    public void ASketch_UsesTheProgramsReadyMadeProfile_OrTheEverydayKeys()
    {
        var auto = Auto();

        var obs = auto.BuildSketch("OBS64", "OBS Studio");
        Assert.NotNull(obs);
        Assert.Equal("OBS Studio", obs!.Profile.Name);
        Assert.Equal("obs64", obs.Process);
        Assert.Contains(obs.Profile.Keys, k => k.Action == DeckActionType.ObsToggleStream);

        var other = auto.BuildSketch("notepad", "Notepad");
        Assert.NotNull(other);
        Assert.Equal("Notepad", other!.Profile.Name);
        Assert.Contains(other.Profile.Keys, k => k.Action != DeckActionType.None);

        Assert.Null(auto.BuildSketch("", "Nothing"));
        Assert.Empty(Prepared());   // a sketch is not saved
    }

    // ── old files ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OldSettings_WithoutTheAssistant_LoadWithEverythingOff()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ \"LanguageCode\": \"pl\" }");

        var settings = new AssistSettingsStore(new ProfileService(_dir, new SecretProtectionService())).Read();

        Assert.False(settings.Enabled);
        Assert.False(settings.Asked);
        Assert.False(settings.PressCounting);
        Assert.Empty(settings.Offered);
        Assert.Null(settings.SeenModules);
    }

    [Fact]
    public void TheSettings_AreKeptInTheSettingsFile_AndReadBack()
    {
        _store.Update(s => { s.Enabled = true; s.Asked = true; s.Offered.Add("template:x"); });

        var again = new AssistSettingsStore(new ProfileService(_dir, new SecretProtectionService())).Read();

        Assert.True(again.Enabled);
        Assert.Contains("template:x", again.Offered);
    }
}

/// <summary>T-37: the suggestions. Without any AI: a few plain rules, one at a time, each only once.</summary>
public sealed class NudgeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_nudge_" + Guid.NewGuid().ToString("N"));
    private readonly ProfileService _profiles;
    private readonly AssistSettingsStore _store;
    private readonly FakeUsage _usage = new();
    private readonly FakePresses _presses = new();
    private DateTime _now = new(2026, 11, 14, 12, 0, 0);

    public NudgeTests()
    {
        _profiles = new ProfileService(_dir, new SecretProtectionService());
        _store = new AssistSettingsStore(_profiles);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class FakeUsage : IAppUsageService
    {
        public Dictionary<string, AppUsageEntry> Data { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool IsRunning => true;
        public DateTime Since => DateTime.MinValue;
        public event Action<string>? AppInFront { add { } remove { } }
        public IReadOnlyDictionary<string, AppUsageEntry> Snapshot() => Data;
        public void Start() { }
        public void Stop() { }
        public void Clear() => Data.Clear();
        public void Dispose() { }
        public void Add(string app, int minutes, int days) => Data[app] = new AppUsageEntry { Seconds = minutes * 60L, Days = days };
    }

    private sealed class FakePresses : IPressCounter
    {
        public bool IsActive { get; set; } = true;
        public DateTime? Since { get; set; }
        public Dictionary<string, long> Counts { get; } = new();
        public void Count(string profileId, string controlId) { }
        public IReadOnlyDictionary<string, long> Get(string profileId) => Counts;
        public void Flush() { }
        public void Clear() => Counts.Clear();
    }

    private NudgeService Create() => new(_profiles, _store, _usage, _presses, null, () => _now);

    private void TurnOn(bool pressCounting = false) => _store.Update(s => { s.Asked = true; s.Enabled = true; s.PressCounting = pressCounting; });

    // ── a program the user works in a lot ───────────────────────────────────────────────────

    [Fact]
    public void WithTheAssistantOff_ThereIsNoSuggestion()
    {
        _usage.Add("photoshop", 600, 20);
        var nudges = Create();

        nudges.Evaluate();

        Assert.Null(nudges.Current);
    }

    [Fact]
    public void AProgramWorkedInALot_WithoutAProfile_IsSuggested()
    {
        TurnOn();
        _usage.Add("photoshop", 120, 5);
        var nudges = Create();

        nudges.Evaluate();

        Assert.NotNull(nudges.Current);
        Assert.Equal(NudgeKind.AppWithoutProfile, nudges.Current!.Kind);
        Assert.Equal("photoshop", nudges.Current.Subject);
    }

    [Theory]
    [InlineData(29, 5)]    // too short
    [InlineData(120, 2)]   // too few days: one long afternoon is not "working in it"
    public void ALittleUse_IsNotEnough(int minutes, int days)
    {
        TurnOn();
        _usage.Add("photoshop", minutes, days);
        var nudges = Create();

        nudges.Evaluate();

        Assert.Null(nudges.Current);
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("MasterHub")]
    [InlineData("ShellExperienceHost")]
    public void WindowsOwnProgramsAndMasterHub_AreNeverSuggested(string process)
    {
        TurnOn();
        _usage.Add(process, 600, 30);
        var nudges = Create();

        nudges.Evaluate();

        Assert.Null(nudges.Current);
    }

    [Fact]
    public void AProgramThatAlreadyHasAProfile_EvenOneThatIsSwitchedOff_IsNotSuggested()
    {
        TurnOn();
        var p = _profiles.CreateProfile("Photo", setAsActive: false);
        p.SmartRules.Add(new SmartProfileRule { ProfileId = p.Id, ProcessName = "Photoshop.exe", IsEnabled = false });
        _profiles.SaveProfile(p);
        _usage.Add("photoshop", 600, 20);
        var nudges = Create();

        nudges.Evaluate();

        Assert.Null(nudges.Current);
    }

    [Fact]
    public void ASuggestion_ThatWasAnswered_NeverComesBack_EvenAfterARestart()
    {
        TurnOn();
        _usage.Add("photoshop", 600, 20);
        var nudges = Create();
        nudges.Evaluate();
        var nudge = nudges.Current!;

        nudges.Answer(nudge);

        Assert.Null(nudges.Current);
        nudges.Evaluate();
        Assert.Null(nudges.Current);

        var restarted = Create();
        restarted.Evaluate();
        Assert.Null(restarted.Current);
    }

    [Fact]
    public void ASuggestion_FiresOnce_AndOnlyOneIsShownAtATime()
    {
        TurnOn();
        _usage.Add("photoshop", 600, 20);
        _usage.Add("blender", 300, 10);
        var nudges = Create();
        int changes = 0;
        nudges.Changed += () => changes++;

        nudges.Evaluate();
        nudges.Evaluate();
        nudges.Evaluate();

        Assert.Equal("photoshop", nudges.Current!.Subject);   // the one used most comes first
        Assert.Equal(1, changes);                             // looking again does not raise it again

        nudges.Answer(nudges.Current);
        Assert.Equal("blender", nudges.Current!.Subject);
        nudges.Answer(nudges.Current);
        Assert.Null(nudges.Current);
    }

    [Fact]
    public void TurningTheAssistantOff_RemovesTheSuggestion()
    {
        TurnOn();
        _usage.Add("photoshop", 600, 20);
        var nudges = Create();
        nudges.Evaluate();
        Assert.NotNull(nudges.Current);

        _store.Update(s => s.Enabled = false);
        nudges.Evaluate();

        Assert.Null(nudges.Current);
    }

    // ── a kind of module attached for the first time ────────────────────────────────────────

    [Fact]
    public void TheModulesThatAreThereWhenTheAssistantStarts_AreKnown_NotNew()
    {
        TurnOn();
        var nudges = Create();

        nudges.ModulesConnected(new[] { ModuleKinds.Keys, ModuleKinds.Faders });

        Assert.Null(nudges.Current);
        Assert.Equal(new[] { ModuleKinds.Keys, ModuleKinds.Faders }, _store.Read().SeenModules!.ToArray());
    }

    [Fact]
    public void ANewKindOfModule_IsSuggestedOnce()
    {
        TurnOn();
        var nudges = Create();
        nudges.ModulesConnected(new[] { ModuleKinds.Keys });

        nudges.ModulesConnected(new[] { ModuleKinds.Keys, ModuleKinds.Knobs });

        Assert.Equal(NudgeKind.NewModule, nudges.Current!.Kind);
        Assert.Equal(ModuleKinds.Knobs, nudges.Current.Subject);

        nudges.Answer(nudges.Current);
        nudges.ModulesConnected(new[] { ModuleKinds.Keys, ModuleKinds.Knobs });
        Assert.Null(nudges.Current);

        // unplugged and plugged in again: not new any more
        nudges.ModulesConnected(new[] { ModuleKinds.Keys });
        nudges.ModulesConnected(new[] { ModuleKinds.Keys, ModuleKinds.Knobs });
        Assert.Null(nudges.Current);
    }

    [Fact]
    public void ANewModule_IsSuggestedAfterARestartToo_IfItWasNotAnsweredYet()
    {
        TurnOn();
        Create().ModulesConnected(new[] { ModuleKinds.Keys });

        var restarted = Create();
        restarted.ModulesConnected(new[] { ModuleKinds.Keys, ModuleKinds.Dial });

        Assert.Equal(ModuleKinds.Dial, restarted.Current!.Subject);
    }

    // ── a key that is never pressed ─────────────────────────────────────────────────────────

    private ProfileModel ProfileWithKeys()
    {
        var p = _profiles.GetActiveProfile();
        p.Keys = new List<KeyAssignmentModel>
        {
            new() { Index = 0, Action = DeckActionType.MicMute, CustomTitle = "MIC" },
            new() { Index = 1, Action = DeckActionType.Screenshot, CustomTitle = "SHOT" }
        };
        p.Pages[0].Keys = new List<KeyAssignmentModel>(p.Keys);
        _profiles.SaveProfile(p);
        return _profiles.GetActiveProfile();
    }

    [Fact]
    public void AnUnusedKey_IsSuggested_OnlyWithPressCounting_AfterEnoughUse()
    {
        var active = ProfileWithKeys();
        _presses.Since = _now.AddDays(-10);
        _presses.Counts["p:0:0"] = 120;   // MIC is used a lot, SHOT never

        TurnOn(pressCounting: false);
        var off = Create();
        off.Evaluate();
        Assert.Null(off.Current);          // counting is off: no suggestion from numbers

        TurnOn(pressCounting: true);
        var nudges = Create();
        nudges.Evaluate();

        Assert.Equal(NudgeKind.UnusedKey, nudges.Current!.Kind);
        Assert.Equal($"{active.Id}|1", nudges.Current.Subject);
        Assert.Equal("SHOT", nudges.Current.Display);
    }

    [Theory]
    [InlineData(3, 120)]    // counted for too short a time
    [InlineData(10, 10)]    // not enough presses to say anything
    public void AnUnusedKey_IsNotSuggested_TooEarly(int daysCounted, int presses)
    {
        ProfileWithKeys();
        TurnOn(pressCounting: true);
        _presses.Since = _now.AddDays(-daysCounted);
        _presses.Counts["p:0:0"] = presses;
        var nudges = Create();

        nudges.Evaluate();

        Assert.Null(nudges.Current);
    }

    [Fact]
    public void WhenEveryKeyWasPressed_ThereIsNothingToSuggest()
    {
        ProfileWithKeys();
        TurnOn(pressCounting: true);
        _presses.Since = _now.AddDays(-30);
        _presses.Counts["p:0:0"] = 80;
        _presses.Counts["p:0:1"] = 3;
        var nudges = Create();

        nudges.Evaluate();

        Assert.Null(nudges.Current);
    }
}

/// <summary>T-37: a sketch for the module that was just attached fills only the controls that have nothing yet.</summary>
public sealed class ModuleSketchTests
{
    private static ProfileModel Empty() => ProfileModel.CreateEmpty("Mine");

    [Fact]
    public void Faders_GetMasterVolumeAndTheMic_OnTheFirstFreeOnes()
    {
        var profile = Empty();
        var sketch = ModuleSketch.Build(ModuleKinds.Faders, profile)!;

        var assigned = sketch.Sliders.Where(s => s.TargetType != AudioTargetType.None).OrderBy(s => s.Index).ToList();
        Assert.Equal(2, assigned.Count);
        Assert.Equal(AudioTargetType.MasterPlayback, assigned[0].TargetType);
        Assert.Equal(AudioTargetType.MasterRecording, assigned[1].TargetType);
        Assert.All(assigned, s => Assert.True(s.Brightness > 0));
        // the strings the fader view model reads back must still parse to the same targets
        Assert.Contains("🔊", assigned[0].DisplayTarget);
        Assert.Contains("🎙️", assigned[1].DisplayTarget);
    }

    [Fact]
    public void AFaderThatAlreadyHasAJob_IsNeverTouched()
    {
        var profile = Empty();
        var first = profile.Sliders.OrderBy(s => s.Index).First();
        first.TargetType = AudioTargetType.Process;
        first.TargetProcessName = "spotify";
        first.DisplayTarget = "🎵 spotify";

        var sketch = ModuleSketch.Build(ModuleKinds.Faders, profile)!;

        var kept = sketch.Sliders.Single(s => s.Index == first.Index);
        Assert.Equal(AudioTargetType.Process, kept.TargetType);
        Assert.Equal("spotify", kept.TargetProcessName);
        Assert.Equal(2, sketch.Sliders.Count(s => s.TargetType is AudioTargetType.MasterPlayback or AudioTargetType.MasterRecording));
    }

    [Fact]
    public void WhenEveryControlOfTheModuleHasAJob_ThereIsNothingToSketch()
    {
        var profile = Empty();
        foreach (var s in profile.Sliders) s.TargetType = AudioTargetType.MasterPlayback;
        foreach (var k in profile.Knobs) k.RotateAction = RollerActionType.MasterVolume;
        foreach (var r in profile.Rollers) r.Action = RollerActionType.ScrollVertical;
        profile.Dial.DisplayWidget = "Clock";
        profile.BaseBrightness = 80;
        profile.LedColorHex = "#FF0000";

        Assert.Null(ModuleSketch.Build(ModuleKinds.Faders, profile));
        Assert.Null(ModuleSketch.Build(ModuleKinds.Knobs, profile));
        Assert.Null(ModuleSketch.Build(ModuleKinds.Rollers, profile));
        Assert.Null(ModuleSketch.Build(ModuleKinds.Dial, profile));
        Assert.Null(ModuleSketch.Build(ModuleKinds.Base, profile));
    }

    [Fact]
    public void Knobs_Rollers_Dial_AndTheEdgeLight_GetSensibleDefaults()
    {
        var knobs = ModuleSketch.Build(ModuleKinds.Knobs, Empty())!;
        Assert.Equal(RollerActionType.MasterVolume, knobs.Knobs.OrderBy(k => k.Index).First().RotateAction);

        var rollers = ModuleSketch.Build(ModuleKinds.Rollers, Empty())!;
        Assert.Contains(rollers.Rollers, r => r.Action == RollerActionType.MasterVolume);

        var dial = ModuleSketch.Build(ModuleKinds.Dial, Empty())!;
        Assert.Equal("Clock", dial.Dial.DisplayWidget);

        var edge = ModuleSketch.Build(ModuleKinds.Base, Empty())!;
        Assert.True(edge.BaseBrightness > 0);
        Assert.NotEqual("#000000", edge.LedColorHex);
    }

    [Fact]
    public void ADialWithAnImage_IsTheUsersOwn()
    {
        var profile = Empty();
        profile.Dial.ImagePath = @"C:\pics\mine.png";

        Assert.Null(ModuleSketch.Build(ModuleKinds.Dial, profile));
    }

    [Fact]
    public void AnUnknownModule_HasNoSketch() => Assert.Null(ModuleSketch.Build("toaster", Empty()));
}
