using System.Text.Json;
using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Avalonia.Views.Controls;
using MasterHub.Avalonia.Views.Inspector;
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
using MasterHub.Core.Media;
using MasterHub.Core.Localization;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Timers;
using MasterHub.Core.Wooting;
using MasterHub.Avalonia.Services;

namespace MasterHub.UiTests;

/// <summary>T-13 in the Studio: the editor, press / hold / double on one timer, independence of pages and the panel of running timers.</summary>
public sealed class TimerKeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_timer_ui_" + Guid.NewGuid().ToString("N"));
    private TimeSpan _now;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    // the app speaks the language of the user (Polish by default), so the tests compare with the localised words
    private static string Word(string key, string fallback) => LocalizationService.Instance.GetString(key, fallback);

    private void Advance(double seconds) => _now += TimeSpan.FromSeconds(seconds);

    private (CanvasViewModel Canvas, TimerService Timers, ProfileService Profiles) Create()
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var timers = new TimerService(() => _now);
        var canvas = new CanvasViewModel(F<IMasterHubDeviceService>(), F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles,
            timers: timers);
        canvas.ApplyProfile(profiles.GetActiveProfile());
        return (canvas, timers, profiles);
    }

    private static KeyItemViewModel Timer(CanvasViewModel canvas, int index, DeckActionType action, int seconds = 60)
    {
        var key = canvas.Keys[index];
        key.AssignedAction = action;
        key.LoadTimer(new TimerSettings { TotalSeconds = seconds });
        return key;
    }

    // ── the editor ──────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Editor_SetsTheCountdownLength_AndKeepsItInTheAllowedRange()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.TimerCountdown };
        int saves = 0;
        key.ActionChangedCallback = _ => saves++;

        var editor = key.TimerEditor;
        editor.Hours = 1;
        editor.Minutes = 30;
        editor.Seconds = 15;
        Assert.Equal(5415, key.TimerSettings.TotalSeconds);

        editor.Hours = 0; editor.Minutes = 0; editor.Seconds = 0;
        Assert.Equal(1, key.TimerSettings.TotalSeconds);           // a countdown of zero would ring at once

        editor.Hours = 500;
        Assert.Equal(99, editor.Hours);
        Assert.True(saves >= 5);
    }

    [AvaloniaFact]
    public void Editor_Pomodoro_ClampsItsValues()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.TimerPomodoro };
        var editor = key.TimerEditor;

        editor.WorkMinutes = 50;
        editor.BreakMinutes = 0;
        editor.Rounds = 99;
        editor.AutoStartNextPhase = true;
        editor.BlinkOnExpiry = false;

        Assert.Equal(50, key.TimerSettings.WorkMinutes);
        Assert.Equal(1, key.TimerSettings.BreakMinutes);
        Assert.Equal(20, key.TimerSettings.RoundsBeforeLongBreak);
        Assert.True(key.TimerSettings.AutoStartNextPhase);
        Assert.False(key.TimerSettings.BlinkOnExpiry);
    }

    [AvaloniaFact]
    public void Settings_AreSavedOnlyForTimerKeys_AndRoundTrip()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.TimerCountdown };
        key.TimerEditor.Minutes = 12;

        var model = new KeyAssignmentModel { Index = 0, Timer = key.ToTimerModel() };
        var back = JsonSerializer.Deserialize<KeyAssignmentModel>(JsonSerializer.Serialize(model))!;

        var loaded = new KeyItemViewModel { Index = 1, AssignedAction = DeckActionType.TimerCountdown };
        loaded.LoadTimer(back.Timer);
        Assert.Equal(720, loaded.TimerSettings.TotalSeconds);

        Assert.Null(new KeyItemViewModel { Index = 2, AssignedAction = DeckActionType.MediaNext }.ToTimerModel());
        Assert.Null(JsonSerializer.Deserialize<KeyAssignmentModel>(JsonSerializer.Serialize(new KeyAssignmentModel { Index = 3 }))!.Timer);
    }

    [AvaloniaFact]
    public void TimerKinds_AreRecognised()
    {
        var key = new KeyItemViewModel { Index = 0 };

        key.AssignedAction = DeckActionType.TimerPomodoro;
        Assert.True(key.IsTimerAction);
        Assert.True(key.IsPomodoroTimer);
        Assert.False(key.IsCountdownTimer);
        Assert.Equal(TimerKind.Pomodoro, key.TimerKindOrNull);
        Assert.True(key.IsMonitoringAction);                      // the key shows a value, like the clock

        key.AssignedAction = DeckActionType.MediaNext;
        Assert.False(key.IsTimerAction);
        Assert.Null(key.TimerKindOrNull);
    }

    [AvaloniaFact]
    public void Inspector_RendersForEveryKind()
    {
        foreach (var (action, name) in new[]
        {
            (DeckActionType.TimerCountdown, "inspector_timer_countdown"),
            (DeckActionType.TimerPomodoro, "inspector_timer_pomodoro"),
            (DeckActionType.TimerStopwatch, "inspector_timer_stopwatch")
        })
        {
            var key = new KeyItemViewModel { Index = 0, AssignedAction = action };
            Assert.True(File.Exists(Shot.Render(new KeyTimerSettingsView { DataContext = key }, name, 340, 300)));
        }
    }

    // ── press / hold / double on one key ────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Press_StartsTheTimer_AndTheKeyCountsDown_OncePerSecond()
    {
        var (canvas, timers, _) = Create();
        var key = Timer(canvas, 2, DeckActionType.TimerCountdown, 60);

        canvas.ExecuteKeyAction(key);
        Assert.Equal("01:00", key.LiveValueText);
        Assert.True(canvas.IsTimerTickRunning);                    // the clock runs while a timer runs
        Assert.Single(timers.Active);

        Advance(10);
        canvas.OnTimerTick();
        Assert.Equal("00:50", key.LiveValueText);

        Advance(0.3);
        canvas.OnTimerTick();
        Assert.Equal("00:50", key.LiveValueText);                  // same second: nothing to redraw

        canvas.ExecuteKeyAction(key);                              // press again: pause
        Advance(30);
        canvas.OnTimerTick();
        Assert.Equal("00:50", key.LiveValueText);                  // paused at 49.7 s → shown rounded up (the same second)
        Assert.False(canvas.IsTimerTickRunning);                   // nothing runs: no more wake-ups
    }

    [AvaloniaFact]
    public void HoldResets_AndDoubleAddsAMinute_WithoutAnySetUp()
    {
        var (canvas, _, _) = Create();
        var key = Timer(canvas, 3, DeckActionType.TimerCountdown, 60);

        Assert.True(key.GestureBindings.Hold);                     // a timer key understands the gestures by itself
        Assert.True(key.GestureBindings.Double);
        Assert.False(new KeyItemViewModel { Index = 9, AssignedAction = DeckActionType.MediaNext }.GestureBindings.Hold);

        canvas.RunGesture(3, KeyGesture.Press);
        Advance(20);
        canvas.RunGesture(3, KeyGesture.Double);                   // 40 s left + 60 s
        Assert.Equal("01:40", key.LiveValueText);

        canvas.RunGesture(3, KeyGesture.Hold);                     // reset: back to the set length, stopped
        Assert.Equal("01:00", key.LiveValueText);
        Assert.False(canvas.ActiveTimers.HasItems);
    }

    [AvaloniaFact]
    public void Stopwatch_DoubleSplits_AndTheKeyShowsTheLastSplit()
    {
        var (canvas, _, _) = Create();
        var key = Timer(canvas, 4, DeckActionType.TimerStopwatch);

        canvas.RunGesture(4, KeyGesture.Press);
        Advance(75);
        canvas.RunGesture(4, KeyGesture.Double);

        Assert.Equal("01:15", key.LiveValueText);
        Assert.Equal("L1 01:15", key.TimerTitle);
    }

    [AvaloniaFact]
    public void AnExplicitHoldBinding_WinsOverTheBuiltInReset()
    {
        var (canvas, _, _) = Create();
        var key = Timer(canvas, 5, DeckActionType.TimerCountdown, 60);
        key.HoldAction = DeckActionType.TimerStartPause;           // Hold now starts / pauses instead of resetting

        canvas.RunGesture(5, KeyGesture.Hold);
        Advance(5);
        canvas.OnTimerTick();

        Assert.Equal("00:55", key.LiveValueText);
    }

    [AvaloniaFact]
    public void ACommandOnAKeyThatIsNoTimer_DoesNothing()
    {
        var (canvas, timers, _) = Create();
        var key = canvas.Keys[6];
        key.AssignedAction = DeckActionType.MediaNext;

        canvas.RunTimerCommand(key, TimerCommand.StartPause);

        Assert.Empty(timers.Active);
    }

    // ── expiry & look ───────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Expiry_RaisesTheEventOnce_AndTheKeyBlinksRed()
    {
        var (canvas, _, _) = Create();
        var key = Timer(canvas, 7, DeckActionType.TimerCountdown, 5);
        var rang = new List<TimerExpiry>();
        canvas.TimerRang += (_, e) => rang.Add(e);

        canvas.RunGesture(7, KeyGesture.Press);
        Advance(5.2);
        canvas.OnTimerTick();

        Assert.Single(rang);
        Assert.Equal("00:00", key.LiveValueText);
        Assert.Equal(Word("Timer_Done", "DONE"), key.TimerTitle);
        Assert.Equal(CanvasViewModel.TimerAlertColor, key.RenderBackgroundHex);   // red phase of the blink

        Advance(1);
        canvas.OnTimerTick();
        Assert.NotEqual(CanvasViewModel.TimerAlertColor, key.RenderBackgroundHex);   // the other phase: normal colour

        canvas.OnTimerTick();
        Assert.Single(rang);                                        // it rang once
    }

    [AvaloniaFact]
    public void Pomodoro_ShowsItsPhase_AndItsColour()
    {
        var (canvas, _, _) = Create();
        var key = canvas.Keys[8];
        key.AssignedAction = DeckActionType.TimerPomodoro;
        key.LoadTimer(new TimerSettings { WorkMinutes = 25, BreakMinutes = 5, AutoStartNextPhase = true });

        canvas.RunGesture(8, KeyGesture.Press);
        Assert.Equal("25:00", key.LiveValueText);
        Assert.Equal(Word("Timer_PhaseWork", "WORK") + " 1/4", key.TimerTitle);
        Assert.Equal(CanvasViewModel.TimerWorkColor, key.RenderBackgroundHex);

        Advance(25 * 60 + 1);
        canvas.OnTimerTick();
        Assert.Equal(Word("Timer_PhaseBreak", "BREAK"), key.TimerTitle);
        Assert.Equal(CanvasViewModel.TimerShortBreakColor, key.RenderBackgroundHex);
    }

    [AvaloniaFact]
    public void ATitleTheUserTyped_IsNeverReplacedByThePhase()
    {
        var (canvas, _, _) = Create();
        var key = canvas.Keys[9];
        key.AssignedAction = DeckActionType.TimerPomodoro;
        key.CustomTitle = "Focus";
        key.LoadTimer(new TimerSettings());

        canvas.RunGesture(9, KeyGesture.Press);

        Assert.Equal(Word("Timer_PhaseWork", "WORK") + " 1/4", key.TimerTitle);
        Assert.Null(key.TimerTitleOverride);                       // the key draws "Focus", not the phase
    }

    // ── independence & the panel ────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TheSameKeyOnAnotherPage_IsAnotherTimer_AndTheFirstKeepsRunning()
    {
        var (canvas, timers, _) = Create();
        var key = Timer(canvas, 0, DeckActionType.TimerCountdown, 60);
        canvas.RunGesture(0, KeyGesture.Press);                    // runs on page 1

        canvas.AddNewPage();                                       // page 2, its key 0 is empty
        Assert.Equal(DeckActionType.None, canvas.Keys[0].AssignedAction);
        Timer(canvas, 0, DeckActionType.TimerCountdown, 600);
        canvas.RunGesture(0, KeyGesture.Press);

        Assert.Equal(2, timers.Active.Count);                      // two timers, same key number

        Advance(30);
        canvas.OnTimerTick();
        Assert.Equal("09:30", canvas.Keys[0].LiveValueText);       // page 2

        canvas.SwitchToPage(0);
        Assert.Equal("00:30", canvas.Keys[0].LiveValueText);       // page 1 ran on in the background
    }

    [AvaloniaFact]
    public void Panel_ListsRunningTimers_AndStopForgetsOne()
    {
        var (canvas, timers, _) = Create();
        var key = Timer(canvas, 1, DeckActionType.TimerCountdown, 90);
        canvas.RunGesture(1, KeyGesture.Press);

        var panel = canvas.ActiveTimers;
        Assert.True(panel.HasItems);
        var item = panel.Items.Single();
        Assert.Equal("01:30", item.Text);
        Assert.False(item.IsAlert);
        Assert.Contains("(", item.Caption);                       // names the key

        Assert.True(File.Exists(Shot.Render(new ActiveTimersView { DataContext = panel }, "panel_active_timers", 420, 160)));

        item.StopCommand.Execute(null);

        Assert.False(panel.HasItems);
        Assert.Empty(timers.Active);
        Assert.Equal("01:30", key.LiveValueText);                 // the key is back to its start
    }

    [AvaloniaFact]
    public void EditingAnUnstartedTimer_ChangesWhatTheKeyShowsAtOnce()
    {
        var (canvas, _, _) = Create();
        var key = Timer(canvas, 2, DeckActionType.TimerCountdown, 60);
        canvas.UpdateMonitoringDemand();
        Assert.Equal("01:00", key.LiveValueText);                  // an idle timer shows its length

        key.TimerEditor.Minutes = 5;
        Assert.Equal("05:00", key.LiveValueText);
    }

    [AvaloniaFact]
    public void TheTickStops_WhenTheCanvasIsDisposed()
    {
        var (canvas, _, _) = Create();
        Timer(canvas, 0, DeckActionType.TimerCountdown, 60);
        canvas.RunGesture(0, KeyGesture.Press);
        Assert.True(canvas.IsTimerTickRunning);

        canvas.Dispose();

        Assert.False(canvas.IsTimerTickRunning);
    }
}
