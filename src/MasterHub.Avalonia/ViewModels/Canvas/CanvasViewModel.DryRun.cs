using System;
using MasterHub.Core.Actions;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>What a blocked press says to the user while a draft is on the deck.</summary>
/// <param name="KeyNumber">1-based key number (0 = not a key: a knob, a slider, the dial).</param>
/// <param name="ActionName">The action that would have run.</param>
public readonly record struct DryRunNotice(int KeyNumber, string ActionName);

/// <summary>
/// Dry-run (T-08): while a draft is shown (the setup tour, a template try-out, a draft put back after a crash) the controls
/// <b>do not run anything</b>. An action that goes live, records, mutes the microphone or changes the volume would change the
/// user's real setup before they decided to keep the draft, so it only says what it would have done. Looking around the draft
/// (pages, folders) and timers, which touch nothing outside the deck, still work.
/// </summary>
public partial class CanvasViewModel
{
    /// <summary>True while a draft is on the deck.</summary>
    public bool IsDryRun { get; set; }

    /// <summary>Raised (on the UI thread or the thread of the press) when a press or a turn was blocked by the dry-run.</summary>
    public event Action<DryRunNotice>? DryRunBlocked;

    /// <summary>What may still run in a dry-run: moving around inside the draft, and timers (they only show time on the key).</summary>
    internal static bool IsAllowedInDryRun(DeckActionType action) => action is
        DeckActionType.GoToPage or DeckActionType.OpenFolder or DeckActionType.GoBack or DeckActionType.GoHome or
        DeckActionType.NextPage or DeckActionType.PreviousPage or
        DeckActionType.TimerCountdown or DeckActionType.TimerPomodoro or DeckActionType.TimerStopwatch or
        DeckActionType.TimerStartPause or DeckActionType.TimerReset or DeckActionType.TimerAddMinute or DeckActionType.TimerSplit;

    /// <summary>Returns <c>true</c> (and says so) when this key's action must not run now.</summary>
    private bool BlockedByDryRun(KeyItemViewModel key)
    {
        if (!IsDryRun || IsAllowedInDryRun(key.AssignedAction)) return false;

        bool isRealKey = key.Index >= 0 && key.Index < Keys.Count && ReferenceEquals(Keys[key.Index], key);
        DryRunBlocked?.Invoke(new DryRunNotice(isRealKey ? key.Index + 1 : 0, key.ActionDisplayName));
        return true;
    }

    /// <summary>Knobs, sliders, rollers and the dial turn volumes and other live settings: in a dry-run they only say so.</summary>
    private bool BlockedByDryRun(string what)
    {
        if (!IsDryRun) return false;
        DryRunBlocked?.Invoke(new DryRunNotice(0, what));
        return true;
    }
}
