using System;
using System.Collections.Generic;
using Avalonia.Threading;
using MasterHub.Core.Actions;
using MasterHub.Core.Localization;
using MasterHub.Core.Timers;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>
/// Timer keys (T-13). The timers live in <see cref="ITimerService"/>, keyed by profile + page/folder + key, so a timer keeps
/// running while its page is not on the deck. The clock tick runs only while some timer runs or blinks.
/// </summary>
public partial class CanvasViewModel
{
    private static readonly TimeSpan TimerInterval = TimeSpan.FromMilliseconds(500);

    private readonly ITimerService _timers;
    private DispatcherTimer? _timerTick;

    /// <summary>Colours of the Pomodoro phases; a finished timer flashes <see cref="TimerAlertColor"/>.</summary>
    internal const string TimerWorkColor = "#9A3412";
    internal const string TimerShortBreakColor = "#166534";
    internal const string TimerLongBreakColor = "#1D4ED8";
    internal const string TimerAlertColor = "#B3261E";

    /// <summary>The panel that lists running timers (with Stop). Bound in the main window.</summary>
    public ActiveTimersViewModel ActiveTimers { get; private set; } = null!;

    /// <summary>A countdown / Pomodoro phase ran out. Meant for the soundboard (T-14) and the base light (T-18).</summary>
    public event Action<TimerKey, TimerExpiry>? TimerRang;

    private void InitializeTimers()
    {
        ActiveTimers = new ActiveTimersViewModel(_timers, StopTimer);
        _timers.Expired += OnTimerExpired;
    }

    private TimerKey TimerKeyOf(int keyIndex)
    {
        string profile = _profileService.GetActiveProfile()?.Id ?? "";
        string place = _currentFolderId != null ? "f:" + _currentFolderId : "p:" + _currentPageIndex;
        return new TimerKey(profile, place, keyIndex);
    }

    private string TimerCaption(KeyItemViewModel key)
    {
        string profile = _profileService.GetActiveProfile()?.Name ?? "";
        string title = !string.IsNullOrWhiteSpace(key.CustomTitle) ? key.CustomTitle : key.ActionDisplayName;
        return string.IsNullOrEmpty(profile) ? $"{title} ({key.KeyNumberDisplay})" : $"{profile} · {title} ({key.KeyNumberDisplay})";
    }

    // ── commands ────────────────────────────────────────────────────────────────────────────

    /// <summary>The timer actions: the key itself (press = start/pause) and the commands a Hold / Double can be bound to.</summary>
    private bool TryExecuteTimerAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            case DeckActionType.TimerCountdown:
            case DeckActionType.TimerPomodoro:
            case DeckActionType.TimerStopwatch:
            case DeckActionType.TimerStartPause:
                RunTimerCommand(key, TimerCommand.StartPause);
                return true;
            case DeckActionType.TimerReset:
                RunTimerCommand(key, TimerCommand.Reset);
                return true;
            case DeckActionType.TimerAddMinute:
                RunTimerCommand(key, TimerCommand.AddMinute);
                return true;
            case DeckActionType.TimerSplit:
                RunTimerCommand(key, TimerCommand.Split);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Press, Hold and Double of one key reach the <b>same</b> timer: a gesture binding runs on a throw-away copy that has the key's index.</summary>
    internal void RunTimerCommand(KeyItemViewModel source, TimerCommand command)
    {
        var key = source.Index >= 0 && source.Index < Keys.Count ? Keys[source.Index] : source;
        if (key.TimerKindOrNull is not { } kind) return;   // the key is no timer: the command has nothing to act on

        var snapshot = _timers.Apply(TimerKeyOf(key.Index), kind, key.TimerSettings, TimerCaption(key), command);
        if (ApplyTimerSnapshot(key, snapshot)) RenderKeyVisual(key, sendToHardware: true);

        EnsureTimerTick();
        ActiveTimers.Refresh();
    }

    /// <summary>What a timer key does on Hold / Double when the user bound nothing else: reset, and +1 minute (a split on a stopwatch).</summary>
    private bool TryRunBuiltInTimerGesture(KeyItemViewModel key, KeyGesture gesture)
    {
        if (!key.IsTimerAction) return false;

        if (gesture == KeyGesture.Hold && key.HoldAction == DeckActionType.None)
        {
            RunTimerCommand(key, TimerCommand.Reset);
            return true;
        }

        if (gesture == KeyGesture.Double && key.DoubleAction == DeckActionType.None)
        {
            RunTimerCommand(key, key.IsStopwatchTimer ? TimerCommand.Split : TimerCommand.AddMinute);
            return true;
        }

        return false;
    }

    /// <summary>A knob / dial step on a countdown key: one minute per detent while it is not running.</summary>
    internal bool AdjustTimer(int keyIndex, int detents)
    {
        if (keyIndex < 0 || keyIndex >= Keys.Count) return false;
        var key = Keys[keyIndex];
        if (key.TimerKindOrNull is not { } kind) return false;

        bool changed = _timers.Adjust(TimerKeyOf(keyIndex), kind, key.TimerSettings, detents);
        if (changed && ApplyTimerSnapshot(key, _timers.Snapshot(TimerKeyOf(keyIndex), kind, key.TimerSettings)))
            RenderKeyVisual(key, sendToHardware: true);
        return changed;
    }

