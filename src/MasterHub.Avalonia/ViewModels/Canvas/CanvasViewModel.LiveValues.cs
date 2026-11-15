using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Actions;
using MasterHub.Core.LiveValues;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>Live-value keys (T-11): tells the service which sources are on screen and shows what it reads.</summary>
public partial class CanvasViewModel
{
    private readonly ILiveValueService? _liveValues;

    /// <summary>Only the live-value keys of the view that is on the deck are fetched (the page the user left stops being polled).</summary>
    private void UpdateLiveValueDemand()
    {
        if (_liveValues == null) return;
        _liveValues.SetDemand(Keys.Where(k => k.AssignedAction == DeckActionType.LiveValue).Select(k => k.LiveSettings).ToList());
    }

    /// <summary>Puts the current reading of a live-value key on the key (text, severity colour).</summary>
    private void ApplyLiveReading(KeyItemViewModel key)
    {
        if (_liveValues == null || key.AssignedAction != DeckActionType.LiveValue) return;

        var display = LiveValueFormatter.Render(_liveValues.GetReading(key.LiveSettings), key.LiveSettings);
        key.LiveValueText = display.Text;
        key.LiveSeverity = display.Severity;
    }

    private void OnLiveReadingChanged(string sourceId)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys.Where(k => k.AssignedAction == DeckActionType.LiveValue && k.LiveSettings.SourceId() == sourceId))
            {
                ApplyLiveReading(key);
                RenderKeyVisual(key, sendToHardware: true);
            }
        });
    }

    /// <summary>A press fetches the value at once; a command that is not approved yet asks first (the exact line is shown).</summary>
    private bool TryExecuteLiveValueAction(KeyItemViewModel key)
    {
        if (key.AssignedAction != DeckActionType.LiveValue) return false;
        _ = PressLiveValueAsync(key);
        return true;
    }

    private async Task PressLiveValueAsync(KeyItemViewModel key)
    {
        var service = _liveValues;
        if (service == null) return;

        var settings = key.LiveSettings;
        if (settings.Kind == LiveValueSourceKind.Command && settings.IsConfigured && !service.IsCommandApproved(settings.Command))
        {
            bool allowed = await _confirmDialog.ConfirmAsync(ConfirmRequests.AllowCommand(settings.Command), TimeSpan.FromSeconds(30));
            if (!allowed) return;
            service.ApproveCommand(settings.Command);
        }

        await service.RefreshNowAsync(settings);
    }
}
