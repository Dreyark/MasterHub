using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Pages;

public record IntegrationStatusItem(string Name, string Description, string Icon, string Status, bool IsOnline);

public class StreamDeckPluginItemViewModel : ObservableObject
{
    public StreamDeckInstalledPlugin Plugin { get; }
    public Bitmap? PluginBitmap { get; }

    public string PluginUuid => Plugin.PluginUuid;
    public string Name => Plugin.Name;
    public string Author => Plugin.Author;
    public string Version => Plugin.Version;
    public string Description => Plugin.Description;
    public bool IsNodeJs => Plugin.IsNodeJs;
    public bool IsRunning => Plugin.IsRunning;
    public string StatusDisplay => Plugin.StatusDisplay;
    public string ToggleButtonText => Plugin.ToggleButtonText;
    public string ActionsCountDisplay => Plugin.ActionsCountDisplay;

    public StreamDeckPluginItemViewModel(StreamDeckInstalledPlugin plugin)
    {
        Plugin = plugin;
        if (!string.IsNullOrEmpty(plugin.IconPath))
        {
            PluginBitmap = KeyItemViewModel.LoadMediaBitmap(plugin.IconPath);
        }
    }
}

public partial class ActionStorePageViewModel : ObservableObject
{
    private readonly IObsService? _obsService;
    private readonly IDiscordService? _discordService;
    private readonly ITeamsService? _teamsService;
    private readonly IStreamDeckPluginHost? _streamDeckHost;

    public ObservableCollection<IntegrationStatusItem> Integrations { get; } = new();
    public ObservableCollection<StreamDeckPluginItemViewModel> StreamDeckPlugins { get; } = new();

    [ObservableProperty]
    private PluginCatalogViewModel _catalog;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _statusMessage = "";

    public ActionStorePageViewModel(
        IObsService? obsService = null,
        IDiscordService? discordService = null,
        ITeamsService? teamsService = null,
        IStreamDeckPluginHost? streamDeckHost = null,
        IPluginCatalogService? catalogService = null)
    {
        _obsService = obsService;
        _discordService = discordService;
        _teamsService = teamsService;
        _streamDeckHost = streamDeckHost;
        _catalog = new PluginCatalogViewModel(catalogService ?? new PluginCatalogService(), streamDeckHost, () => RefreshPlugins());

        if (_obsService != null) _obsService.ConnectionStateChanged += _ => Dispatcher.UIThread.Post(RefreshIntegrations);
        if (_discordService != null) _discordService.ConnectionStateChanged += _ => Dispatcher.UIThread.Post(RefreshIntegrations);
        if (_streamDeckHost != null) _streamDeckHost.PluginInstalled += _ => Dispatcher.UIThread.Post(RefreshPlugins);

        LocalizationService.Instance.LanguageChanged += _ =>
        {
            RefreshIntegrations();
            RefreshPlugins();
        };

        RefreshIntegrations();
        RefreshPlugins();
    }

    public void RefreshIntegrations()
    {
        Integrations.Clear();
        var loc = LocalizationService.Instance;

        bool obsOnline = _obsService?.IsConnected ?? false;
        Integrations.Add(new("OBS Studio", loc.GetString("Store_ObsDesc", "..."), "📡",
            obsOnline ? loc.GetString("Store_ObsOnline", "Połączono (WebSocket v5)") : loc.GetString("Store_ObsOffline", "Gotowy do połączenia"), obsOnline));

        bool discordOnline = _discordService?.IsConnected ?? false;
        Integrations.Add(new("Discord", loc.GetString("Store_DiscordDesc", "..."), "🎙️",
            discordOnline ? loc.GetString("Store_DiscordOnline", "Aktywny (Local IPC)") : loc.GetString("Store_DiscordOffline", "Oczekiwanie na klienta"), discordOnline));

        bool teamsOnline = _teamsService?.IsTeamsRunning ?? false;
        Integrations.Add(new("Microsoft Teams", loc.GetString("Store_TeamsDesc", "..."), "💼",
            teamsOnline ? loc.GetString("Store_TeamsOnline", "Wykryto proces Teams") : loc.GetString("Store_TeamsOffline", "Nieuruchomiony"), teamsOnline));

        Integrations.Add(new("System & Media", loc.GetString("Store_SysMediaDesc", "..."), "🖥️", loc.GetString("Store_SysMediaActive", "Aktywny (System API)"), true));
        Integrations.Add(new("Razer Synapse & Wooting", loc.GetString("Store_HardwareDesc", "..."), "⚡", loc.GetString("Store_HardwareReady", "Gotowy (HID SDK)"), true));
    }

    [RelayCommand]
    public void RefreshPlugins()
    {
        StreamDeckPlugins.Clear();
        if (_streamDeckHost == null) return;
        _streamDeckHost.RefreshPlugins();
        foreach (var p in _streamDeckHost.InstalledPlugins)
        {
            StreamDeckPlugins.Add(new StreamDeckPluginItemViewModel(p));
        }
        Catalog?.UpdateInstalledStates();
    }

    [RelayCommand]
    public async Task InstallPluginAsync()
    {
        if (_streamDeckHost == null) return;
        try
        {
            var topLevel = (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Wybierz wtyczkę Stream Deck",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Wtyczka Stream Deck (*.streamDeckPlugin, *.zip)")
                    {
                        Patterns = new[] { "*.streamDeckPlugin", "*.zip" }
                    }
                }
            });

            if (files.Count > 0)
            {
                string path = files[0].Path.LocalPath;
                StatusMessage = "Instalowanie wtyczki...";
                var installed = await _streamDeckHost.InstallPluginPackageAsync(path);
                if (installed != null)
                {
                    StatusMessage = $"Zainstalowano: {installed.Name}";
                    await _streamDeckHost.StartPluginAsync(installed.PluginUuid);
                    RefreshPlugins();
                }
                else
                {
                    StatusMessage = "Błąd: Nie udało się zainstalować wtyczki (nieprawidłowy format lub brak manifestu).";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Błąd instalacji: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenPluginsFolder()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub", "plugins");
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    public async Task TogglePluginAsync(StreamDeckPluginItemViewModel? item)
    {
        if (item == null || _streamDeckHost == null) return;
        if (item.IsRunning)
        {
            await _streamDeckHost.StopPluginAsync(item.PluginUuid);
            StatusMessage = $"Zatrzymano: {item.Name}";
        }
        else
        {
            await _streamDeckHost.StartPluginAsync(item.PluginUuid);
            StatusMessage = $"Uruchomiono: {item.Name}";
        }
        RefreshPlugins();
    }

    [RelayCommand]
    public async Task UninstallPluginAsync(StreamDeckPluginItemViewModel? item)
    {
        if (item == null || _streamDeckHost == null) return;
        bool deleted = await _streamDeckHost.UninstallPluginAsync(item.PluginUuid);
        if (deleted)
        {
            StatusMessage = $"Odinstalowano: {item.Name}";
            RefreshPlugins();
        }
    }
}
