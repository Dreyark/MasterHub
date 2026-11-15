using System;
using System.Collections.Generic;
using System.Globalization;

namespace MasterHub.Core.Timers;

public enum TimerKind
{
    Countdown = 0,
    Stopwatch = 1,
    Pomodoro = 2
}

public enum PomodoroPhase
{
    Work = 0,
    ShortBreak = 1,
    LongBreak = 2
}

/// <summary>What a gesture can tell a timer. The press, hold and double-press of one key all reach the <b>same</b> timer.</summary>
public enum TimerCommand
{
    StartPause = 0,
    Reset = 1,
    /// <summary>Countdown / Pomodoro: one more minute (on a finished countdown: a one-minute snooze).</summary>
    AddMinute = 2,
    /// <summary>Stopwatch: remember the current time as a split.</summary>
    Split = 3
}

public enum TimerState
{
    /// <summary>Never started since the last reset (the key shows the configured time).</summary>
    Idle = 0,
    Running = 1,
    Paused = 2,
    /// <summary>A countdown / Pomodoro phase ran out and nobody has acknowledged it yet (the key blinks).</summary>
    Finished = 3
}

/// <summary>What a timer key is set up to do (saved with the key).</summary>
public class TimerSettings
{
    /// <summary>The longest time the official app allows too: 99 h 59 min 59 s.</summary>
    public const int MaxSeconds = 99 * 3600 + 59 * 60 + 59;

    /// <summary>Countdown length.</summary>
    public int TotalSeconds { get; set; } = 5 * 60;

    public int WorkMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;

    /// <summary>A long break replaces the short one after this many work sessions.</summary>
    public int RoundsBeforeLongBreak { get; set; } = 4;

    /// <summary>Pomodoro: the next phase starts by itself instead of waiting for a press.</summary>
    public bool AutoStartNextPhase { get; set; }

    /// <summary>A sound file played when a countdown / phase runs out (empty = silent). Not packed into profile packages.</summary>
    public string EndSoundPath { get; set; } = "";

    /// <summary>The key blinks red when a countdown / phase runs out.</summary>
    public bool BlinkOnExpiry { get; set; } = true;

    /// <summary>Brings every value into its allowed range (a profile edited by hand must not break a timer).</summary>
    public TimerSettings Normalize()
    {
        TotalSeconds = Math.Clamp(TotalSeconds, 1, MaxSeconds);
        WorkMinutes = Math.Clamp(WorkMinutes, 1, 600);
        BreakMinutes = Math.Clamp(BreakMinutes, 1, 600);
        LongBreakMinutes = Math.Clamp(LongBreakMinutes, 1, 600);
        RoundsBeforeLongBreak = Math.Clamp(RoundsBeforeLongBreak, 1, 20);
        EndSoundPath ??= "";
        return this;
    }

    public TimerSettings Clone() => (TimerSettings)MemberwiseClone();

    public TimeSpan PhaseLength(PomodoroPhase phase) => TimeSpan.FromMinutes(phase switch
    {
        PomodoroPhase.Work => WorkMinutes,
        PomodoroPhase.ShortBreak => BreakMinutes,
        _ => LongBreakMinutes
    });
}

/// <summary>One countdown / Pomodoro phase ran out.</summary>
public readonly record struct TimerExpiry(TimerKind Kind, PomodoroPhase EndedPhase, string EndSoundPath = "");

/// <summary>A stopwatch split.</summary>
public readonly record struct TimerLap(int Number, TimeSpan Total, TimeSpan Lap);

/// <summary>Everything a key needs to draw a timer.</summary>
public sealed record TimerSnapshot(
    TimerKind Kind,
    TimerState State,
    TimeSpan Value,
    string Text,
    PomodoroPhase Phase,
    int Round,
    int RoundsBeforeLongBreak,
    TimerLap? LastLap,
    int LapCount,
    bool AlertOn)
{
    /// <summary>What the key redraws on: it is only drawn again when this changes (the key engine redraws only when its text changes).</summary>
    public string Signature => $"{State}|{Text}|{Phase}|{Round}|{LapCount}|{AlertOn}";
}

public static class TimerText
{
    /// <summary><c>MM:SS</c>, or <c>H:MM:SS</c> from one hour on.</summary>
    public static string Format(TimeSpan value)
    {
        long total = (long)Math.Max(0, Math.Floor(value.TotalSeconds));
        long h = total / 3600, m = total % 3600 / 60, s = total % 60;
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{m:00}:{s:00}");
    }
}

