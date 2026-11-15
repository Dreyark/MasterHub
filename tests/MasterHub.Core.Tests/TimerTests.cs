using MasterHub.Core.Timers;

namespace MasterHub.Core.Tests;

/// <summary>T-13: the timer state machine and the service, on a fake monotonic clock (nothing waits).</summary>
public class DeckTimerTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static DeckTimer Countdown(int seconds = 300) => new(TimerKind.Countdown, new TimerSettings { TotalSeconds = seconds });

    // ── countdown ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Countdown_NotStarted_ShowsTheConfiguredTime()
    {
        var t = Countdown();
        var snap = t.Snapshot(S(0));

        Assert.Equal(TimerState.Idle, snap.State);
        Assert.Equal("05:00", snap.Text);
        Assert.True(t.IsIdle);
    }

    [Fact]
    public void Countdown_Runs_AndCountsDown()
    {
        var t = Countdown();
        t.Apply(TimerCommand.StartPause, S(10));

        Assert.Equal(TimerState.Running, t.State(S(10)));
        Assert.Equal("04:59", t.Snapshot(S(11)).Text);
        Assert.Equal("03:59", t.Snapshot(S(71)).Text);
        Assert.Null(t.Poll(S(71)));
    }

    [Fact]
    public void Countdown_ShowsWholeSecondsRoundedUp_NeverZeroWhileRunning()
    {
        var t = Countdown(10);
        t.Apply(TimerCommand.StartPause, S(0));

        Assert.Equal("00:01", t.Snapshot(S(9.6)).Text);
        Assert.Equal(TimerState.Running, t.State(S(9.6)));
    }

    [Fact]
    public void Countdown_PauseKeepsTheTime_AndResumeContinues()
    {
        var t = Countdown();
        t.Apply(TimerCommand.StartPause, S(0));
        t.Apply(TimerCommand.StartPause, S(60));                // pause at 04:00

        Assert.Equal(TimerState.Paused, t.State(S(60)));
        Assert.Equal("04:00", t.Snapshot(S(500)).Text);          // time passes, the paused timer does not

        t.Apply(TimerCommand.StartPause, S(500));                // resume
        Assert.Equal("03:50", t.Snapshot(S(510)).Text);
    }

    [Fact]
    public void Countdown_Expiry_IsReportedOnce_AndTheTimerFinishes()
    {
        var t = Countdown(60);
        t.Apply(TimerCommand.StartPause, S(0));

        Assert.Null(t.Poll(S(59)));
        var expiry = t.Poll(S(60));
        Assert.NotNull(expiry);
        Assert.Equal(TimerKind.Countdown, expiry!.Value.Kind);
        Assert.Null(t.Poll(S(61)));                              // the end sound / flash happens once per run

        var snap = t.Snapshot(S(61));
        Assert.Equal(TimerState.Finished, snap.State);
        Assert.Equal("00:00", snap.Text);
        Assert.False(t.IsRunning);
    }

    [Fact]
    public void Countdown_StartOnAFinishedTimer_PutsItBackToTheFullTime_WithoutStarting()
    {
        var t = Countdown(60);
        t.Apply(TimerCommand.StartPause, S(0));
        t.Poll(S(60));

        t.Apply(TimerCommand.StartPause, S(70));

        var snap = t.Snapshot(S(70));
        Assert.Equal(TimerState.Idle, snap.State);
        Assert.Equal("01:00", snap.Text);
        Assert.False(t.IsRunning);

        // and it can run again, reporting its end again
        t.Apply(TimerCommand.StartPause, S(80));
        Assert.NotNull(t.Poll(S(140)));
    }

    [Fact]
    public void Countdown_AddMinute_ExtendsARunningOne_AndSnoozesAFinishedOne()
    {
        var t = Countdown(60);
        t.Apply(TimerCommand.StartPause, S(0));
        t.Apply(TimerCommand.AddMinute, S(10));                  // 50 s left + 60 s

        Assert.Equal("01:50", t.Snapshot(S(10)).Text);
        Assert.Equal(TimerState.Running, t.State(S(10)));

        var finished = Countdown(30);
        finished.Apply(TimerCommand.StartPause, S(0));
        finished.Poll(S(30));
        finished.Apply(TimerCommand.AddMinute, S(35));           // snooze: one more minute, running

        Assert.Equal(TimerState.Running, finished.State(S(35)));
        Assert.Equal("01:00", finished.Snapshot(S(35)).Text);
        Assert.NotNull(finished.Poll(S(95)));                    // and it rings again
    }

    [Fact]
    public void Countdown_Reset_GoesBackToTheStart()
    {
        var t = Countdown();
        t.Apply(TimerCommand.StartPause, S(0));
        Assert.True(t.Apply(TimerCommand.Reset, S(30)));

        Assert.Equal(TimerState.Idle, t.State(S(30)));
        Assert.Equal("05:00", t.Snapshot(S(30)).Text);
        Assert.False(t.Apply(TimerCommand.Reset, S(31)));         // nothing to reset: no change
    }

    [Fact]
    public void Countdown_Adjust_IsOneMinutePerDetent_OnlyWhileNotRunning()
    {
        var t = Countdown();

        Assert.True(t.Adjust(2, S(0)));
        Assert.Equal("07:00", t.Snapshot(S(0)).Text);
        Assert.True(t.Adjust(-9, S(0)));                          // never below 10 seconds
        Assert.Equal("00:10", t.Snapshot(S(0)).Text);

        t.Apply(TimerCommand.StartPause, S(0));
        Assert.False(t.Adjust(1, S(1)));                          // running: the knob does nothing

        t.Apply(TimerCommand.Reset, S(2));
        Assert.Equal("05:00", t.Snapshot(S(2)).Text);             // a reset goes back to the length set on the key
    }

    [Fact]
    public void Countdown_IsCappedAt_99h59m59s()
    {
        var t = Countdown(TimerSettings.MaxSeconds + 1000);

        Assert.Equal("99:59:59", t.Snapshot(S(0)).Text);

        t.Apply(TimerCommand.StartPause, S(0));
        t.Apply(TimerCommand.AddMinute, S(1));
        Assert.Equal("99:59:59", t.Snapshot(S(1)).Text);          // the extra minute cannot go over the limit
    }

    [Fact]
    public void Settings_AreNormalised_AHandEditedProfileCannotBreakATimer()
    {
        var t = new DeckTimer(TimerKind.Pomodoro, new TimerSettings { TotalSeconds = -5, WorkMinutes = 0, BreakMinutes = -1, LongBreakMinutes = 99999, RoundsBeforeLongBreak = 0 });

        Assert.Equal(1, t.Settings.WorkMinutes);
        Assert.Equal(1, t.Settings.RoundsBeforeLongBreak);
        Assert.Equal(600, t.Settings.LongBreakMinutes);
        Assert.Equal("01:00", t.Snapshot(S(0)).Text);
    }

    // ── stopwatch ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Stopwatch_CountsUp_PausesAndResumes()
    {
        var t = new DeckTimer(TimerKind.Stopwatch, new TimerSettings());
        Assert.Equal("00:00", t.Snapshot(S(0)).Text);

        t.Apply(TimerCommand.StartPause, S(100));
        Assert.Equal("01:05", t.Snapshot(S(165)).Text);

        t.Apply(TimerCommand.StartPause, S(165));                 // pause
        Assert.Equal("01:05", t.Snapshot(S(900)).Text);

        t.Apply(TimerCommand.StartPause, S(900));                 // resume
        Assert.Equal("01:15", t.Snapshot(S(910)).Text);
    }

    [Fact]
    public void Stopwatch_ShowsHours_FromOneHourOn()
    {
        var t = new DeckTimer(TimerKind.Stopwatch, new TimerSettings());
        t.Apply(TimerCommand.StartPause, S(0));

        Assert.Equal("1:01:05", t.Snapshot(S(3665)).Text);
    }

    [Fact]
    public void Stopwatch_Splits_AreNumbered_WithTheTimeSinceThePreviousOne()
    {
        var t = new DeckTimer(TimerKind.Stopwatch, new TimerSettings());
        Assert.False(t.Apply(TimerCommand.Split, S(0)));          // nothing to split before it started

        t.Apply(TimerCommand.StartPause, S(0));
        Assert.True(t.Apply(TimerCommand.Split, S(30)));
        Assert.True(t.Apply(TimerCommand.Split, S(75)));

        var snap = t.Snapshot(S(75));
        Assert.Equal(2, snap.LapCount);
        Assert.Equal(2, snap.LastLap!.Value.Number);
        Assert.Equal(S(75), snap.LastLap.Value.Total);
        Assert.Equal(S(45), snap.LastLap.Value.Lap);

        t.Apply(TimerCommand.Reset, S(80));
        Assert.Equal(0, t.Snapshot(S(80)).LapCount);
    }

    [Fact]
    public void Stopwatch_IgnoresAddMinute_AndCountdownIgnoresSplit()
    {
        var sw = new DeckTimer(TimerKind.Stopwatch, new TimerSettings());
        sw.Apply(TimerCommand.StartPause, S(0));
        Assert.False(sw.Apply(TimerCommand.AddMinute, S(5)));

        var cd = Countdown();
        cd.Apply(TimerCommand.StartPause, S(0));
        Assert.False(cd.Apply(TimerCommand.Split, S(5)));
    }

    // ── Pomodoro ────────────────────────────────────────────────────────────────────────────

    private static DeckTimer Pomodoro(bool auto = false) =>
        new(TimerKind.Pomodoro, new TimerSettings { WorkMinutes = 25, BreakMinutes = 5, LongBreakMinutes = 15, RoundsBeforeLongBreak = 4, AutoStartNextPhase = auto });

    [Fact]
    public void Pomodoro_WorkThenShortBreak_ThenWorkAgain_AndAPressStartsEachPhase()
    {
        var t = Pomodoro();
        var now = S(0);

        var first = t.Snapshot(now);
        Assert.Equal(PomodoroPhase.Work, first.Phase);
        Assert.Equal(1, first.Round);
        Assert.Equal("25:00", first.Text);

        t.Apply(TimerCommand.StartPause, now);
        now = S(25 * 60);
        var ended = t.Poll(now);
        Assert.Equal(PomodoroPhase.Work, ended!.Value.EndedPhase);

        var waiting = t.Snapshot(now);                            // the key already shows what comes next and blinks
        Assert.Equal(TimerState.Finished, waiting.State);
        Assert.Equal(PomodoroPhase.ShortBreak, waiting.Phase);
        Assert.Equal("05:00", waiting.Text);

        t.Apply(TimerCommand.StartPause, now);                    // a press starts the break at once
        Assert.Equal(TimerState.Running, t.State(now));

        now += S(5 * 60);
        Assert.Equal(PomodoroPhase.ShortBreak, t.Poll(now)!.Value.EndedPhase);
        var next = t.Snapshot(now);
        Assert.Equal(PomodoroPhase.Work, next.Phase);
        Assert.Equal(2, next.Round);
    }

    [Fact]
    public void Pomodoro_LongBreak_ComesAfterTheFourthWorkSession()
    {
        var t = Pomodoro(auto: true);
        var now = S(0);
        t.Apply(TimerCommand.StartPause, now);

        var phases = new List<PomodoroPhase>();
        for (int i = 0; i < 8; i++)                                // 4 × (work + break)
        {
            var len = t.Snapshot(now).Phase == PomodoroPhase.Work ? 25 : t.Snapshot(now).Phase == PomodoroPhase.LongBreak ? 15 : 5;
            now += S(len * 60);
            var expiry = t.Poll(now);
            Assert.NotNull(expiry);
            phases.Add(expiry!.Value.EndedPhase);
        }

        Assert.Equal(new[]
        {
            PomodoroPhase.Work, PomodoroPhase.ShortBreak, PomodoroPhase.Work, PomodoroPhase.ShortBreak,
            PomodoroPhase.Work, PomodoroPhase.ShortBreak, PomodoroPhase.Work, PomodoroPhase.LongBreak
        }, phases);

        // after the long break a new cycle starts at round 1
        var snap = t.Snapshot(now);
        Assert.Equal(PomodoroPhase.Work, snap.Phase);
        Assert.Equal(1, snap.Round);
    }

    [Fact]
    public void Pomodoro_AutoStart_RunsTheNextPhaseWithoutAPress_ButStillReportsTheEnd()
    {
        var t = Pomodoro(auto: true);
        t.Apply(TimerCommand.StartPause, S(0));

        Assert.NotNull(t.Poll(S(25 * 60)));
        Assert.Equal(TimerState.Running, t.State(S(25 * 60)));
        Assert.Equal(PomodoroPhase.ShortBreak, t.Snapshot(S(25 * 60)).Phase);
    }

    [Fact]
    public void Pomodoro_Reset_GoesBackToTheFirstWorkSession()
    {
        var t = Pomodoro();
        t.Apply(TimerCommand.StartPause, S(0));
        t.Poll(S(25 * 60));
        t.Apply(TimerCommand.StartPause, S(25 * 60));

        t.Apply(TimerCommand.Reset, S(26 * 60));

        var snap = t.Snapshot(S(26 * 60));
        Assert.Equal(PomodoroPhase.Work, snap.Phase);
        Assert.Equal(1, snap.Round);
        Assert.Equal(TimerState.Idle, snap.State);
    }

    // ── blinking & reconfiguring ────────────────────────────────────────────────────────────

    [Fact]
    public void FinishedTimer_BlinksAtMostOncePerSecond_AndStopsAfterFiveMinutes()
    {
        var t = Countdown(10);
        t.Apply(TimerCommand.StartPause, S(0));
        t.Poll(S(10));

        Assert.True(t.Snapshot(S(10.2)).AlertOn);
        Assert.False(t.Snapshot(S(11.2)).AlertOn);
        Assert.True(t.Snapshot(S(12.2)).AlertOn);

        Assert.True(t.WantsTick(S(60)));
        Assert.True(t.Snapshot(S(10 + 301)).AlertOn);              // steady red after the blink limit
        Assert.False(t.WantsTick(S(10 + 400)));                    // and no more redraws
    }

    [Fact]
    public void BlinkCanBeSwitchedOff()
    {
        var t = new DeckTimer(TimerKind.Countdown, new TimerSettings { TotalSeconds = 5, BlinkOnExpiry = false });
        t.Apply(TimerCommand.StartPause, S(0));
        t.Poll(S(5));

        Assert.Equal(TimerState.Finished, t.State(S(5)));
        Assert.False(t.Snapshot(S(5)).AlertOn);
        Assert.False(t.WantsTick(S(6)));
    }

    [Fact]
    public void Reconfigure_FollowsTheNewSettings_OnlyBeforeTheTimerWasStarted()
    {
        var idle = Countdown(300);
        idle.Reconfigure(new TimerSettings { TotalSeconds = 600 });
        Assert.Equal("10:00", idle.Snapshot(S(0)).Text);

        var running = Countdown(300);
        running.Apply(TimerCommand.StartPause, S(0));
        running.Reconfigure(new TimerSettings { TotalSeconds = 600 });
        Assert.Equal("04:00", running.Snapshot(S(60)).Text);       // progress is kept

        running.Apply(TimerCommand.Reset, S(61));
        Assert.Equal("10:00", running.Snapshot(S(61)).Text);       // the next run uses the new length
    }

    [Fact]
    public void Signature_ChangesWithTheShownText_NotWithTheSubSecond()
    {
        var t = Countdown();
        t.Apply(TimerCommand.StartPause, S(0));

        Assert.Equal(t.Snapshot(S(1.1)).Signature, t.Snapshot(S(1.9)).Signature);
        Assert.NotEqual(t.Snapshot(S(1.9)).Signature, t.Snapshot(S(2.1)).Signature);
    }
}

