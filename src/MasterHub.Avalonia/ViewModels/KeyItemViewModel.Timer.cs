using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MasterHub.Core.Actions;
using MasterHub.Core.Timers;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>Edits the <see cref="TimerSettings"/> of one timer key (the inspector section binds to it).</summary>
public sealed partial class TimerEditor : ObservableObject
{
    private readonly KeyItemViewModel _key;

    public TimerEditor(KeyItemViewModel key) => _key = key;

    private TimerSettings Model => _key.TimerSettings;

    // the countdown length is edited as hours / minutes / seconds, the way people think of it
    public int Hours
    {
        get => Model.TotalSeconds / 3600;
        set => SetTotal(Math.Clamp(value, 0, 99) * 3600 + Minutes * 60 + Seconds);
    }

    public int Minutes
    {
        get => Model.TotalSeconds % 3600 / 60;
        set => SetTotal(Hours * 3600 + Math.Clamp(value, 0, 59) * 60 + Seconds);
    }

    public int Seconds
    {
        get => Model.TotalSeconds % 60;
        set => SetTotal(Hours * 3600 + Minutes * 60 + Math.Clamp(value, 0, 59));
    }

    private void SetTotal(int seconds)
    {
        Model.TotalSeconds = Math.Clamp(seconds, 1, TimerSettings.MaxSeconds);   // a countdown of zero would ring at once
        Touch(nameof(Hours), nameof(Minutes), nameof(Seconds));
    }

    public int WorkMinutes { get => Model.WorkMinutes; set { Model.WorkMinutes = Math.Clamp(value, 1, 600); Touch(nameof(WorkMinutes)); } }
    public int BreakMinutes { get => Model.BreakMinutes; set { Model.BreakMinutes = Math.Clamp(value, 1, 600); Touch(nameof(BreakMinutes)); } }
    public int LongBreakMinutes { get => Model.LongBreakMinutes; set { Model.LongBreakMinutes = Math.Clamp(value, 1, 600); Touch(nameof(LongBreakMinutes)); } }
    public int Rounds { get => Model.RoundsBeforeLongBreak; set { Model.RoundsBeforeLongBreak = Math.Clamp(value, 1, 20); Touch(nameof(Rounds)); } }

    public bool AutoStartNextPhase { get => Model.AutoStartNextPhase; set { Model.AutoStartNextPhase = value; Touch(nameof(AutoStartNextPhase)); } }
    /// <summary>Sound played when the time is up (empty = silent).</summary>
    public string EndSoundPath { get => Model.EndSoundPath; set { Model.EndSoundPath = value?.Trim() ?? ""; Touch(nameof(EndSoundPath)); } }

    public bool BlinkOnExpiry { get => Model.BlinkOnExpiry; set { Model.BlinkOnExpiry = value; Touch(nameof(BlinkOnExpiry)); } }

    private void Touch(params string[] names)
    {
        foreach (string n in names) OnPropertyChanged(n);
        _key.NotifyTimerSettingsChanged();
    }
}

/// <summary>Timer keys (T-13): countdown, Pomodoro, stopwatch. The timers themselves live in <see cref="ITimerService"/>; the key only keeps how it is set up.</summary>
public partial class KeyItemViewModel
{
    private TimerEditor? _timerEditor;

    /// <summary>The key's timer set-up (always present; only saved for a timer key).</summary>
    public TimerSettings TimerSettings { get; private set; } = new();

    public TimerEditor TimerEditor => _timerEditor ??= new TimerEditor(this);

    public bool IsTimerAction => AssignedAction is DeckActionType.TimerCountdown or DeckActionType.TimerPomodoro or DeckActionType.TimerStopwatch;
    public bool IsCountdownTimer => AssignedAction == DeckActionType.TimerCountdown;
    public bool IsPomodoroTimer => AssignedAction == DeckActionType.TimerPomodoro;
    public bool IsStopwatchTimer => AssignedAction == DeckActionType.TimerStopwatch;

    public TimerKind? TimerKindOrNull => AssignedAction switch
    {
        DeckActionType.TimerCountdown => TimerKind.Countdown,
        DeckActionType.TimerPomodoro => TimerKind.Pomodoro,
        DeckActionType.TimerStopwatch => TimerKind.Stopwatch,
        _ => null
    };

    /// <summary>What the timer shows now (not saved): the Pomodoro phase or the last split. Replaces the default title only; a title the user typed stays.</summary>
    public string? TimerTitle { get; set; }

    /// <summary>Background of the current Pomodoro phase / of a finished timer while it blinks (not saved).</summary>
    public string? TimerBackground { get; set; }

    /// <summary>The title a timer wants drawn, or <c>null</c> to keep the key's own.</summary>
    public string? TimerTitleOverride =>
        IsTimerAction && !string.IsNullOrEmpty(TimerTitle) && (string.IsNullOrEmpty(CustomTitle) || CustomTitle == GetOriginalActionTitle())
            ? TimerTitle
            : null;

    internal void NotifyTimerSettingsChanged()
    {
        OnPropertyChanged(nameof(TimerSettings));
        ActionChangedCallback?.Invoke(this);
    }

    private void NotifyTimerKindChanged()
    {
        OnPropertyChanged(nameof(IsTimerAction));
        OnPropertyChanged(nameof(IsCountdownTimer));
        OnPropertyChanged(nameof(IsPomodoroTimer));
        OnPropertyChanged(nameof(IsStopwatchTimer));
    }

    public void LoadTimer(TimerSettings? model)
    {
        TimerSettings = (model ?? new TimerSettings()).Normalize();
        _timerEditor = null;
        OnPropertyChanged(nameof(TimerSettings));
        OnPropertyChanged(nameof(TimerEditor));
    }

    /// <summary>The set-up to save, or <c>null</c> for a key that is not a timer key.</summary>
    public TimerSettings? ToTimerModel() => IsTimerAction ? TimerSettings : null;

    private void ResetTimer()
    {
        TimerSettings = new TimerSettings();
        _timerEditor = null;
        TimerTitle = null;
        TimerBackground = null;
    }
}
