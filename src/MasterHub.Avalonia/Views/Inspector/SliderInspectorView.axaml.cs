using Avalonia.Controls;
using global::Avalonia.Interactivity;
using MasterHub.Avalonia.ViewModels;

namespace MasterHub.Avalonia.Views.Inspector;

public partial class SliderInspectorView : UserControl
{
    public SliderInspectorView()
    {
        InitializeComponent();
    }

    private void OnPresetColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex && DataContext is SliderItemViewModel vm)
        {
            vm.SelectColor(hex);
        }
    }
}