public class TimerServiceTests
{
    private sealed class Clock
    {
        public TimeSpan Now { get; set; }
        public void Advance(double seconds) => Now += TimeSpan.FromSeconds(seconds);
    }

    private static (TimerService Service, Clock Clock) Create()
    {
        var clock = new Clock();
        return (new TimerService(() => clock.Now), clock);
    }

    private static readonly TimerSettings Five = new() { TotalSeconds = 5 };
    private static TimerKey Key(string profile = "p1", string place = "0", int index = 0) => new(profile, place, index);

    [Fact]
    public void TheSameKey_OnTwoProfilesOrPages_IsTwoIndependentTimers()
    {
        var (service, clock) = Create();

        service.Apply(Key("p1"), TimerKind.Countdown, Five, "A", TimerCommand.StartPause);
        clock.Advance(2);

        Assert.Equal(TimerState.Running, service.Snapshot(Key("p1"), TimerKind.Countdown, Five).State);
        Assert.Equal(TimerState.Idle, service.Snapshot(Key("p2"), TimerKind.Countdown, Five).State);
        Assert.Equal(TimerState.Idle, service.Snapshot(Key("p1", place: "1"), TimerKind.Countdown, Five).State);
        Assert.Equal("00:03", service.Snapshot(Key("p1"), TimerKind.Countdown, Five).Text);
    }

