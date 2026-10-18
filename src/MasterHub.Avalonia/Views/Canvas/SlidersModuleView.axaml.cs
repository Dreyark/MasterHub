using Avalonia.Controls;
using Avalonia.Input;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;

namespace MasterHub.Avalonia.Views.Canvas;

public partial class SlidersModuleView : UserControl
{
    public SlidersModuleView()
    {
        InitializeComponent();
    }

    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control c && c.DataContext is SliderItemViewModel slider)
        {
            e.Handled = true;
            if (DataContext is CanvasViewModel canvasVm)
            {
                canvasVm.SelectSlider(slider);
            }
        }
    }
}
