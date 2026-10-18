using Avalonia.Controls;
using Avalonia.Input;
using MasterHub.Avalonia.ViewModels.Canvas;

namespace MasterHub.Avalonia.Views.Canvas;

public partial class BaseStationView : UserControl
{
    public BaseStationView()
    {
        InitializeComponent();
    }

    private void OnBasePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is CanvasViewModel canvasVm)
        {
            canvasVm.SelectBase();
        }
    }
}