    [Fact]
    public void ARunningTimer_KeepsRunning_WhileItsKeyIsNotOnScreen()
    {
        var (service, clock) = Create();
        service.Apply(Key(), TimerKind.Countdown, new TimerSettings { TotalSeconds = 60 }, "A", TimerCommand.StartPause);

        clock.Advance(45);                                          // the user is on another page, nobody asks about the key

        Assert.Equal("00:15", service.Snapshot(Key(), TimerKind.Countdown, new TimerSettings { TotalSeconds = 60 }).Text);
    }

    [Fact]
    public void Tick_ReturnsAKeyOnlyWhenItsPictureChanges()
    {
        var (service, clock) = Create();
        service.Apply(Key(), TimerKind.Countdown, new TimerSettings { TotalSeconds = 60 }, "A", TimerCommand.StartPause);

        Assert.Empty(service.Tick());                               // nothing moved yet
        clock.Advance(0.4);
        Assert.Empty(service.Tick());                               // still the same whole second
        clock.Advance(1);
        Assert.Equal(new[] { Key() }, service.Tick());
        Assert.Empty(service.Tick());
    }

    [Fact]
    public void Expiry_IsRaisedOnce_AndTheFinishedKeyKeepsBlinking()
    {
        var (service, clock) = Create();
        var expired = new List<(TimerKey, TimerExpiry)>();
        service.Expired += (k, e) => expired.Add((k, e));
        service.Apply(Key(), TimerKind.Countdown, Five, "A", TimerCommand.StartPause);

        clock.Advance(5.5);
        var changed = service.Tick();
        service.Tick();
        clock.Advance(1);
        var blink = service.Tick();

        Assert.Single(expired);
        Assert.Equal(Key(), expired[0].Item1);
        Assert.Contains(Key(), changed);
        Assert.Contains(Key(), blink);                              // the blink toggles the picture
        Assert.True(service.NeedsTick);
    }

