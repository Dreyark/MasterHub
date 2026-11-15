using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Avalonia.Views.Controls;
using MasterHub.Avalonia.Views.Pages;
using MasterHub.Core.Actions;
using MasterHub.Core.Apps;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.YouTube;
using MasterHub.Core.Localization;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Auto;
using MasterHub.Core.Profiles.Draft;
using MasterHub.Core.Profiles.Templates;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;

namespace MasterHub.UiTests;

/// <summary>T-37 in the app: the one-time question, the suggestion card, prepared profiles on the Profiles page, press counting.</summary>
public sealed class AssistUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_assist_ui_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private sealed class FakeInstalled : IInstalledAppsService
    {
        public IReadOnlyList<InstalledApp> Apps { get; set; } = Array.Empty<InstalledApp>();
        public bool IsScanning => false;
        public DateTime? LastScanUtc => null;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public event Action? Changed { add { } remove { } }
    }

    private sealed class FakeUsage : IAppUsageService
    {
        public Dictionary<string, AppUsageEntry> Data { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool IsRunning { get; private set; }
        public DateTime Since => new(2026, 11, 1);
        public event Action<string>? AppInFront;
        public IReadOnlyDictionary<string, AppUsageEntry> Snapshot() => Data;
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Clear() => Data.Clear();
        public void Dispose() { }
        public void Raise(string app) => AppInFront?.Invoke(app);
    }

    private sealed class Rig
    {
        public ProfileService Profiles = null!;
        public CanvasViewModel Canvas = null!;
        public DraftSessionViewModel Draft = null!;
        public TourViewModel Tour = null!;
        public ProfileTemplateService Templates = null!;
        public AssistSettingsStore Store = null!;
        public FakeUsage Usage = null!;
        public FakeInstalled Installed = null!;
        public PressCounter Presses = null!;
        public AutoProfileService Auto = null!;
        public NudgeService Nudges = null!;
        public AssistViewModel Assist = null!;
        public DateTime Now = new(2026, 11, 14, 12, 0, 0);
    }

    /// <summary>The deck the assistant sees (none by default); a test sets it before creating the rig.</summary>
    private IMasterHubDeviceService? _device;

    private Rig CreateRig(params InstalledApp[] installedApps)
    {
        var rig = new Rig();
        rig.Profiles = new ProfileService(_dir, new SecretProtectionService());
        var (audio, _) = ScriptedProxy<IAudioService>.Create();
        rig.Store = new AssistSettingsStore(rig.Profiles);
        var store = rig.Store;
        rig.Presses = new PressCounter(() => store.PressCountingOn, Path.Combine(_dir, "presses.json"));
        rig.Canvas = new CanvasViewModel(F<IMasterHubDeviceService>(), F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            audio, F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), rig.Profiles,
            pressCounter: rig.Presses);
        rig.Canvas.ApplyProfile(rig.Profiles.GetActiveProfile());

        rig.Templates = new ProfileTemplateService(rig.Profiles);
        rig.Draft = new DraftSessionViewModel(rig.Profiles, rig.Canvas);
        rig.Tour = new TourViewModel(rig.Draft, rig.Profiles, rig.Templates, null, F<IMasterHubDeviceService>(), () => Array.Empty<string>());
        rig.Installed = new FakeInstalled { Apps = installedApps };
        rig.Usage = new FakeUsage();
        rig.Auto = new AutoProfileService(rig.Profiles, rig.Templates, rig.Installed, rig.Store, () => "en");
        rig.Nudges = new NudgeService(rig.Profiles, rig.Store, rig.Usage, rig.Presses, rig.Installed, () => rig.Now);
        rig.Assist = new AssistViewModel(rig.Store, rig.Usage, rig.Presses, rig.Auto, rig.Nudges, rig.Profiles, rig.Draft, rig.Tour,
            rig.Installed, _device, rig.Templates);
        return rig;
    }

    private static InstalledApp Program(string name, string exe) =>
        new("start:" + exe, name, InstalledAppKind.StartMenu, @"C:\apps\" + exe + ".lnk", @"C:\apps\" + exe + ".exe");

    private static string Word(string key, string fallback) => LocalizationService.Instance.GetString(key, fallback);

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static void WaitUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Assert.True(condition(), "timed out");
    }

    // ── the one-time question ───────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TheQuestion_IsAskedOnce_AndNothingIsOnUntilTheUserSaysYes()
    {
        var rig = CreateRig();

        rig.Assist.Start();

        Assert.True(rig.Assist.ShowOffer);
        Assert.False(rig.Assist.IsEnabled);
        Assert.False(rig.Usage.IsRunning);   // nothing is counted before the answer
        Assert.False(rig.Store.Enabled);

        rig.Assist.DeclineOfferCommand.Execute(null);

        Assert.False(rig.Assist.ShowOffer);
        Assert.False(rig.Store.Enabled);
        Assert.False(rig.Usage.IsRunning);

        // not asked again after a restart, either
        var again = CreateRig();
        again.Assist.Start();
        Assert.False(again.Assist.ShowOffer);
    }

    [AvaloniaFact]
    public void TheQuestion_WaitsForTheTour_AndForADraft()
    {
        var rig = CreateRig();
        rig.Tour.Run();
        rig.Assist.Start();
        Assert.False(rig.Assist.ShowOffer);   // the tour is on screen

        rig.Tour.SkipCommand.Execute(null);
        Pump();
        Assert.True(rig.Assist.ShowOffer);

        rig.Draft.Start(rig.Templates.Build(rig.Templates.GetAll().First()), DraftMode.Preview);
        Pump();
        Assert.False(rig.Assist.ShowOffer);   // a draft is on the deck
    }

    [AvaloniaFact]
    public void SayingYes_StartsLooking_AndPreparesProfilesSwitchedOff()
    {
        var rig = CreateRig(Program("OBS Studio", "obs64"));
        rig.Assist.Start();

        rig.Assist.AcceptOfferCommand.Execute(null);

        Assert.True(rig.Store.Enabled);
        Assert.True(rig.Usage.IsRunning);
        Assert.False(rig.Assist.ShowOffer);
        Assert.NotNull(rig.Store.Read().SeenModules);   // what is connected now is "known", not new

        WaitUntil(() => rig.Profiles.GetAllProfiles().Any(p => p.AutoMade));
        Pump();
        Assert.Equal(1, rig.Assist.UnseenCount);
        Assert.True(rig.Assist.HasUnseen);
        Assert.All(rig.Profiles.GetAllProfiles().Where(p => p.AutoMade).SelectMany(p => p.SmartRules), r => Assert.False(r.IsEnabled));
    }

    [AvaloniaFact]
    public void TurningTheSwitchOff_StopsEverything_AndTheNumbersCanBeDeleted()
    {
        var rig = CreateRig();
        rig.Assist.IsEnabled = true;
        rig.Assist.PressCounting = true;
        Assert.True(rig.Store.PressCountingOn);
        rig.Usage.Data["photoshop"] = new AppUsageEntry { Seconds = 3600, Days = 4 };

        rig.Assist.IsEnabled = false;

        Assert.False(rig.Store.Enabled);
        Assert.False(rig.Store.PressCountingOn);   // counting presses needs the switch
        Assert.False(rig.Assist.PressCounting);
        Assert.False(rig.Usage.IsRunning);

        rig.Assist.DeleteDataCommand.Execute(null);
        Assert.Empty(rig.Usage.Data);
        Assert.Equal(Word("Assist_DataNone", "Nothing has been counted yet."), rig.Assist.DataNote);
    }

    [AvaloniaFact]
    public void AGameComingToTheFront_GetsAProfile_OnlyWhenTheAssistantIsOn()
    {
        var game = new InstalledApp("steam:1", "Quest of Tests", InstalledAppKind.Steam, "steam://rungameid/1", @"C:\games\quest.exe");
        var rig = CreateRig(game);
        rig.Assist.Start();

        rig.Usage.Raise("quest");   // the assistant is still off
        Assert.DoesNotContain(rig.Profiles.GetAllProfiles(), p => p.AutoMade);

        rig.Assist.IsEnabled = true;
        rig.Usage.Raise("quest");
        Pump();

        var made = Assert.Single(rig.Profiles.GetAllProfiles(), p => p.AutoMade);
        Assert.Equal("Quest of Tests", made.Name);
        Assert.Equal(1, rig.Assist.UnseenCount);
    }

    // ── the suggestion card ─────────────────────────────────────────────────────────────────

    private Rig WithAProgramWorkedInALot(string name = "photoshop")
    {
        var rig = CreateRig(Program("Photoshop", name));
        rig.Usage.Data[name] = new AppUsageEntry { Seconds = 7200, Days = 6 };
        rig.Assist.IsEnabled = true;
        rig.Nudges.Evaluate();
        Pump();
        return rig;
    }

    [AvaloniaFact]
    public void AProgramWorkedInALot_ShowsTheCard_WithItsName()
    {
        var rig = WithAProgramWorkedInALot();

        Assert.True(rig.Assist.HasNudge);
        Assert.Contains("Photoshop", rig.Assist.NudgeText);
        Assert.Equal(Word("Assist_ShowSketch", "Show a sketch"), rig.Assist.NudgeAccept);
    }

    [AvaloniaFact]
    public void Accepting_OpensADraft_Keeping_LinksTheProgram_AndItIsNotSuggestedAgain()
    {
        var rig = WithAProgramWorkedInALot();
        var mine = rig.Profiles.GetActiveProfile().Id;

        rig.Assist.AcceptNudgeCommand.Execute(null);
        Pump();

        Assert.True(rig.Draft.IsActive);                       // a sketch to look at, nothing saved yet
        Assert.DoesNotContain(rig.Profiles.GetAllProfiles(), p => p.Name == "Photoshop");
        Assert.False(rig.Assist.HasNudge);                     // out of the way while the draft is on the deck

        var kept = rig.Draft.Keep()!;
        Pump();

        var saved = rig.Profiles.GetAllProfiles().Single(p => p.Id == kept.Id);
        Assert.Equal("Photoshop", saved.Name);
        var rule = Assert.Single(saved.SmartRules);
        Assert.Equal("photoshop", rule.ProcessName);
        Assert.True(rule.IsEnabled);                           // the user kept it on purpose
        Assert.NotEqual(mine, saved.Id);
        Assert.False(rig.Assist.HasNudge);                     // answered: it does not come back
    }

    [AvaloniaFact]
    public void ASketchThatIsThrownAway_LeavesNoLink()
    {
        var rig = WithAProgramWorkedInALot();

        rig.Assist.AcceptNudgeCommand.Execute(null);
        rig.Draft.Discard();
        Pump();

        Assert.All(rig.Profiles.GetAllProfiles(), p => Assert.Empty(p.SmartRules));
        Assert.DoesNotContain(rig.Profiles.GetAllProfiles(), p => p.Name == "Photoshop");
    }

    [AvaloniaFact]
    public void ASketchKeptLater_ByAnotherDraft_IsNotLinked()
    {
        var rig = WithAProgramWorkedInALot();
        rig.Assist.AcceptNudgeCommand.Execute(null);
        rig.Draft.Discard();
        Pump();   // the link of the thrown-away sketch is forgotten

        rig.Draft.Start(rig.Templates.Build(rig.Templates.GetAll().First(t => t.Id == "obs-streaming")), DraftMode.Preview);
        var kept = rig.Draft.Keep()!;

        Assert.Empty(rig.Profiles.GetAllProfiles().Single(p => p.Id == kept.Id).SmartRules);
    }

    [AvaloniaFact]
    public void NoThanks_NeverComesBack()
    {
        var rig = WithAProgramWorkedInALot();

        rig.Assist.DismissNudgeCommand.Execute(null);
        Pump();
        rig.Nudges.Evaluate();

        Assert.False(rig.Assist.HasNudge);
        Assert.False(rig.Draft.IsActive);   // saying no changes nothing
        Assert.Contains("app:photoshop", rig.Store.Read().DismissedNudges);
    }

    [AvaloniaFact]
    public void TheCard_StepsAside_ForADraftOrTheTour_AndComesBackUnanswered()
    {
        var rig = WithAProgramWorkedInALot();
        Assert.True(rig.Assist.HasNudge);

        rig.Draft.Start(rig.Templates.Build(rig.Templates.GetAll().First()), DraftMode.Preview);
        Pump();
        Assert.False(rig.Assist.HasNudge);

        rig.Draft.Discard();
        Pump();
        Assert.True(rig.Assist.HasNudge);   // it was never answered
    }

    [AvaloniaFact]
    public void ANewModule_OffersASketch_ThatOnlyFillsFreeControls()
    {
        var (device, deck) = ScriptedProxy<IMasterHubDeviceService>.Create();
        deck.Values["IsKeypadConnected"] = true;
        _device = device;
        var rig = CreateRig();

        rig.Assist.IsEnabled = true;   // the keypad that is connected now was there before the assistant: known, not new
        Pump();
        Assert.False(rig.Assist.HasNudge);

        deck.Values["IsSliderConnected"] = true;
        rig.Nudges.ModulesConnected(new[] { ModuleKinds.Keys, ModuleKinds.Faders });   // the faders are new
        Pump();

        Assert.True(rig.Assist.HasNudge);
        Assert.Contains(Word("Tour_ModFaders", "faders"), rig.Assist.NudgeText);

        rig.Assist.AcceptNudgeCommand.Execute(null);
        Pump();

        Assert.True(rig.Draft.IsActive);
        Assert.Contains(rig.Profiles.GetActiveProfile().Sliders, s => s.TargetType == AudioTargetType.MasterPlayback);
        Assert.Contains(rig.Profiles.GetActiveProfile().Sliders, s => s.TargetType == AudioTargetType.MasterRecording);
    }

    [AvaloniaFact]
    public void AnUnusedKey_PointsToTheStudio()
    {
        var rig = CreateRig();
        rig.Assist.IsEnabled = true;
        rig.Assist.PressCounting = true;

        // a profile with two keys, only one of which was ever pressed, counted for ten days
        rig.Canvas.Keys[0].AssignedAction = DeckActionType.MicMute;
        rig.Canvas.Keys[0].CustomTitle = "MIC";
        rig.Canvas.Keys[1].AssignedAction = DeckActionType.Screenshot;
        rig.Canvas.Keys[1].CustomTitle = "SHOT";
        rig.Canvas.SaveCurrentStateToActiveProfile();
        var counterClock = new DateTime(2026, 11, 4, 12, 0, 0);
        var counter = new PressCounter(() => true, Path.Combine(_dir, "p2.json"), () => counterClock);
        string id = rig.Profiles.GetActiveProfile().Id;
        for (int i = 0; i < 60; i++) counter.Count(id, "p:0:0");

        var nudges = new NudgeService(rig.Profiles, rig.Store, rig.Usage, counter, null, () => rig.Now);
        var assist = new AssistViewModel(rig.Store, rig.Usage, counter, rig.Auto, nudges, rig.Profiles, rig.Draft, rig.Tour, null, null, rig.Templates);
        bool opened = false;
        assist.OpenStudioRequested = () => opened = true;
        nudges.Evaluate();
        Pump();

        Assert.True(assist.HasNudge);
        Assert.Contains("SHOT", assist.NudgeText);
        Assert.Equal(Word("Assist_OpenStudio", "Open the Studio"), assist.NudgeAccept);

        assist.AcceptNudgeCommand.Execute(null);

        Assert.True(opened);
        Assert.False(rig.Draft.IsActive);   // nothing was started: it only points
    }

    // ── prepared profiles on the Profiles page ──────────────────────────────────────────────

    private ProfilesPageViewModel PageFor(Rig rig) => new(rig.Profiles, templates: rig.Templates, autoProfiles: rig.Auto);

    [AvaloniaFact]
    public void ProfilesPage_ListsPreparedProfilesInTheirOwnGroup_WithANewMark_AndSwitchOn()
    {
        var rig = CreateRig(Program("OBS Studio", "obs64"));
        rig.Assist.IsEnabled = true;
        WaitUntil(() => rig.Profiles.GetAllProfiles().Any(p => p.AutoMade));
        var page = PageFor(rig);

        var item = Assert.Single(page.Profiles, p => p.IsReady);
        Assert.Equal(Word("Profiles_GroupReady", "Ready for your apps"), item.GroupLabel);
        Assert.True(item.IsNew);
        Assert.Contains("obs64", item.ReadyHint);
        Assert.Equal(1, page.Profiles.Count(p => p.GroupLabel == item.GroupLabel));   // not mixed into "For apps and games"

        Assert.True(File.Exists(Shot.Render(new ProfilesPageView { DataContext = page }, "page_profiles_ready", 1000, 760)));

        page.SwitchOnCommand.Execute(item);

        var now = Assert.Single(page.Profiles, p => p.Id == item.Id);
        Assert.False(now.IsReady);
        Assert.NotEqual(item.GroupLabel, now.GroupLabel);
        Assert.Contains("obs64", page.StatusMessage);
        Assert.All(rig.Profiles.GetAllProfiles().Single(p => p.Id == item.Id).SmartRules, r => Assert.True(r.IsEnabled));
    }

    [AvaloniaFact]
    public void TheNewMark_IsDoneWith_OnceTheProfilesPageHasBeenSeen()
    {
        var rig = CreateRig(Program("OBS Studio", "obs64"));
        rig.Assist.IsEnabled = true;
        WaitUntil(() => rig.Profiles.GetAllProfiles().Any(p => p.AutoMade));
        Pump();
        Assert.Equal(1, rig.Assist.UnseenCount);

        rig.Assist.MarkProfilesSeen();

        Assert.Equal(0, rig.Assist.UnseenCount);
        Assert.False(PageFor(rig).Profiles.Single(p => p.IsReady).IsNew);
    }

    // ── press counting ──────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Presses_AreCountedOnlyWhenOn_NeverInAPreview_AndNeverForAnEmptyKey()
    {
        var rig = CreateRig();
        string id = rig.Profiles.GetActiveProfile().Id;
        rig.Canvas.Keys[0].AssignedAction = DeckActionType.MediaStop;

        rig.Canvas.RunGesture(0, KeyGesture.Press);
        Assert.Empty(rig.Presses.Get(id));   // counting is off

        rig.Assist.IsEnabled = true;
        rig.Assist.PressCounting = true;

        rig.Canvas.RunGesture(0, KeyGesture.Press);
        rig.Canvas.RunGesture(0, KeyGesture.Press);
        rig.Canvas.RunGesture(5, KeyGesture.Press);   // nothing assigned: not "use"
        Assert.Equal(2, rig.Presses.Get(id)["p:0:0"]);
        Assert.False(rig.Presses.Get(id).ContainsKey("p:0:5"));

        rig.Draft.Start(rig.Templates.Build(rig.Templates.GetAll().First(t => t.Id == "obs-streaming")), DraftMode.Preview);
        rig.Canvas.RunGesture(0, KeyGesture.Press);
        Assert.Empty(rig.Presses.Get(rig.Profiles.GetActiveProfile().Id));   // a preview is not use
        Assert.Equal(2, rig.Presses.Get(id)["p:0:0"]);

        rig.Assist.PressCounting = false;
        rig.Draft.Discard();
        rig.Canvas.RunGesture(0, KeyGesture.Press);
        Assert.Equal(2, rig.Presses.Get(id)["p:0:0"]);   // off again: no more counting
    }

    // ── what it looks like ──────────────────────────────────────────────────────────────────

    /// <summary>The floating cards (assistant, tour, draft banner) have a solid background: the text under them must not show through.</summary>
    [AvaloniaFact]
    public void TheFloatingCards_AreOpaque_AndHideTheTextBelow()
    {
        var rig = CreateRig();
        rig.Assist.Start();                    // the one-time question is showing
        rig.Tour.Run();                        // and the tour (a question step)
        rig.Draft.Start(rig.Templates.Build(rig.Templates.GetAll().First()), DraftMode.Preview);

        var text = new global::Avalonia.Controls.StackPanel();
        for (int i = 0; i < 40; i++)
            text.Children.Add(new global::Avalonia.Controls.TextBlock { Text = "Text on the page below the cards, line " + i + " – it must not shine through", FontSize = 14 });

        var layers = new global::Avalonia.Controls.Panel();
        layers.Children.Add(text);
        layers.Children.Add(new AssistCardView { DataContext = rig.Assist });
        layers.Children.Add(new TourView { DataContext = rig.Tour });
        layers.Children.Add(new DraftBannerView { DataContext = rig.Draft });

        int checkedCards = 0;
        Shot.Render(layers, "floating_cards_over_text", 900, 640, window =>
        {
            foreach (var border in global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
                         .OfType<global::Avalonia.Controls.Border>().Where(b => b.Classes.Contains("floating") && b.IsEffectivelyVisible))
            {
                var brush = Assert.IsAssignableFrom<global::Avalonia.Media.ISolidColorBrush>(border.Background);
                Assert.Equal(255, brush.Color.A);
                Assert.Equal(1.0, brush.Opacity);
                Assert.True(border.BoxShadow.Count > 0);   // and it stands off the page with a shadow
                checkedCards++;
            }
        });

        Assert.True(checkedCards >= 2, "the question, the tour and the banner should all have been on screen");
    }

    [AvaloniaFact]
    public void TheCardsAndTheSettingsCard_Render()
    {
        var rig = CreateRig();
        rig.Assist.Start();
        Assert.True(File.Exists(Shot.Render(new AssistCardView { DataContext = rig.Assist }, "assist_offer", 520, 340)));

        rig.Assist.IsEnabled = true;
        Assert.True(File.Exists(Shot.Render(new AssistSettingsCardView { DataContext = rig.Assist }, "assist_settings", 760, 360)));

        var nudged = WithAProgramWorkedInALot();
        Assert.True(File.Exists(Shot.Render(new AssistCardView { DataContext = nudged.Assist }, "assist_nudge", 520, 200)));
    }
}
