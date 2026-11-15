using MasterHub.Core.Api;
using MasterHub.Core.Devices;
using MasterHub.Core.Profiles;
using MasterHub.Core.Rendering;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>Lights set from outside, for now (the CLI's <c>lights</c> command, T-24): nothing is saved into a profile.</summary>
public partial class CanvasViewModel
{
    /// <summary>
    /// Puts the deck's lights (the base's edge, the faders and the knobs) in one state, or gives them back to the profile.
    /// The state stays until another profile is applied or <see cref="LightsMode.Profile"/> is asked for. Returns <c>false</c> for a colour
    /// that is not <c>#rrggbb</c>.
    /// </summary>
    public bool ApplyLightsOverride(LightsMode mode, string? colorHex)
    {
        if (mode == LightsMode.Profile)
        {
            _lightsOverride = false;
            PushAllHardwareState();   // the colours, effects and brightness of the active profile go out again
            return true;
        }

        byte r = 0, g = 0, b = 0;
        if (mode == LightsMode.Color)
        {
            if (!global::Avalonia.Media.Color.TryParse(colorHex, out var col)) return false;
            (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B);
        }

        _lightsOverride = true;

        if (mode == LightsMode.Rainbow)
        {
            _deviceService.SetBaseLedMode(LedEffect.Rainbow);
            foreach (var slider in Sliders) _deviceService.SetSliderLedMode(slider.Index, LedEffect.Rainbow);
            foreach (var knob in Knobs) _deviceService.SetKnobLedMode(knob.Index, KnobLedMode.Rainbow);
            return true;
        }

        // Off is the colour black; a colour command also puts each module back in its static mode
        _deviceService.SetBaseLedColor(r, g, b);
        foreach (var slider in Sliders) _deviceService.SetSliderLedColor(slider.Index, r, g, b);
        foreach (var roller in Rollers) _deviceService.SetRollerLedColor(roller.Index, r, g, b);
        foreach (var knob in Knobs)
        {
            _deviceService.SetKnobLedColor(knob.Index, r, g, b);
            _deviceService.SetKnobLedMode(knob.Index, KnobLedMode.Static);
        }

        return true;
    }
}
