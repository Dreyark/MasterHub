using Avalonia.Controls;
using Avalonia.Interactivity;
using MasterHub.Avalonia.ViewModels.Inspector;

namespace MasterHub.Avalonia.Views.Inspector;

public partial class IconLibraryPickerView : UserControl
{
    public IconLibraryPickerView()
    {
        InitializeComponent();
    }

    // Click handlers instead of bindings through the parent: no binding errors while the picker has no data context.
    private void OnIconClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name } && DataContext is IconPickerViewModel vm)
        {
            vm.PickCommand.Execute(name);
        }
    }

    private void OnTintClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex } && DataContext is IconPickerViewModel vm)
        {
            vm.SelectTintCommand.Execute(hex);
        }
    }
}
