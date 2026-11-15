using MasterHub.Core.Actions;
using MasterHub.Core.Devices;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>Keys that change the deck's brightness (T-17). They move the same setting as the slider, so the slider, the synced lights and the profile follow.</summary>
public partial class CanvasViewModel
{
    private bool TryExecuteBrightnessAction(KeyItemViewModel key)
    {
        BrightnessCommand command;
        switch (key.AssignedAction)
        {
            case DeckActionType.DeckBrightnessUp: command = BrightnessCommand.Up; break;
            case DeckActionType.DeckBrightnessDown: command = BrightnessCommand.Down; break;
            case DeckActionType.DeckBrightnessCycle: command = BrightnessCommand.Cycle; break;
            default: return false;
        }

        KeypadBrightness = (byte)DeckBrightness.Next(KeypadBrightness, command, DeckBrightness.ParseStep(key.ActionParameter));
        return true;
    }
}
