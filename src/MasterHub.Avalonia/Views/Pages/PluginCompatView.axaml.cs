using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MasterHub.Avalonia.ViewModels.Pages;

namespace MasterHub.Avalonia.Views.Pages;

public partial class PluginCompatView : UserControl
{
    public PluginCompatView()
    {
        InitializeComponent();
    }

    private void OnDownloadNodeClick(object? sender, RoutedEventArgs e)
    {
        var page = this.FindAncestorOfType<ActionStorePageView>()?.DataContext as ActionStorePageViewModel;
        if (page != null && DataContext is StreamDeckPluginItemViewModel item)
        {
            _ = page.InstallNodeAsync(item);
        }
    }
}
