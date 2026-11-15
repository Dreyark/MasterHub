using MasterHub.Core.Actions;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>Result of the last press (T-07): a key whose last press failed shows a red corner until a press succeeds. Not saved.</summary>
public partial class KeyItemViewModel
{
    private bool _lastActionFailed;
    private string _lastActionError = "";

    public bool LastActionFailed
    {
        get => _lastActionFailed;
        private set => SetField(ref _lastActionFailed, value);
    }

    /// <summary>Why the last press failed (shown in the inspector, written to the log); empty when it did not.</summary>
    public string LastActionError
    {
        get => _lastActionError;
        private set => SetField(ref _lastActionError, value);
    }

    /// <summary>Applies an action result. Returns <c>true</c> when the failed/ok state changed (the key has to be redrawn).</summary>
    public bool ApplyActionResult(ActionResult result)
    {
        if (result.Outcome == ActionOutcome.NotApplicable) return false;

        bool failed = result.IsFailure;
        string reason = failed ? result.Reason ?? "" : "";
        bool changed = failed != LastActionFailed || reason != LastActionError;

        LastActionFailed = failed;
        LastActionError = reason;
        return changed;
    }

    /// <summary>Forgets a previous failure (a profile or page was loaded: the mark belongs to a profile and a control).</summary>
    public void ClearActionResult()
    {
        LastActionFailed = false;
        LastActionError = "";
    }
}