    private void StopTimer(TimerKey timer)
    {
        _timers.Stop(timer);
        RefreshTimerKeys(render: true);
        ActiveTimers.Refresh();
    }

    // ── what the keys show ──────────────────────────────────────────────────────────────────

    /// <summary>Puts the timer's state on the key (text, title, colour). <c>true</c> = the picture has to be redrawn.</summary>
    private bool ApplyTimerSnapshot(KeyItemViewModel key, TimerSnapshot snapshot)
    {
        string text = snapshot.Text;
        string? title = TimerTitleFor(snapshot);
        string? background = TimerBackgroundFor(snapshot);

        bool changed = key.LiveValueText != text || key.TimerTitle != title || key.TimerBackground != background;
        key.LiveValueText = text;
        key.TimerTitle = title;
        key.TimerBackground = background;
        return changed;
    }

    private static string? TimerTitleFor(TimerSnapshot s)
    {
        var loc = LocalizationService.Instance;
        switch (s.Kind)
        {
            case TimerKind.Pomodoro:
                string phase = s.Phase switch
                {
                    PomodoroPhase.Work => loc.GetString("Timer_PhaseWork", "WORK"),
                    PomodoroPhase.ShortBreak => loc.GetString("Timer_PhaseBreak", "BREAK"),
                    _ => loc.GetString("Timer_PhaseLongBreak", "LONG BREAK")
                };
                return s.Phase == PomodoroPhase.Work ? $"{phase} {s.Round}/{s.RoundsBeforeLongBreak}" : phase;

            case TimerKind.Stopwatch when s.LastLap is { } lap:
                return $"L{lap.Number} {TimerText.Format(lap.Total)}";

            case TimerKind.Countdown when s.State == TimerState.Finished:
                return loc.GetString("Timer_Done", "DONE");

            default:
                return null;
        }
    }

    private static string? TimerBackgroundFor(TimerSnapshot s)
    {
        if (s.State == TimerState.Finished && s.AlertOn) return TimerAlertColor;   // blinks: red, then the normal colour again

        return s.Kind == TimerKind.Pomodoro
            ? s.Phase switch
            {
                PomodoroPhase.Work => TimerWorkColor,
                PomodoroPhase.ShortBreak => TimerShortBreakColor,
                _ => TimerLongBreakColor
            }
            : null;
    }

    /// <summary>Brings the timer keys of the view that is on the deck in line with their timers (after a page / profile change or an edit).</summary>
    private void RefreshTimerKeys(bool render)
    {
        foreach (var key in Keys)
        {
            if (RefreshTimerKey(key) && render) RenderKeyVisual(key, sendToHardware: true);
        }
    }

    /// <summary>One key: follows its timer, or drops what an earlier timer put on it. <c>true</c> = changed.</summary>
    private bool RefreshTimerKey(KeyItemViewModel key)
    {
        if (key.TimerKindOrNull is not { } kind)
        {
            bool had = key.TimerTitle != null || key.TimerBackground != null;
            key.TimerTitle = null;
            key.TimerBackground = null;
            return had;
        }

        var id = TimerKeyOf(key.Index);
        _timers.Reconfigure(id, kind, key.TimerSettings);   // an unstarted timer follows the edited length
        return ApplyTimerSnapshot(key, _timers.Snapshot(id, kind, key.TimerSettings));
    }

    // ── clock ───────────────────────────────────────────────────────────────────────────────

    internal bool IsTimerTickRunning => _timerTick?.IsEnabled == true;

    private void EnsureTimerTick()
    {
        if (_disposed || !_timers.NeedsTick) return;

        _timerTick ??= CreateTimerTick();
        if (!_timerTick.IsEnabled) _timerTick.Start();
    }

    private DispatcherTimer CreateTimerTick()
    {
        var timer = new DispatcherTimer { Interval = TimerInterval };
        timer.Tick += (_, _) => OnTimerTick();
        return timer;
    }

    /// <summary>One step: only the keys whose picture changed are drawn again (once per second while running).</summary>
    internal void OnTimerTick()
    {
        if (_disposed) return;

        var changed = new HashSet<TimerKey>(_timers.Tick());
        if (changed.Count > 0 && !_deviceService.IsStandby)
        {
            foreach (var key in Keys)
            {
                if (key.TimerKindOrNull is not { } kind) continue;
                var id = TimerKeyOf(key.Index);
                if (!changed.Contains(id)) continue;

                if (ApplyTimerSnapshot(key, _timers.Snapshot(id, kind, key.TimerSettings))) RenderKeyVisual(key, sendToHardware: true);
            }
        }

        ActiveTimers.Refresh();
        if (!_timers.NeedsTick) _timerTick?.Stop();   // nothing runs or blinks: no more wake-ups
    }

    private void OnTimerExpired(TimerKey key, TimerExpiry expiry)
    {
        TimerRang?.Invoke(key, expiry);
        PlayTimerEndSound(expiry.EndSoundPath);   // T-14: the optional sound of the key
        EnsureTimerTick();   // a finished timer blinks, so the clock keeps going a while
    }

    private void DisposeTimers()
    {
        _timerTick?.Stop();
        _timerTick = null;
        _timers.Expired -= OnTimerExpired;
    }
}
