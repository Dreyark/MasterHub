using MasterHub.Core.Devices;
using MasterHub.Core.Rendering;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>The base's edge light (T-18): the profile's own colour + effect, and the status light that may cover it.</summary>
public partial class CanvasViewModel
{
    private readonly DeckStatusLightService? _statusLights;
    private StatusLight? _statusLight;

    /// <summary>Lights set from outside (the CLI, T-24): they stay until another profile is applied or the profile's own lights are asked for.</summary>
    private bool _lightsOverride;

    /// <summary>Colour, effect and speed of the profile's own edge light go to the base (the status light, if any, wins).</summary>
    private void ApplyBaseEdge()
    {
        if (_statusLight != null || _lightsOverride) return;

        if (global::Avalonia.Media.Color.TryParse(BaseStation.ColorHex, out var col))
        {
            var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, BaseStation.Brightness);
            _deviceService.SetBaseLedColor(r, g, b);
        }
        PushBaseEffect();
    }

    private void PushBaseEffect()
    {
        // the colour command above puts the base in static mode; an effect is sent after it (the scheduler keeps that order)
        if (BaseStation.EdgeEffect != LedEffect.Static) _deviceService.SetBaseLedMode(BaseStation.EdgeEffect);
        if (BaseStation.ShowsEdgeSpeed) _deviceService.SetLedSpeed(BaseStation.EdgeSpeed);
    }

    private void OnBaseEffectChanged(BaseStationItemViewModel baseStation)
    {
        if (_statusLight == null)
        {
            // Static needs the colour again (it also switches the mode back); animated effects keep their colour.
            if (baseStation.EdgeEffect == LedEffect.Static) ApplyBaseEdge();
            else PushBaseEffect();
        }
        ScheduleAutoSave();
    }

    /// <summary>The status light changed: show it on the edge, or (null) give the edge back to the profile.</summary>
    private void OnStatusLightChanged(StatusLight? light)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _statusLight = light;
            if (light is StatusLight l)
            {
                _deviceService.SetBaseLedColor(l.R, l.G, l.B);
                if (l.Effect != LedEffect.Static) _deviceService.SetBaseLedMode(l.Effect);
            }
            else
            {
                ApplyBaseEdge();
            }
        });
    }

    /// <summary>A reaction was switched on/off in the inspector: keep it in the app settings and tell the watcher.</summary>
    private void OnStatusLightSettingsChanged(BaseStationItemViewModel vm)
    {
        var settings = _profileService.GetAppSettings();
        settings.StatusLights = new StatusLightSettings
        {
            Enabled = vm.StatusLightsEnabled,
            MicMuted = vm.StatusMicMuted,
            ObsLive = vm.StatusObsLive,
            DiscordMuted = vm.StatusDiscordMuted,
            Attention = settings.StatusLights.Attention,
            Ready = settings.StatusLights.Ready
        };
        _profileService.SaveAppSettings(settings);
        _statusLights?.Refresh();
    }
}
