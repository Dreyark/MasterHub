using Avalonia.Controls;
using global::Avalonia.Interactivity;
using MasterHub.Avalonia.ViewModels;

namespace MasterHub.Avalonia.Views.Inspector;

public partial class BaseStationInspectorView : UserControl
{
    public BaseStationInspectorView()
    {
        InitializeComponent();
    }

    private void OnPresetColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex && DataContext is BaseStationItemViewModel vm)
        {
            vm.SelectColor(hex);
        }
    }
}
