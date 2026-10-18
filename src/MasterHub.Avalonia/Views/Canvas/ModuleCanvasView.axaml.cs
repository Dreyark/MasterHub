using Avalonia.Controls;
using Avalonia.Input;
using MasterHub.Avalonia.ViewModels.Canvas;

namespace MasterHub.Avalonia.Views.Canvas;

public partial class ModuleCanvasView : UserControl
{
    public ModuleCanvasView()
    {
        InitializeComponent();
    }

    private void OnCanvasBackgroundPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is CanvasViewModel canvasVm)
        {
            canvasVm.DeselectAll();
        }
    }
}
