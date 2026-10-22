using Avalonia.Controls;
using Avalonia.Input;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;

namespace MasterHub.Avalonia.Views.Canvas;

public partial class KnobsModuleView : UserControl
{
    public KnobsModuleView()
    {
        InitializeComponent();
    }

    private void OnKnobPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control c && c.DataContext is KnobItemViewModel knob)
        {
            e.Handled = true;
            if (DataContext is CanvasViewModel canvasVm)
            {
                canvasVm.SelectKnob(knob);
            }
        }
    }
}
