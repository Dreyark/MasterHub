using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Avalonia.Views.Controls;
using MasterHub.Core.Actions;
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
using MasterHub.Core.Profiles.Draft;
using MasterHub.Core.Profiles.Templates;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;

namespace MasterHub.UiTests;

/// <summary>T-08 in the Studio: the draft on the deck (banner, dry-run, keep / discard, trial, crash restore) and the setup tour.</summary>
public sealed class DraftTourUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_tour_ui_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private sealed record Rig(ProfileService Profiles, CanvasViewModel Canvas, DraftSessionViewModel Draft, ProfileTemplateService Templates,
        ScriptedProxy<IAudioService> Audio);

    private Rig CreateRig()
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var (audio, script) = ScriptedProxy<IAudioService>.Create();
        var canvas = new CanvasViewModel(F<IMasterHubDeviceService>(), F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            audio, F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles);
        canvas.ApplyProfile(profiles.GetActiveProfile());
        return new Rig(profiles, canvas, new DraftSessionViewModel(profiles, canvas), new ProfileTemplateService(profiles), script);
    }

    private TourViewModel CreateTour(Rig rig, params string[] running) =>
        new(rig.Draft, rig.Profiles, rig.Templates, null, F<IMasterHubDeviceService>(), () => running);

    private static string Word(string key, string fallback) => LocalizationService.Instance.GetString(key, fallback);

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

    /// <summary>The user's own profile with a recognisable key (a mic mute on key 1).</summary>
    private static ProfileModel UserProfile(Rig rig)
    {
        rig.Canvas.Keys[0].AssignedAction = DeckActionType.MicMute;
        rig.Canvas.Keys[0].CustomTitle = "MY MIC";
        rig.Canvas.SaveCurrentStateToActiveProfile();
        return rig.Profiles.GetActiveProfile();
    }

    private ProfileModel StreamingDraft(Rig rig) => rig.Templates.Build(rig.Templates.GetAll().First(t => t.Id == "obs-streaming"));

    // ── the draft ───────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ADraft_IsShownOnTheDeck_WithoutTouchingTheUsersProfile()
    {
        var rig = CreateRig();
        var mine = UserProfile(rig);
        string before = File.ReadAllText(Directory.GetFiles(Path.Combine(_dir, "profiles"), "*.json").Single());

        rig.Draft.Start(StreamingDraft(rig), DraftMode.Preview);

        Assert.True(rig.Draft.IsActive);
        Assert.True(rig.Draft.IsVisible);
        Assert.True(rig.Canvas.IsDryRun);
        Assert.Contains(rig.Canvas.Keys, k => k.AssignedAction == DeckActionType.ObsToggleStream);   // the draft is on the canvas
        Assert.NotEqual("MY MIC", rig.Canvas.Keys[0].CustomTitle);
        Assert.Contains("OBS", rig.Draft.Title, StringComparison.OrdinalIgnoreCase);

        // an edit made in the preview goes to the draft, never to the user's profile
        rig.Canvas.Keys[14].AssignedAction = DeckActionType.MediaStop;
        rig.Canvas.SaveCurrentStateToActiveProfile();
        Assert.Equal(before, File.ReadAllText(Directory.GetFiles(Path.Combine(_dir, "profiles"), "*.json").Single()));
        Assert.Single(rig.Profiles.GetAllProfiles());
        _ = mine;
    }

    [AvaloniaFact]
    public void TheDryRun_BlocksWhatWouldChangeTheRealSetup_ButNotLookingAround()
    {
        var rig = CreateRig();
        var notices = new List<DryRunNotice>();
        rig.Canvas.DryRunBlocked += notices.Add;
        rig.Draft.Start(rig.Templates.Build(rig.Templates.GetAll().First(t => t.Id == "discord-meetings")), DraftMode.Preview);

        var mic = rig.Canvas.Keys[5];
        mic.AssignedAction = DeckActionType.MicMute;
        rig.Canvas.ExecuteKeyAction(mic);                          // would mute the real microphone

        Assert.Equal(0, rig.Audio.Calls.GetValueOrDefault("ToggleMasterRecordingMute"));
        var notice = Assert.Single(notices);
        Assert.Equal(6, notice.KeyNumber);
        Assert.True(rig.Draft.HasBlocked);
        Assert.Contains("6", rig.Draft.BlockedText);

        var next = rig.Canvas.Keys[6];
        next.AssignedAction = DeckActionType.NextPage;
        rig.Canvas.ExecuteKeyAction(next);                         // moving around the draft is allowed
        Assert.Single(notices);                                    // no new notice
    }

    [AvaloniaFact]
    public void AfterTheDraftIsGone_AKeyRunsForReal_Again()
    {
        var rig = CreateRig();
        var mine = UserProfile(rig);
        rig.Draft.Start(StreamingDraft(rig), DraftMode.Preview);
        rig.Draft.Discard();

        rig.Canvas.ExecuteKeyAction(rig.Canvas.Keys[0]);           // the user's own mic key is back and works

        Assert.Equal(1, rig.Audio.Calls["ToggleMasterRecordingMute"]);
        Assert.False(rig.Canvas.IsDryRun);
        _ = mine;
    }

    [AvaloniaFact]
    public void Discard_PutsTheUsersProfileBackOnTheDeck_AndLeavesNothing()
    {
        var rig = CreateRig();
        UserProfile(rig);
        rig.Draft.Start(StreamingDraft(rig), DraftMode.Preview);

        rig.Draft.Discard();

        Assert.False(rig.Draft.IsActive);
        Assert.False(rig.Draft.IsVisible);
        Assert.Equal("MY MIC", rig.Canvas.Keys[0].CustomTitle);
        Assert.Equal(DeckActionType.MicMute, rig.Canvas.Keys[0].AssignedAction);
        Assert.Single(rig.Profiles.GetAllProfiles());
        Assert.False(File.Exists(Path.Combine(_dir, "drafts", "draft.json")));
    }

    [AvaloniaFact]
    public void Keep_SavesTheDraftAsANewProfile_AndFurtherEditsGoThere()
    {
        var rig = CreateRig();
        var mine = UserProfile(rig);
        var kept = default(ProfileModel);
        rig.Draft.Kept += p => kept = p;
        rig.Draft.Start(StreamingDraft(rig), DraftMode.Preview);

        var result = rig.Draft.Keep();

        Assert.NotNull(result);
        Assert.Same(result, kept);
        Assert.Equal(2, rig.Profiles.GetAllProfiles().Count);
        Assert.Equal(kept!.Id, rig.Profiles.GetActiveProfile().Id);
        Assert.False(rig.Canvas.IsDryRun);

        rig.Canvas.Keys[14].AssignedAction = DeckActionType.MediaStop;
        rig.Canvas.SaveCurrentStateToActiveProfile();
        var saved = rig.Profiles.GetAllProfiles().First(p => p.Id == kept.Id);
        Assert.Contains(saved.Keys, k => k.Index == 14 && k.Action == DeckActionType.MediaStop);

        var original = rig.Profiles.GetAllProfiles().First(p => p.Id == mine.Id);
        Assert.DoesNotContain(original.Keys, k => k.Index == 14 && k.Action == DeckActionType.MediaStop);   // the user's profile is as it was
    }

    [AvaloniaFact]
    public void ATrial_PutsItselfAway_WhenTheTimeIsUp_AndShowsItsCountdown()
    {
        var rig = CreateRig();
        UserProfile(rig);
        rig.Draft.Start(StreamingDraft(rig), DraftMode.Trial, 20);
        var t0 = rig.Profiles.ActiveDraft!.StartedUtc;
        var now = t0;
        rig.Draft.UseClock(() => now);

        Assert.True(rig.Draft.HasCountdown);
        now = t0.AddSeconds(8);
        rig.Draft.OnClock();
        Assert.Equal(string.Format(Word("Draft_SecondsLeft", "{0} s left"), 12), rig.Draft.CountdownText);
        Assert.True(rig.Draft.IsActive);

        now = t0.AddSeconds(20);
        rig.Draft.OnClock();

        Assert.False(rig.Draft.IsActive);
        Assert.Equal("MY MIC", rig.Canvas.Keys[0].CustomTitle);      // the user's deck is back
    }

    [AvaloniaFact]
    public void APreview_NeverExpires()
    {
        var rig = CreateRig();
        rig.Draft.Start(StreamingDraft(rig), DraftMode.Preview);
        var t0 = rig.Profiles.ActiveDraft!.StartedUtc;
        rig.Draft.UseClock(() => t0.AddHours(3));

        rig.Draft.OnClock();

        Assert.True(rig.Draft.IsActive);
        Assert.False(rig.Draft.HasCountdown);
    }

    [AvaloniaFact]
    public void ChoosingAnotherProfile_EndsThePreview_AndTheBannerGoesAway()
    {
        var rig = CreateRig();
        var other = rig.Profiles.CreateProfile("Other", setAsActive: false);
        rig.Draft.Start(StreamingDraft(rig), DraftMode.Preview);
        int ended = 0;
        rig.Draft.Ended += () => ended++;

        rig.Profiles.SetActiveProfile(other.Id);                    // the Profiles page, the tray, the API

        Assert.False(rig.Draft.IsVisible);
        Assert.False(rig.Canvas.IsDryRun);
        Assert.Equal(1, ended);
    }

    // ── a draft left by a crash ─────────────────────────────────────────────────────────────

    private void LeaveADraftBehind()
    {
        var rig = CreateRig();
        UserProfile(rig);
        rig.Draft.Start(StreamingDraft(rig), DraftMode.Trial, 20);
        // the process dies here: no keep, no discard
    }

    [AvaloniaFact]
    public void ADraftLeftByACrash_IsOffered_AndPutBackOnRequest()
    {
        LeaveADraftBehind();
        var rig = CreateRig();

        Assert.Equal("MY MIC", rig.Canvas.Keys[0].CustomTitle);       // the deck comes back as the user left it
        rig.Draft.OfferPending();

        Assert.True(rig.Draft.IsVisible);
        Assert.True(rig.Draft.IsPendingOffer);
        Assert.Contains("OBS", rig.Draft.Title, StringComparison.OrdinalIgnoreCase);

        rig.Draft.PutBackCommand.Execute(null);

        Assert.True(rig.Draft.IsActive);
        Assert.False(rig.Draft.IsPendingOffer);
        Assert.True(rig.Canvas.IsDryRun);
        Assert.Contains(rig.Canvas.Keys, k => k.AssignedAction == DeckActionType.ObsToggleStream);
        Assert.False(rig.Draft.HasCountdown);                          // a trial that survived a crash does not run out by itself
    }

    [AvaloniaFact]
    public void NotNow_ForgetsTheLeftDraft_AndUseThisKeepsIt()
    {
        LeaveADraftBehind();
        var notNow = CreateRig();
        notNow.Draft.OfferPending();
        notNow.Draft.NotNowCommand.Execute(null);
        Assert.False(notNow.Draft.IsVisible);
        Assert.Null(CreateRig().Profiles.PendingDraft);

        LeaveADraftBehind();
        var useThis = CreateRig();
        useThis.Draft.OfferPending();
        useThis.Draft.UseThisCommand.Execute(null);

        Assert.False(useThis.Draft.IsActive);
        Assert.Equal(2, useThis.Profiles.GetAllProfiles().Count);
        Assert.Contains(useThis.Canvas.Keys, k => k.AssignedAction == DeckActionType.ObsToggleStream);
    }

    // ── the template try-out ────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TryingATemplate_BuildsADraftWithoutSavingAProfile()
    {
        var rig = CreateRig();
        var picker = new TemplatePickerViewModel(rig.Templates);
        ProfileModel? previewed = null;
        picker.PreviewRequested += p => previewed = p;
        picker.OpenCommand.Execute(null);
        picker.Selected = picker.Items.First(i => i.Template.Id == "system-monitor");

        picker.PreviewCommand.Execute(null);

        Assert.NotNull(previewed);
        Assert.Single(rig.Profiles.GetAllProfiles());                  // nothing was saved
        rig.Draft.Start(previewed!, DraftMode.Trial, 20);
        Assert.True(rig.Draft.IsActive);
        Assert.True(rig.Draft.HasCountdown);
    }

    // ── the tour ────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TheTour_IsOffered_OnceOnAnEmptyDeck_AndNeverForced()
    {
        var rig = CreateRig();
        var tour = CreateTour(rig);

        tour.OfferIfFirstRun();
        Assert.True(tour.IsOpen);
        Assert.True(tour.IsWelcome);

        tour.SkipCommand.Execute(null);                              // "not now"
        Assert.False(tour.IsOpen);
        Assert.Equal(TourState.Dismissed, rig.Profiles.GetAppSettings().TourState);

        var again = CreateTour(rig);
        again.OfferIfFirstRun();
        Assert.False(again.IsOpen);                                  // not offered again by itself

        again.Run();                                                 // but Settings can run it
        Assert.True(again.IsOpen);
        Assert.True(again.IsFocus);
    }

    [AvaloniaFact]
    public void TheTour_IsNotOffered_ToSomeoneWhoHasAlreadySetUpTheDeck()
    {
        var rig = CreateRig();
        UserProfile(rig);
        var tour = CreateTour(rig);

        tour.OfferIfFirstRun();

        Assert.False(tour.IsOpen);

        var twoProfiles = CreateRig();
        twoProfiles.Profiles.CreateProfile("Another", setAsActive: false);
        var second = CreateTour(twoProfiles);
        second.OfferIfFirstRun();
        Assert.False(second.IsOpen);
    }

    [AvaloniaFact]
    public void TheTour_SuggestsAnAnswerFromWhatIsRunning_AndTheUserCanPickAnother()
    {
        var rig = CreateRig();
        var tour = CreateTour(rig, "obs64", "chrome");

        tour.Run();
        WaitUntil(() => tour.Foci.Any(f => f.IsSuggested));

        Assert.Equal(TourFocus.Streaming, tour.SelectedFocus!.Focus);
        Assert.Equal(TourFocus.Streaming, tour.Foci.Single(f => f.IsSuggested).Focus);
        Assert.True(tour.HasNoticed);
        Assert.Contains("obs64", tour.NoticedText);

        tour.SelectedFocus = tour.Foci.First(f => f.Focus == TourFocus.Gaming);
        Assert.Equal(TourFocus.Gaming, tour.SelectedFocus.Focus);
    }

    [AvaloniaFact]
    public void WithoutADeck_TheTourSaysSo_AndStillWorks()
    {
        var rig = CreateRig();
        var tour = CreateTour(rig);

        tour.Run();
        WaitUntil(() => tour.Foci.Any(f => f.IsSuggested));

        Assert.Equal(Word("Tour_NoModules", ""), tour.DetectionText);
        Assert.Equal(TourFocus.Desk, tour.SelectedFocus!.Focus);     // nothing to go on: the everyday desk
    }

    [AvaloniaFact]
    public void TheTour_ShowsADraft_ThenKeepsOrDropsEachPart_ThenKeeps()
    {
        var rig = CreateRig();
        UserProfile(rig);
        var tour = CreateTour(rig, "obs64");
        tour.Run();
        WaitUntil(() => tour.Foci.Any(f => f.IsSuggested));

        tour.NextCommand.Execute(null);
        Assert.True(tour.IsLooking);
        tour.BuildCommand.Execute(null);

        Assert.True(tour.IsCompose);
        Assert.True(rig.Draft.IsActive);
        Assert.Single(rig.Profiles.GetAllProfiles());                  // nothing saved yet
        var template = rig.Templates.GetAll().First(t => t.Id == "obs-streaming");
        Assert.Equal(template.Keys.Count, tour.Parts.Count);
        Assert.All(tour.Parts, p => Assert.True(p.Included));

        // drop one part: the deck follows at once
        var dropped = tour.Parts[0];
        dropped.Included = false;
        Assert.Equal(DeckActionType.None, rig.Canvas.Keys[dropped.KeyIndex].AssignedAction);
        Assert.True(rig.Draft.IsActive);

        dropped.Included = true;                                       // and back
        Assert.NotEqual(DeckActionType.None, rig.Canvas.Keys[dropped.KeyIndex].AssignedAction);

        tour.Parts[1].Included = false;
        int droppedIndex = tour.Parts[1].KeyIndex;
        tour.KeepCommand.Execute(null);

        Assert.False(tour.IsOpen);
        Assert.Equal(TourState.Done, rig.Profiles.GetAppSettings().TourState);
        Assert.Equal(2, rig.Profiles.GetAllProfiles().Count);
        var kept = rig.Profiles.GetActiveProfile();
        Assert.DoesNotContain(kept.Keys, k => k.Index == droppedIndex && k.Action != DeckActionType.None);   // the dropped part is not in it
        Assert.Contains(kept.Keys, k => k.Action == DeckActionType.ObsToggleStream);
        Assert.Empty(kept.SmartRules);                                 // nothing is linked behind the user's back
    }

    [AvaloniaFact]
    public void TheTour_CanLinkTheProgramsOfTheProfile_WhenAsked()
    {
        var rig = CreateRig();
        var tour = CreateTour(rig);
        tour.Run();
        WaitUntil(() => tour.Foci.Any(f => f.IsSuggested));
        tour.SelectedFocus = tour.Foci.First(f => f.Focus == TourFocus.Streaming);
        tour.NextCommand.Execute(null);
        tour.BuildCommand.Execute(null);
        tour.LinkApps = true;

        tour.KeepCommand.Execute(null);

        var kept = rig.Profiles.GetActiveProfile();
        var exes = rig.Templates.GetAll().First(t => t.Id == "obs-streaming").Exes;
        Assert.NotEmpty(exes);
        Assert.Equal(exes.OrderBy(e => e).ToArray(), kept.SmartRules.Select(r => r.ProcessName).OrderBy(e => e).ToArray());
    }

    [AvaloniaFact]
    public void Back_FromTheDraft_ThrowsItAwayWithoutClosingTheTour()
    {
        var rig = CreateRig();
        var mine = UserProfile(rig);
        var tour = CreateTour(rig);
        tour.Run();
        WaitUntil(() => tour.Foci.Any(f => f.IsSuggested));
        tour.NextCommand.Execute(null);
        tour.BuildCommand.Execute(null);

        tour.BackCommand.Execute(null);

        Assert.True(tour.IsOpen);
        Assert.True(tour.IsLooking);
        Assert.False(rig.Draft.IsActive);
        Assert.Equal("MY MIC", rig.Canvas.Keys[0].CustomTitle);
        Assert.Equal(mine.Id, rig.Profiles.GetActiveProfile().Id);
    }

    [AvaloniaFact]
    public void Cancel_ThrowsTheDraftAway_AndTheUsersDeckIsExactlyAsItWas()
    {
        var rig = CreateRig();
        var mine = UserProfile(rig);
        var tour = CreateTour(rig);
        tour.Run();
        WaitUntil(() => tour.Foci.Any(f => f.IsSuggested));
        tour.NextCommand.Execute(null);
        tour.BuildCommand.Execute(null);

        tour.CancelCommand.Execute(null);

        Assert.False(tour.IsOpen);
        Assert.False(rig.Draft.IsActive);
        Assert.Equal(TourState.Dismissed, rig.Profiles.GetAppSettings().TourState);
        Assert.Single(rig.Profiles.GetAllProfiles());
        Assert.Equal(DeckActionType.MicMute, rig.Canvas.Keys[0].AssignedAction);
        Assert.Equal(mine.Id, rig.Profiles.GetActiveProfile().Id);
    }

    [AvaloniaFact]
    public void ChoosingAProfile_WhileTheTourShowsADraft_ClosesTheTour()
    {
        var rig = CreateRig();
        var other = rig.Profiles.CreateProfile("Other", setAsActive: false);
        var tour = CreateTour(rig);
        tour.Run();
        WaitUntil(() => tour.Foci.Any(f => f.IsSuggested));
        tour.NextCommand.Execute(null);
        tour.BuildCommand.Execute(null);

        rig.Profiles.SetActiveProfile(other.Id);

        Assert.False(tour.IsOpen);
        Assert.False(rig.Draft.IsActive);
    }

    [AvaloniaFact]
    public void AutoSwitching_IsSkippedWhileADraftIsOnTheDeck()
    {
        var rig = CreateRig();
        var other = rig.Profiles.CreateProfile("Other", setAsActive: false);
        var smart = new FakeSmart();
        var page = new ProfilesPageViewModel(rig.Profiles, null, smart);
        rig.Draft.Start(StreamingDraft(rig), DraftMode.Preview);

        smart.Raise(other.Id);
        Dispatcher.UIThread.RunJobs();

        Assert.True(rig.Draft.IsActive);                              // the draft was not thrown away by the auto-switch
        _ = page;
    }

    private sealed class FakeSmart : ISmartProfileService
    {
        public bool IsEnabled { get; set; }
        public int PollingIntervalMs { get; set; }
        public string? FallbackProfileId { get; set; }
        public string LastForegroundProcess => "";
        public string LastForegroundTitle => "";
        public event Action<string, string>? ForegroundWindowChanged { add { } remove { } }
        public event Action<string>? AutoSwitchProfileRequested;
        public event Action<bool>? IsEnabledChanged { add { } remove { } }
        public void UpdateRules(IEnumerable<SmartProfileRule> rules) { }
        public void Start() { }
        public void Stop() { }
        public void CheckForegroundWindowNow() { }
        public void Raise(string id) => AutoSwitchProfileRequested?.Invoke(id);
        public void Dispose() { }
    }

    // ── what it looks like ──────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Views_Render()
    {
        var rig = CreateRig();
        rig.Draft.Start(StreamingDraft(rig), DraftMode.Trial, 20);
        rig.Canvas.ExecuteKeyAction(rig.Canvas.Keys.First(k => k.AssignedAction == DeckActionType.ObsToggleStream));
        Assert.True(File.Exists(Shot.Render(new DraftBannerView { DataContext = rig.Draft }, "draft_banner_trial", 760, 130)));
        rig.Draft.Discard();

        LeaveADraftBehind();
        var pending = CreateRig();
        pending.Draft.OfferPending();
        Assert.True(File.Exists(Shot.Render(new DraftBannerView { DataContext = pending.Draft }, "draft_banner_pending", 760, 110)));

        var tour = CreateTour(rig, "obs64");
        tour.Run();
        WaitUntil(() => tour.Foci.Any(f => f.IsSuggested));
        Assert.True(File.Exists(Shot.Render(new TourView { DataContext = tour }, "tour_question", 470, 460)));
        tour.NextCommand.Execute(null);
        Assert.True(File.Exists(Shot.Render(new TourView { DataContext = tour }, "tour_looking", 470, 300)));
        tour.BuildCommand.Execute(null);
        Assert.True(File.Exists(Shot.Render(new TourView { DataContext = tour }, "tour_compose", 470, 560)));
    }
}