/// <summary>
/// The pure state of one timer. It never reads a clock: every call is given a monotonic <c>now</c>, so it can be tested
/// without waiting and nothing in it touches the hardware or the UI.
/// </summary>
public sealed class DeckTimer
{
    /// <summary>After this long a finished timer stops blinking and stays red (a key that is never answered must not redraw forever).</summary>
    public static readonly TimeSpan BlinkLimit = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private const int MaxLaps = 50;

    private TimerSettings _settings;
    private readonly List<TimerLap> _laps = new();

    private TimeSpan _total;                 // length of the current countdown / phase
    private TimeSpan _stored;                // time left (countdown) or elapsed (stopwatch) while not running
    private TimeSpan? _since;                // when it started running
    private bool _started;
    private bool _finished;
    private TimeSpan _finishedAt;
    private int _workDone;                   // Pomodoro: finished work sessions
    private PomodoroPhase _phase;

    public DeckTimer(TimerKind kind, TimerSettings settings)
    {
        Kind = kind;
        _settings = (settings ?? new TimerSettings()).Clone().Normalize();
        ResetState();
    }

    public TimerKind Kind { get; }
    public TimerSettings Settings => _settings;
    public bool IsRunning => _since != null;
    public bool IsFinished => _finished;

    /// <summary>A timer someone started and not yet put back to its initial state.</summary>
    public bool IsIdle => !_started && !_finished;

    // ── state ───────────────────────────────────────────────────────────────────────────────

    private void ResetState()
    {
        _since = null;
        _started = false;
        _finished = false;
        _workDone = 0;
        _phase = PomodoroPhase.Work;
        _laps.Clear();
        _total = Kind switch
        {
            TimerKind.Countdown => TimeSpan.FromSeconds(_settings.TotalSeconds),
            TimerKind.Pomodoro => _settings.PhaseLength(PomodoroPhase.Work),
            _ => TimeSpan.Zero
        };
        _stored = Kind == TimerKind.Stopwatch ? TimeSpan.Zero : _total;
    }

    /// <summary>Time left (countdown, Pomodoro) or elapsed (stopwatch).</summary>
    public TimeSpan Value(TimeSpan now)
    {
        if (Kind == TimerKind.Stopwatch)
        {
            var elapsed = _stored + (_since is { } s ? now - s : TimeSpan.Zero);
            return Min(Max(elapsed, TimeSpan.Zero), TimeSpan.FromSeconds(TimerSettings.MaxSeconds));
        }

        var left = _since is { } since ? _stored - (now - since) : _stored;
        return Max(left, TimeSpan.Zero);
    }

    /// <summary>Running, or blinking after its end (plus a moment so the last picture is the steady red). Anything else needs no clock tick.</summary>
    public bool WantsTick(TimeSpan now) =>
        _since != null || (_finished && _settings.BlinkOnExpiry && now - _finishedAt < BlinkLimit + TimeSpan.FromSeconds(2));

    public TimerState State(TimeSpan now) =>
        _since != null ? TimerState.Running : _finished ? TimerState.Finished : _started ? TimerState.Paused : TimerState.Idle;

    // ── time passing ────────────────────────────────────────────────────────────────────────

    /// <summary>Notices that a running countdown ran out. The expiry is reported <b>once</b> per run.</summary>
    public TimerExpiry? Poll(TimeSpan now)
    {
        if (_since == null) return null;

        if (Kind == TimerKind.Stopwatch)
        {
            if (Value(now) >= TimeSpan.FromSeconds(TimerSettings.MaxSeconds))
            {
                _stored = TimeSpan.FromSeconds(TimerSettings.MaxSeconds);   // the stopwatch stops at its limit
                _since = null;
            }
            return null;
        }

        if (Value(now) > TimeSpan.Zero) return null;

        var ended = _phase;
        _since = null;
        _finished = true;
        _finishedAt = now;

        if (Kind == TimerKind.Pomodoro)
        {
            AdvancePhase();                                   // the key already shows what comes next
            if (_settings.AutoStartNextPhase)
            {
                _finished = false;
                _since = now;
            }
        }
        else
        {
            _stored = TimeSpan.Zero;
        }

        return new TimerExpiry(Kind, ended, _settings.EndSoundPath);
    }

    private void AdvancePhase()
    {
        if (_phase == PomodoroPhase.Work)
        {
            _workDone++;
            _phase = _workDone % _settings.RoundsBeforeLongBreak == 0 ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        }
        else
        {
            _phase = PomodoroPhase.Work;
        }

        _total = _settings.PhaseLength(_phase);
        _stored = _total;
    }

    // ── commands ────────────────────────────────────────────────────────────────────────────

