using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MasterHub.Avalonia.ViewModels;

namespace MasterHub.Avalonia.Views.Inspector;

public partial class KeyDisplaySettingsView : UserControl
{
    public KeyDisplaySettingsView()
    {
        InitializeComponent();
    }

    private void OnPresetIconClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string glyph && DataContext is KeyItemViewModel vm)
        {
            vm.SetPresetIcon(glyph);
        }
    }

    // The library is read only when the picker is opened for the first time, not every time a key is selected.
    private void OnIconLibraryOpening(object? sender, EventArgs e)
    {
        if (DataContext is KeyItemViewModel vm && sender is Flyout { Content: Border { Child: IconLibraryPickerView picker } })
        {
            picker.DataContext = vm.IconPicker;
        }
    }

    private void OnPresetBgColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex && DataContext is KeyItemViewModel vm)
        {
            vm.SelectBackgroundColor(hex);
        }
    }

    private async void OnBrowseImageClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not KeyItemViewModel vm) return;
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var loc = MasterHub.Core.Localization.LocalizationService.Instance;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = loc.GetString("Media_PickerTitle", "Wybierz grafikę, animację lub wideo dla klawisza LCD"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(loc.GetString("Media_AllSupported", "Wszystkie multimedia"))
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.ico", "*.gif", "*.mp4", "*.avi", "*.mov", "*.wmv", "*.webm", "*.mkv" }
                    },
                    new FilePickerFileType(loc.GetString("Media_Videos", "Wideo (*.mp4, *.avi, *.mov, *.wmv, *.webm, *.mkv)"))
                    {
                        Patterns = new[] { "*.mp4", "*.avi", "*.mov", "*.wmv", "*.webm", "*.mkv" }
                    },
                    new FilePickerFileType(loc.GetString("Media_Animations", "Animacje GIF / WebP (*.gif, *.webp)"))
                    {
                        Patterns = new[] { "*.gif", "*.webp" }
                    },
                    FilePickerFileTypes.ImageAll
                }
            });

            if (files.Count > 0)
            {
                vm.ApplyMediaFile(files[0].Path.LocalPath, vm.SelectedEditState == 1);
                vm.NotifyCurrentEditProperties();
            }
        }
        catch { }
    }

    private void OnClearImageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is KeyItemViewModel vm)
        {
            vm.ApplyMediaFile("", vm.SelectedEditState == 1);
            vm.NotifyCurrentEditProperties();
        }
    }
}
