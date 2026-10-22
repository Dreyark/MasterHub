using Avalonia.Controls;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;

namespace MasterHub.Avalonia.Views.Inspector;

public partial class KeyActionSettingsView : UserControl
{
    public IObsService? ObsService { get; }
    public IDiscordService? DiscordService { get; }
    public ITeamsService? TeamsService { get; }
    public ISystemMonitorService? SystemMonitorService { get; }
    public INowPlayingService? NowPlayingService { get; }

    public KeyActionSettingsView() : this(null, null, null, null, null)
    {
    }

    public KeyActionSettingsView(
        IObsService? obsService = null,
        IDiscordService? discordService = null,
        ITeamsService? teamsService = null,
        ISystemMonitorService? systemMonitorService = null,
        INowPlayingService? nowPlayingService = null)
    {
        ObsService = obsService;
        DiscordService = discordService;
        TeamsService = teamsService;
        SystemMonitorService = systemMonitorService;
        NowPlayingService = nowPlayingService;

        InitializeComponent();
    }

    private async void OnBrowseFileClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MasterHub.Avalonia.ViewModels.KeyItemViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Wybierz plik lub aplikację",
                    AllowMultiple = false
                });
                if (files.Count > 0)
                {
                    vm.ActionParameter = files[0].Path.LocalPath;
                    vm.AutoExtractIconIfApplicable();
                }
            }
        }
    }

    private async void OnBrowseFolderClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MasterHub.Avalonia.ViewModels.KeyItemViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new global::Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Wybierz folder z dysku",
                    AllowMultiple = false
                });
                if (folders.Count > 0)
                {
                    vm.ActionParameter = folders[0].Path.LocalPath;
                }
            }
        }
    }
}
