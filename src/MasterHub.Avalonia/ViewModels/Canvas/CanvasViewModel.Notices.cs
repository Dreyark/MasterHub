using MasterHub.Core.Actions;
using MasterHub.Core.Localization;
using MasterHub.Core.Notices;
using MasterHub.Core.SystemControl;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>
/// Banners in the window (T-34 #8) raised by keys: an action that failed (with its reason), and the elevated-app warning
/// (T-34 #12) when key presses would be dropped by Windows because the program in front runs as administrator.
/// </summary>
public partial class CanvasViewModel
{
    /// <summary>Where banners go (the app's shared center; tests replace it).</summary>
    internal INoticeCenter Notices { get; set; } = NoticeCenter.Shared;

    /// <summary>Looks at the program in front (tests replace it).</summary>
    internal IElevationProbe ElevationProbe { get; set; } = new WindowsElevationProbe();

    /// <summary>
    /// Before a key types or presses keys: when the program in front runs as administrator and MasterHub does not, Windows
    /// silently drops the presses – say so. The keys are still sent.
    /// </summary>
    private void WarnIfKeystrokesWillBeDropped(KeyItemViewModel key)
    {
        if (!ElevatedInputGuard.SendsKeystrokes(key.AssignedAction)) return;
        try
        {
            var foreground = ElevationProbe.GetForeground();
            if (!ElevatedInputGuard.InputWillBeDropped(ElevationProbe.IsSelfElevated, foreground)) return;

            string text = string.Format(
                LocalizationService.Instance.GetString("Notice_ElevatedTarget",
                    "\"{0}\" runs as administrator, so Windows ignores key presses from MasterHub. Run MasterHub as administrator too, or start {0} normally."),
                foreground!.ProcessName);
            Notices.Post(NoticeLevel.Warning, text, "elevated:" + foreground.ProcessName.ToLowerInvariant());
            System.Diagnostics.Debug.WriteLine($"[keys] {foreground.ProcessName} (pid {foreground.ProcessId}) is elevated: synthetic input is dropped by UIPI");
        }
        catch
        {
            // the warning is a help, never a reason for the key not to work
        }
    }

    /// <summary>A failed action also shows a banner with its reason (one per key; repeats are merged).</summary>
    private void PostFailureNotice(KeyItemViewModel key, ActionResult result)
    {
        if (!result.IsFailure) return;
        string text = string.Format(
            LocalizationService.Instance.GetString("Notice_ActionFailed", "Key {0}: the action did not work – {1}"),
            key.Index + 1, result.Reason);
        Notices.Post(NoticeLevel.Error, text, "fail:" + key.Index);
    }
}
