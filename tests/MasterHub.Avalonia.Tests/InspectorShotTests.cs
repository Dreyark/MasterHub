using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.Views.Canvas;
using MasterHub.Avalonia.Views.Inspector;
using MasterHub.Core.Actions;

namespace MasterHub.UiTests;

/// <summary>Renderuje inspektory i moduły z prawdziwymi ViewModelami (dane przykładowe), do przeglądu wizualnego.</summary>
public class InspectorShotTests
{
    [AvaloniaFact]
    public void KeyInspector_WithObsAction()
    {
        var key = new KeyItemViewModel { Index = 2, AssignedAction = DeckActionType.ObsToggleStream, CustomTitle = "Stream", CustomSubtitle = "OBS" };
        var view = new KeyInspectorView { DataContext = key };
        Shot.Render(view, "inspector_key_obs", 340, 2200);
    }

    [AvaloniaFact]
    public void KeyInspector_WithHotkeyAction()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.Hotkey, ActionParameter = "Ctrl+Shift+M", CustomTitle = "Mute" };
        Shot.Render(new KeyInspectorView { DataContext = key }, "inspector_key_hotkey", 340, 2200);
    }

    [AvaloniaFact]
    public void OtherInspectors()
    {
        Shot.Render(new SliderInspectorView { DataContext = new SliderItemViewModel { Index = 1 } }, "inspector_slider", 340, 700);
        Shot.Render(new RollerInspectorView { DataContext = new RollerItemViewModel { Index = 0 } }, "inspector_roller", 340, 600);
        Shot.Render(new KnobInspectorView { DataContext = new KnobItemViewModel { Index = 0 } }, "inspector_knob", 340, 800);
        Shot.Render(new DialInspectorView { DataContext = new DialItemViewModel() }, "inspector_dial", 340, 800);
        Shot.Render(new BaseStationInspectorView { DataContext = new BaseStationItemViewModel() }, "inspector_base", 340, 600);
    }

    [AvaloniaFact]
    public void Modules()
    {
        var keys = new KeysModuleView();
        Shot.Render(keys, "module_keys", 640, 420);
        Shot.Render(new SlidersModuleView(), "module_sliders", 640, 420);
        Shot.Render(new KnobsModuleView(), "module_knobs", 640, 300);
        Shot.Render(new DialModuleView(), "module_dial", 400, 400);
    }
}
