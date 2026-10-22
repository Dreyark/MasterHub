using Avalonia.Controls;
using Avalonia.Interactivity;
using MasterHub.Avalonia.ViewModels;

namespace MasterHub.Avalonia.Views.Inspector;

public partial class KnobInspectorView : UserControl
{
    public KnobInspectorView()
    {
        InitializeComponent();
    }

    private void OnResetStepsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is KnobItemViewModel vm)
        {
            vm.ResetSteps();
        }
    }

    private void OnPresetColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex && DataContext is KnobItemViewModel vm)
        {
            vm.SelectColor(hex);
        }
    }
}
