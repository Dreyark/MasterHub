using MasterHub.Core.Actions;

namespace MasterHub.Avalonia.ViewModels.Canvas;

public partial class CanvasViewModel
{
    /// <summary>Applies the user's gesture timing (Settings) to the key engine: Hold time and the Double-tap window.</summary>
    public void ApplyGestureTimes(int holdMs, int doubleMs)
    {
        _keyInteractionEngine.HoldDurationMs = System.Math.Clamp(holdMs, 200, 3000);
        _keyInteractionEngine.DoublePressIntervalMs = System.Math.Clamp(doubleMs, 100, 1000);
    }

    private void OnGestureTriggered(int keyIndex, KeyGesture gesture)
    {
        if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            RunGesture(keyIndex, gesture);
        }
        else
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => RunGesture(keyIndex, gesture));
        }
    }

    /// <summary>Runs what a recognised gesture of a key does: the tap is the key's own action, Hold and Double are its extra bindings.</summary>
    internal void RunGesture(int keyIndex, KeyGesture gesture)
    {
        if (keyIndex < 0 || keyIndex >= Keys.Count) return;
        var key = Keys[keyIndex];
        CountPress(key);   // T-37: counted only when the user turned counting on
        EndHoldProgress(key);   // T-03: a recognised gesture ends the hold bar

        switch (gesture)
        {
            case KeyGesture.Press:
                ExecuteKeyAction(key);
                break;
            case KeyGesture.Hold:
                if (TryRunBuiltInTimerGesture(key, gesture)) break;
                ExecuteGestureBinding(key, key.HoldAction, key.HoldParameter, confirmedByHold: true);
                break;
            case KeyGesture.Double:
                if (TryRunBuiltInTimerGesture(key, gesture)) break;
                ExecuteGestureBinding(key, key.DoubleAction, key.DoubleParameter, confirmedByHold: false);
                break;
        }
    }

    /// <summary>
    /// Runs a Hold / Double binding through the normal action dispatcher by wrapping it in a throw-away key (the same
    /// technique as conditional actions). A destructive Hold was held for <c>PowerActionGuard.HoldToConfirmMs</c> (the
    /// engine was told so), which already is the confirmation; a destructive Double still asks.
    /// </summary>
    private void ExecuteGestureBinding(KeyItemViewModel key, DeckActionType action, string parameter, bool confirmedByHold)
    {
        if (action == DeckActionType.None) return;

        using var temp = new KeyItemViewModel
        {
            Index = key.Index,
            AssignedAction = action,
            ActionParameter = parameter,
            SkipPowerConfirmation = key.SkipPowerConfirmation,
            IsActiveToggleState = key.IsActiveToggleState
        };

        if (confirmedByHold && PowerActionGuard.IsDestructive(action) && !key.SkipPowerConfirmation) _holdConfirmedKey = temp;
        try
        {
            ExecuteKeyAction(temp);
        }
        finally
        {
            _holdConfirmedKey = null;
        }

        if (temp.IsActiveToggleState != key.IsActiveToggleState)
        {
            key.IsActiveToggleState = temp.IsActiveToggleState;
            RenderKeyVisual(key, sendToHardware: true);
        }
    }
}