    [Fact]
    public void ExpiryNoticedByAPress_IsReportedToo()
    {
        var (service, clock) = Create();
        int rings = 0;
        service.Expired += (_, _) => rings++;
        service.Apply(Key(), TimerKind.Countdown, Five, "A", TimerCommand.StartPause);

        clock.Advance(30);                                          // nobody ticked, the user presses a long time later
        var snap = service.Apply(Key(), TimerKind.Countdown, Five, "A", TimerCommand.StartPause);

        Assert.Equal(1, rings);
        Assert.Equal(TimerState.Idle, snap.State);                  // the press put the finished countdown back to the start
    }

    [Fact]
    public void NeedsTick_OnlyWhileSomethingRunsOrBlinks()
    {
        var (service, clock) = Create();
        Assert.False(service.NeedsTick);

        service.Apply(Key(), TimerKind.Countdown, Five, "A", TimerCommand.StartPause);
        Assert.True(service.NeedsTick);

        service.Apply(Key(), TimerKind.Countdown, Five, "A", TimerCommand.StartPause);   // pause
        Assert.False(service.NeedsTick);

        service.Apply(Key(), TimerKind.Countdown, Five, "A", TimerCommand.Reset);
        Assert.False(service.NeedsTick);
        Assert.Empty(service.Active);                                // a reset timer is forgotten
        _ = clock;
    }

