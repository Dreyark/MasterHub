using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Localization;
using MasterHub.Core.Timers;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>One line of the "running timers" panel.</summary>
public sealed partial class ActiveTimerItemViewModel : ObservableObject
{
    public ActiveTimerItemViewModel(TimerKey key, Action<TimerKey> stop)
    {
        Key = key;
        _stop = stop;
    }

    private readonly Action<TimerKey> _stop;

    public TimerKey Key { get; }

    [ObservableProperty] private string _caption = "";
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private bool _isAlert;

    [RelayCommand]
    private void Stop() => _stop(Key);
}

/// <summary>
/// The panel that lists the timers that run, wait part-way or have rung – also those on other pages and profiles –
/// each with a Stop button. It is empty (and hidden) when no timer is active.
/// </summary>
public sealed partial class ActiveTimersViewModel : ObservableObject
{
    private readonly ITimerService _timers;
    private readonly Action<TimerKey> _stop;

    public ActiveTimersViewModel(ITimerService timers, Action<TimerKey> stop)
    {
        _timers = timers;
        _stop = stop;
    }

    public ObservableCollection<ActiveTimerItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _hasItems;

    /// <summary>Brings the list in line with the service (called on every clock tick while something is active).</summary>
    public void Refresh()
    {
        var active = _timers.Active;
        var loc = LocalizationService.Instance;

        // drop the lines whose timer is gone
        foreach (var gone in Items.Where(i => active.All(a => a.Key != i.Key)).ToList()) Items.Remove(gone);

        foreach (var timer in active)
        {
            var item = Items.FirstOrDefault(i => i.Key == timer.Key);
            if (item == null)
            {
                item = new ActiveTimerItemViewModel(timer.Key, _stop);
                Items.Add(item);
            }

            item.Caption = timer.Caption;
            item.Text = timer.Snapshot.Text;
            item.IsAlert = timer.Snapshot.State == TimerState.Finished;
            item.StateText = timer.Snapshot.State switch
            {
                TimerState.Running => loc.GetString("Timer_StateRunning", "running"),
                TimerState.Paused => loc.GetString("Timer_StatePaused", "paused"),
                TimerState.Finished => loc.GetString("Timer_StateDone", "done"),
                _ => ""
            };
        }

        HasItems = Items.Count > 0;
    }
}