    /// <summary>Runs a command. <c>true</c> = the timer changed (a command that does not apply to this kind changes nothing).</summary>
    public bool Apply(TimerCommand command, TimeSpan now)
    {
        Poll(now);

        return command switch
        {
            TimerCommand.StartPause => StartPause(now),
            TimerCommand.Reset => Reset(),
            TimerCommand.AddMinute => AddMinute(now),
            TimerCommand.Split => Split(now),
            _ => false
        };
    }

    private bool StartPause(TimeSpan now)
    {
        if (_since != null)                       // running → pause
        {
            _stored = Value(now);
            _since = null;
            return true;
        }

        if (_finished)
        {
            _finished = false;
            if (Kind == TimerKind.Countdown)      // "start" on a finished countdown puts it back to the full time (it does not start)
            {
                _started = false;
                _stored = _total;
                return true;
            }
            // Pomodoro: the next phase begins now
        }

        if (Kind != TimerKind.Stopwatch && _stored <= TimeSpan.Zero) _stored = _total;
        _started = true;
        _since = now;
        return true;
    }

    private bool Reset()
    {
        bool changed = _started || _finished || _since != null || _laps.Count > 0 || _phase != PomodoroPhase.Work || _workDone != 0;
        ResetState();
        return changed;
    }

    private bool AddMinute(TimeSpan now)
    {
        if (Kind == TimerKind.Stopwatch) return false;

        if (_finished)
        {
            if (Kind == TimerKind.Pomodoro) return false;   // the phase is over and the next one waits: nothing to extend
            _finished = false;                              // countdown: a one-minute snooze that starts at once
            _started = true;
            _stored = Minute;
            _since = now;
            return true;
        }

        var left = Value(now) + Minute;
        left = Min(left, TimeSpan.FromSeconds(TimerSettings.MaxSeconds));
        if (_since != null) _since = now;
        _stored = left;
        _started = true;
        return true;
    }

    private bool Split(TimeSpan now)
    {
        if (Kind != TimerKind.Stopwatch) return false;

        var elapsed = Value(now);
        if (!_started || elapsed <= TimeSpan.Zero) return false;

        var previous = _laps.Count > 0 ? _laps[^1].Total : TimeSpan.Zero;
        _laps.Add(new TimerLap(_laps.Count + 1, elapsed, elapsed - previous));
        if (_laps.Count > MaxLaps) _laps.RemoveAt(0);
        return true;
    }

    /// <summary>
    /// A knob / dial step: one minute each, only on a countdown that is not running. The change lives in this timer only
    /// (a reset goes back to the length set on the key).
    /// </summary>
    public bool Adjust(int detents, TimeSpan now)
    {
        if (Kind != TimerKind.Countdown || _since != null || _finished || detents == 0) return false;

        var length = _started ? _stored : _total;
        length += TimeSpan.FromMinutes(detents);
        length = Min(Max(length, TimeSpan.FromSeconds(10)), TimeSpan.FromSeconds(TimerSettings.MaxSeconds));

        if (!_started) _total = length;
        _stored = length;
        return true;
    }

    /// <summary>The key was edited. A timer nobody started follows the new settings at once; a running one keeps its progress.</summary>
    public void Reconfigure(TimerSettings settings)
    {
        _settings = (settings ?? new TimerSettings()).Clone().Normalize();
        if (IsIdle) ResetState();
    }

    // ── what to draw ────────────────────────────────────────────────────────────────────────

    public TimerSnapshot Snapshot(TimeSpan now)
    {
        var value = Value(now);
        var state = State(now);

        // a countdown shows whole seconds rounded up, so it never reads 00:00 while it still runs
        var shown = Kind == TimerKind.Stopwatch ? value : TimeSpan.FromSeconds(Math.Ceiling(value.TotalSeconds));
        bool blink = state == TimerState.Finished && _settings.BlinkOnExpiry &&
                     (now - _finishedAt >= BlinkLimit || (long)Math.Floor((now - _finishedAt).TotalSeconds) % 2 == 0);

        int rounds = _settings.RoundsBeforeLongBreak;
        int round = _phase == PomodoroPhase.Work
            ? _workDone % rounds + 1
            : (_workDone - 1) % rounds + 1;

        return new TimerSnapshot(Kind, state, value, TimerText.Format(shown), _phase, Kind == TimerKind.Pomodoro ? round : 0, rounds,
            _laps.Count > 0 ? _laps[^1] : null, _laps.Count, blink);
    }

    public IReadOnlyList<TimerLap> Laps => _laps;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
