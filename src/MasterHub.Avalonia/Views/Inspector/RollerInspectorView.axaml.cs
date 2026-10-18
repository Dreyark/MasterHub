using Avalonia.Controls;
using global::Avalonia.Interactivity;
using MasterHub.Avalonia.ViewModels;

namespace MasterHub.Avalonia.Views.Inspector;

public partial class RollerInspectorView : UserControl
{
    public RollerInspectorView()
    {
        InitializeComponent();
    }

    private void OnResetStepsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RollerItemViewModel roller)
        {
            roller.ResetSteps();
        }
    }
}
