using Avalonia.Controls;
using Avalonia.Input;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;

namespace MasterHub.Avalonia.Views.Canvas;

public partial class RollersModuleView : UserControl
{
    public RollersModuleView()
    {
        InitializeComponent();
    }

    private void OnRollerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control c && c.DataContext is RollerItemViewModel roller)
        {
            e.Handled = true;
            if (DataContext is CanvasViewModel canvasVm)
            {
                canvasVm.SelectRoller(roller);
            }
        }
    }
}
