using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MasterHub.Avalonia.ViewModels;

namespace MasterHub.Avalonia.Views.Inspector;

public partial class DialInspectorView : UserControl
{
    public DialInspectorView()
    {
        InitializeComponent();
    }

    private void OnResetStepsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DialItemViewModel vm)
        {
            vm.ResetSteps();
        }
    }

    private void OnPresetColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex && DataContext is DialItemViewModel vm)
        {
            vm.SelectColor(hex);
        }
    }

    private async void OnBrowseImageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DialItemViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz obraz dla tarczy Dial IPS (240x240)",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });

        if (files.Count > 0)
        {
            vm.ImagePath = files[0].Path.LocalPath;
        }
    }

    private void OnClearImageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DialItemViewModel vm)
        {
            vm.ImagePath = null;
        }
    }
}
