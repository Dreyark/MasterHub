using Avalonia.Controls;
using Avalonia.Interactivity;
using MasterHub.Avalonia.ViewModels;

namespace MasterHub.Avalonia.Views.Controls;

public partial class NoticeBannerView : UserControl
{
    public NoticeBannerView()
    {
        InitializeComponent();
    }

    private void OnDismissClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long id } && DataContext is NoticeBannerViewModel vm)
        {
            vm.DismissCommand.Execute(id);
        }
    }
}
