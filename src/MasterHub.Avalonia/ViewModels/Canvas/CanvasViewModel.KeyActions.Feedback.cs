using System;
using System.Threading.Tasks;
using MasterHub.Core.Actions;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>Reports whether a key's action worked (T-07): integrations return a task; its outcome marks or clears the red corner.</summary>
public partial class CanvasViewModel
{
    /// <summary>
    /// Watches an integration call that was started for <paramref name="key"/>. A <c>false</c> result or an exception marks the
    /// key as failed (red corner, reason logged); a success clears the mark. Other result types count as success.
    /// </summary>
    private void Track<T>(KeyItemViewModel key, Task<T> task)
    {
        _ = task.ContinueWith(t =>
        {
            ActionResult result;
            if (t.IsFaulted) result = ActionResult.Failed(t.Exception?.GetBaseException().Message ?? "failed");
            else if (t.IsCanceled) return;
            else if (t.Result is bool ok) result = ActionResult.FromBool(ok, "the service did not accept the command (offline or rejected)");
            else result = ActionResult.Ok;

            ReportActionResult(key, result);
        }, TaskScheduler.Default);
    }

    private void Track(KeyItemViewModel key, Task task)
    {
        _ = task.ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            ReportActionResult(key, t.IsFaulted
                ? ActionResult.Failed(t.Exception?.GetBaseException().Message ?? "failed")
                : ActionResult.Ok);
        }, TaskScheduler.Default);
    }

    internal void ReportActionResult(KeyItemViewModel key, ActionResult result)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!key.ApplyActionResult(result)) return;

            if (result.IsFailure)
            {
                System.Diagnostics.Debug.WriteLine($"[key {key.Index + 1}] {key.AssignedAction} failed: {result.Reason}");
                PostFailureNotice(key, result);   // T-34 #8: a banner in the window, not only a log line
            }
            RenderKeyVisual(key, sendToHardware: true);
        });
    }
}