    [Fact]
    public void Active_ListsRunningAndPausedTimers_AndStopForgetsThem()
    {
        var (service, clock) = Create();
        service.Apply(Key("p1", index: 1), TimerKind.Countdown, Five, "Tea", TimerCommand.StartPause);
        service.Apply(Key("p1", index: 2), TimerKind.Stopwatch, new TimerSettings(), "Work", TimerCommand.StartPause);
        clock.Advance(1);
        service.Apply(Key("p1", index: 2), TimerKind.Stopwatch, new TimerSettings(), "Work", TimerCommand.StartPause);   // paused
        service.Apply(Key("p1", index: 3), TimerKind.Countdown, Five, "Idle", TimerCommand.Reset);                       // never started

        var active = service.Active;
        Assert.Equal(2, active.Count);
        Assert.Contains(active, a => a.Caption == "Tea" && a.Snapshot.State == TimerState.Running);
        Assert.Contains(active, a => a.Caption == "Work" && a.Snapshot.State == TimerState.Paused);

        service.Stop(Key("p1", index: 1));
        Assert.Single(service.Active);
        Assert.Equal(TimerState.Idle, service.Snapshot(Key("p1", index: 1), TimerKind.Countdown, Five).State);
    }

    [Fact]
    public void ChangingTheKindOfAKey_ReplacesItsTimer()
    {
        var (service, _) = Create();
        service.Apply(Key(), TimerKind.Countdown, Five, "A", TimerCommand.StartPause);

        var snap = service.Apply(Key(), TimerKind.Stopwatch, new TimerSettings(), "A", TimerCommand.StartPause);

        Assert.Equal(TimerKind.Stopwatch, snap.Kind);
        Assert.Single(service.Active);
    }

