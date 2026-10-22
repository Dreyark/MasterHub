using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MasterHub.Avalonia.ViewModels;

namespace MasterHub.Avalonia.Views.Inspector;

public partial class KeyPanoramicSettingsSectionView : UserControl
{
    public KeyPanoramicSettingsSectionView()
    {
        InitializeComponent();
    }

    private async void OnBrowsePanoramicClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyItemViewModel vm) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var loc = MasterHub.Core.Localization.LocalizationService.Instance;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = loc.GetString("Panoramic_PickerTitle", "Wybierz tapetę panoramiczną na 15 ekranów (480x272)"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(loc.GetString("Panoramic_FileFilter", "Obrazy panoramiczne (*.jpg, *.jpeg, *.png, *.webp, *.bmp)"))
                {
                    Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp" }
                },
                FilePickerFileTypes.ImageAll
            }
        });

        if (files.Count > 0)
        {
            vm.RequestApplyPanoramicWallpaper(files[0].Path.LocalPath, vm.PanoramicWallpaperFitMode ?? "Cover");
        }
    }

    private void OnClearPanoramicClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is KeyItemViewModel vm)
        {
            vm.RequestClearPanoramicWallpaper();
        }
    }
}
