using Avalonia.Controls;

namespace MasterHub.Avalonia.Views.Inspector;

public partial class KeyTimerSettingsView : UserControl
{
    public KeyTimerSettingsView()
    {
        InitializeComponent();
    }

    private async void OnBrowseEndSound(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MasterHub.Avalonia.ViewModels.KeyItemViewModel key || TopLevel.GetTopLevel(this) is not { } top) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Sound",
                AllowMultiple = false,
                FileTypeFilter = new[] { KeySoundSettingsView.AudioFiles }
            });

            if (files.Count > 0) key.TimerEditor.EndSoundPath = files[0].Path.LocalPath;
        }
        catch
        {
            // a picker that failed to open changes nothing
        }
    }
}
