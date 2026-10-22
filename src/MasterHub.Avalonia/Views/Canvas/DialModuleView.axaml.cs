using Avalonia.Controls;
using Avalonia.Input;
using MasterHub.Avalonia.ViewModels.Canvas;

namespace MasterHub.Avalonia.Views.Canvas;

public partial class DialModuleView : UserControl
{
    public DialModuleView()
    {
        InitializeComponent();
    }

    private void OnDialPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is CanvasViewModel canvasVm)
        {
            canvasVm.SelectDial();
        }
    }
}
