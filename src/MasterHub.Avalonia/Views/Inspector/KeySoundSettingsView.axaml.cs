using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Core.Audio;

namespace MasterHub.Avalonia.Views.Inspector;

public partial class KeySoundSettingsView : UserControl
{
    public KeySoundSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>The picker offers the types the player can read.</summary>
    internal static FilePickerFileType AudioFiles { get; } = new("Audio")
    {
        Patterns = SoundFiles.SupportedExtensions.Select(e => "*" + e).ToArray()
    };

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not KeyItemViewModel key || TopLevel.GetTopLevel(this) is not { } top) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Sound",
                AllowMultiple = false,
                FileTypeFilter = new[] { AudioFiles }
            });

            if (files.Count > 0) key.SoundEditor.FilePath = files[0].Path.LocalPath;
        }
        catch
        {
            // a picker that failed to open changes nothing
        }
    }
}
