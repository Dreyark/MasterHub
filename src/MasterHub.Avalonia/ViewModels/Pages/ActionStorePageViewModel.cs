using System;
using System.Collections.ObjectModel;
using System.Linq;
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

public record IntegrationStatusItem(string Name, string Description, string Icon, string Status, bool IsOnline)
{
    public string StatusColor => IsOnline ? "#10B981" : "#6B7280";
}

public class StreamDeckPluginItemViewModel : ObservableObject, IDisposable
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

    // ── T-31: compatibility badge and reasons (e, f), CPU flag (a) ──────────────────────────

    private PluginCompatReport _compat = new(PluginVerdict.Runs, Array.Empty<PluginIssueItem>());
    private PluginCpuUse? _cpu;

    public PluginCompatReport Compat
    {
        get => _compat;
        set
        {
            _compat = value ?? new PluginCompatReport(PluginVerdict.Runs, Array.Empty<PluginIssueItem>());
            OnPropertyChanged(nameof(Compat));
            OnPropertyChanged(nameof(VerdictText));
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(IsPartial));
            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(IssuesText));
            OnPropertyChanged(nameof(NeedsNode));
        }
    }

    public bool CanRun => Compat.Verdict == PluginVerdict.Runs;
    public bool IsPartial => Compat.Verdict == PluginVerdict.Partial;
    public bool HasIssues => Compat.Issues.Count > 0;
    public string VerdictText => PluginTexts.Verdict(Compat.Verdict);
    public string IssuesText => string.Join("\n", Compat.Issues.Select(PluginTexts.Describe));

    /// <summary>Node.js is missing or too old: the card offers the download (after asking).</summary>
    public bool NeedsNode => Compat.Issues.Any(i => i.Issue is PluginIssue.NodeMissing or PluginIssue.NodeTooOld);

    public int RequiredNodeMajor => NodeResolver.RequiredMajor(Plugin.Manifest?.Nodejs?.Version);

    public PluginCpuUse? Cpu
    {
        get => _cpu;
        set
        {
            _cpu = value;
            OnPropertyChanged(nameof(Cpu));
            OnPropertyChanged(nameof(IsCpuHeavy));
            OnPropertyChanged(nameof(CpuText));
        }
    }

    public bool IsCpuHeavy => Cpu?.IsHeavy == true;
    public string CpuText => Cpu is { IsHeavy: true } c
        ? string.Format(LocalizationService.Instance.GetString("Plugin_CpuHeavy", "Uses a lot of CPU: {0:0} % of one core"), c.PercentOfOneCore)
        : "";

    public void Dispose()
    {
        PluginBitmap?.Dispose();
    }
}

public partial class ActionStorePageViewModel : ObservableObject
{
    private readonly IObsService? _obsService;
    private readonly MasterHub.Core.Dialogs.IConfirmDialogService _confirmDialog;
    private readonly IDiscordService? _discordService;
    private readonly ITeamsService? _teamsService;
    private readonly IStreamDeckPluginHost? _streamDeckHost;
    private readonly MasterHub.Core.Profiles.IProfileService? _profiles;

    /// <summary>For tests: replaces the Node.js download (T-31 b).</summary>
    internal Func<int, Task<NodeInstallResult>>? NodeInstaller { get; set; }

    public ObservableCollection<IntegrationStatusItem> Integrations { get; } = new();
    public ObservableCollection<StreamDeckPluginItemViewModel> StreamDeckPlugins { get; } = new();

