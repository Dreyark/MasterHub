using Avalonia.Controls;
using Avalonia.Input;
using MasterHub.Avalonia.ViewModels;

namespace MasterHub.Avalonia.Views.Controls;

public partial class ConfirmOverlayView : UserControl
{
    public ConfirmOverlayView()
    {
        InitializeComponent();
        Focusable = true;
        PropertyChanged += (_, e) =>
        {
            // Take the keyboard when the overlay appears so that Esc cancels it.
            if (e.Property == IsVisibleProperty && IsVisible) Focus();
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ConfirmOverlayViewModel vm)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