    [Fact]
    public void Reconfigure_UpdatesAnUnstartedTimer_AndDropsIdleOnes()
    {
        var (service, _) = Create();
        service.Apply(Key(), TimerKind.Countdown, Five, "A", TimerCommand.StartPause);
        service.Reconfigure(Key(), TimerKind.Countdown, new TimerSettings { TotalSeconds = 60 });

        Assert.Equal(TimerState.Running, service.Snapshot(Key(), TimerKind.Countdown, Five).State);   // a running timer is not reset

        service.Reconfigure(Key(), TimerKind.Stopwatch, new TimerSettings());                          // the key became a stopwatch
        Assert.Empty(service.Active);
    }

    [Fact]
    public void Snapshot_OfAKeyWithoutATimer_ShowsTheConfiguredTime_AndCreatesNothing()
    {
        var (service, _) = Create();

        Assert.Equal("00:05", service.Snapshot(Key(), TimerKind.Countdown, Five).Text);
        Assert.Equal("25:00", service.Snapshot(Key(index: 1), TimerKind.Pomodoro, new TimerSettings()).Text);
        Assert.Empty(service.Active);
    }

    [Fact]
    public void Adjust_ChangesAnUnstartedCountdown_AndLeavesNothingBehindWhenItFails()
    {
        var (service, _) = Create();

        Assert.True(service.Adjust(Key(), TimerKind.Countdown, new TimerSettings { TotalSeconds = 300 }, 3));
        Assert.Equal("08:00", service.Snapshot(Key(), TimerKind.Countdown, new TimerSettings { TotalSeconds = 300 }).Text);

        Assert.False(service.Adjust(Key(index: 9), TimerKind.Stopwatch, new TimerSettings(), 1));     // a stopwatch has no knob
        Assert.DoesNotContain(service.Active, a => a.Key.Index == 9);
    }
}