    [ObservableProperty]
    private PluginCatalogViewModel _catalog;

    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 2)
        {
            _ = Catalog?.EnsureCatalogLoadedAsync();
        }
    }

    [ObservableProperty]
    private string _statusMessage = "";

    public ActionStorePageViewModel(
        IObsService? obsService = null,
        IDiscordService? discordService = null,
        ITeamsService? teamsService = null,
        IStreamDeckPluginHost? streamDeckHost = null,
        IPluginCatalogService? catalogService = null,
        MasterHub.Core.Dialogs.IConfirmDialogService? confirmDialog = null,
        MasterHub.Core.Profiles.IProfileService? profiles = null)
    {
        _profiles = profiles;
        _obsService = obsService;
        _discordService = discordService;
        _teamsService = teamsService;
        _streamDeckHost = streamDeckHost;
        _confirmDialog = confirmDialog ?? new MasterHub.Core.Dialogs.AutoConfirmDialogService();
        _catalog = new PluginCatalogViewModel(catalogService ?? new PluginCatalogService(), streamDeckHost, () => RefreshPlugins());

        if (_obsService != null) _obsService.ConnectionStateChanged += _ => Dispatcher.UIThread.Post(RefreshIntegrations);
        if (_discordService != null) _discordService.ConnectionStateChanged += _ => Dispatcher.UIThread.Post(RefreshIntegrations);
        if (_streamDeckHost != null)
        {
            _streamDeckHost.PluginInstalled += _ => Dispatcher.UIThread.Post(RefreshPlugins);
            // T-31 a / f: the CPU flag and "why it did not start" reach the card without a refresh
            _streamDeckHost.PluginCpuChanged += use => Dispatcher.UIThread.Post(() => OnPluginCpuChanged(use));
            _streamDeckHost.PluginCannotRun += (uuid, report) => Dispatcher.UIThread.Post(() => OnPluginCannotRun(uuid, report));
        }

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
        Integrations.Add(new("OBS Studio", loc.GetString("Store_ObsDesc", "..."), "ObsLogo",
            obsOnline ? loc.GetString("Store_ObsOnline", "Połączono (WebSocket v5)") : loc.GetString("Store_ObsOffline", "Gotowy do połączenia"), obsOnline));

        bool discordOnline = _discordService?.IsConnected ?? false;
        Integrations.Add(new("Discord", loc.GetString("Store_DiscordDesc", "..."), "Discord",
            discordOnline ? loc.GetString("Store_DiscordOnline", "Aktywny (Local IPC)") : loc.GetString("Store_DiscordOffline", "Oczekiwanie na klienta"), discordOnline));

        bool teamsOnline = _teamsService?.IsTeamsRunning ?? false;
        Integrations.Add(new("Microsoft Teams", loc.GetString("Store_TeamsDesc", "..."), "Teams",
            teamsOnline ? loc.GetString("Store_TeamsOnline", "Wykryto proces Teams") : loc.GetString("Store_TeamsOffline", "Nieuruchomiony"), teamsOnline));

        Integrations.Add(new("System & Media", loc.GetString("Store_SysMediaDesc", "..."), "Computer", loc.GetString("Store_SysMediaActive", "Aktywny (System API)"), true));
        Integrations.Add(new("Razer Synapse & Wooting", loc.GetString("Store_HardwareDesc", "..."), "Razer", loc.GetString("Store_HardwareReady", "Gotowy (HID SDK)"), true));
    }

    [RelayCommand]
    public void RefreshPlugins()
    {
        // The old items leave the list first and their icons are released afterwards: an Image that is still on screen must never
        // be handed a disposed bitmap (it throws in the layout pass).
        var old = StreamDeckPlugins.ToList();
        StreamDeckPlugins.Clear();
        if (old.Count > 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var p in old) p.Dispose();
            }, DispatcherPriority.Background);
        }
        if (_streamDeckHost == null) return;
        _streamDeckHost.RefreshPlugins();
        foreach (var p in _streamDeckHost.InstalledPlugins)
        {
            StreamDeckPlugins.Add(new StreamDeckPluginItemViewModel(p)
            {
                Compat = _streamDeckHost.CheckCompatibility(p.PluginUuid),
                Cpu = _streamDeckHost.GetCpuUse(p.PluginUuid)
            });
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
                    StatusMessage = (await PluginInstallFollowUp.RunAsync(_streamDeckHost, installed)).Message;
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
    public void OpenPluginSettings(StreamDeckPluginItemViewModel? item)
    {
        if (item == null) return;
        var vm = new PluginSettingsViewModel(item, _streamDeckHost);
        var win = new Views.Pages.PluginSettingsWindow(vm);

        var topLevel = (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (topLevel != null)
        {
            win.ShowDialog(topLevel);
        }
        else
        {
            win.Show();
        }
    }

    [RelayCommand]
    public async Task TogglePluginAsync(StreamDeckPluginItemViewModel? item)
    {
        if (item == null || _streamDeckHost == null) return;
        // (starting / stopping is not destructive: it used to show the "Uninstall plugin?" question by mistake)
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
        // T-31 c: the question lists the keys that will stop working
        var usages = _profiles == null ? Array.Empty<PluginUsage>() : PluginUsageScanner.Find(_profiles.GetAllProfiles(), item.PluginUuid);
        if (!await _confirmDialog.ConfirmAsync(MasterHub.Avalonia.Services.ConfirmRequests.UninstallPlugin(item.Name, usages))) return;
        bool deleted = await _streamDeckHost.UninstallPluginAsync(item.PluginUuid);
        if (deleted)
        {
            StatusMessage = $"Odinstalowano: {item.Name}";
            RefreshPlugins();
        }
    }
}
