using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MasterHub.Avalonia.ViewModels.Inspector;

namespace MasterHub.Avalonia.Views.Inspector;

public partial class KeyGestureTabsView : UserControl
{
    public KeyGestureTabsView()
    {
        InitializeComponent();
    }

    /// <summary>"Change…": opens the Action library for the slot being edited (Press / Hold / Double).</summary>
    private void OnChangeActionClick(object? sender, RoutedEventArgs e)
    {
        for (Visual? current = this; current != null; current = current.GetVisualParent())
        {
            if (current is Control { DataContext: InspectorViewModel inspector })
            {
                inspector.OpenActionLibrary();
                return;
            }
        }
    }
}
