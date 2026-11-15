using System;
using System.Threading.Tasks;
using MasterHub.Core.Dialogs;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Canvas;

public partial class CanvasViewModel
{
    /// <summary>
    /// Shutdown / restart / sleep (ROADMAP B-02). Runs at once when the hardware key was held for
    /// <c>PowerActionGuard.HoldToConfirmMs</c> or the key opted out; every other trigger (a click in the Studio,
    /// a rotary push, a multi-action step) asks first through the in-app confirmation overlay.
    /// </summary>
    /// <param name="key">The key that carries the action.</param>
    /// <param name="kind"><c>Shutdown</c>, <c>Restart</c> or <c>Sleep</c> (selects the localised texts).</param>
    /// <param name="action">The system call.</param>
    private void RunPowerAction(KeyItemViewModel key, string kind, Action action)
    {
        if (key.SkipPowerConfirmation || ReferenceEquals(_holdConfirmedKey, key))
        {
            action();
            return;
        }

        _ = ConfirmAndRunPowerAsync(kind, action);
    }

    private async Task ConfirmAndRunPowerAsync(string kind, Action action)
    {
        var loc = LocalizationService.Instance;
        var request = new ConfirmRequest(
            loc.GetString($"Confirm_Power{kind}_Title", $"Power action: {kind}"),
            loc.GetString("Confirm_Power_Message", "The computer will be affected immediately. Hold the key on the deck for 1.5 s instead to skip this question next time."),
            loc.GetString($"Confirm_Power{kind}_Button", kind),
            loc.GetString("Confirm_Cancel", "Cancel"));

        bool confirmed = await _confirmDialog.ConfirmAsync(request, TimeSpan.FromSeconds(20));
        if (confirmed) action();
    }
}
