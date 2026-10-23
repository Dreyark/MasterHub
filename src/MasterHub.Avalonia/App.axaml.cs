using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Devices;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Integrations.YouTube;
using MasterHub.Core.Localization;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Avalonia.ViewModels.Inspector;

namespace MasterHub.Avalonia;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += (s, e) => ShutdownHardware();
            desktop.Exit += (s, e) => ShutdownHardware();
            var appSettings = Services.GetRequiredService<IProfileService>().GetAppSettings();
            if (!string.IsNullOrWhiteSpace(appSettings.LanguageCode))
                LocalizationService.Instance.SetLanguage(appSettings.LanguageCode);
            bool isAutostart = desktop.Args?.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase) || a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)) == true;
            if (isAutostart && appSettings.StartMinimizedToTray)
                mainWindow.Opened += (s, e) => Services.GetRequiredService<ITrayService>().MinimizeToTray();
        }

        AppDomain.CurrentDomain.ProcessExit += (s, e) => ShutdownHardware();

        base.OnFrameworkInitializationCompleted();
        _ = Services.GetRequiredService<IStreamDeckPluginHost>().StartAsync();

        var haSettings = Services.GetRequiredService<IProfileService>().GetHomeAssistantSettings();
        if (haSettings.AutoConnect && !string.IsNullOrWhiteSpace(haSettings.AccessToken))
            _ = Services.GetRequiredService<IHomeAssistantService>().ConnectAsync();
    }

    private static int s_isShuttingDown;

    private static void ShutdownHardware()
    {
        if (System.Threading.Interlocked.Exchange(ref s_isShuttingDown, 1) != 0) return;
        var sp = Services;
        Services = null!;
        try { sp?.GetService<IStreamDeckPluginHost>()?.StopAsync().GetAwaiter().GetResult(); } catch { }
        try { sp?.GetService<CanvasViewModel>()?.SaveCurrentStateToActiveProfile(); } catch { }
        try { sp?.GetService<IMasterHubDeviceService>()?.Dispose(); } catch { }
        try { (sp as IDisposable)?.Dispose(); } catch { }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core hardware & audio/system services
        services.AddSingleton<IMasterHubManager, MasterHubManager>();
        services.AddSingleton<IAudioService, WindowsAudioService>();
        services.AddSingleton<ISystemControlService, WindowsSystemControlService>();
        services.AddSingleton<IStartupService, WindowsStartupService>();
        services.AddSingleton<ISecretProtectionService, SecretProtectionService>();
        services.AddSingleton<IProfileService>(sp => new ProfileService(null, sp.GetRequiredService<ISecretProtectionService>()));
        services.AddSingleton<ISmartProfileService, SmartProfileService>();
        services.AddSingleton<ISystemMonitorService, SystemMonitorService>();
        services.AddSingleton<INowPlayingService, NowPlayingService>();
        // Core integrations
        services.AddSingleton<IObsService, ObsService>();
        services.AddSingleton<IDiscordService>(_ => new DiscordService());
        services.AddSingleton<ITeamsService>(_ => new TeamsService());
        services.AddSingleton<ITwitchService>(_ => new TwitchService());
        services.AddSingleton<IKickService>(_ => new KickService());
        services.AddSingleton<IJetKvmService>(_ => new JetKvmService());
        services.AddSingleton<ISpotifyService, SpotifyService>();
        services.AddSingleton<IVlcService, VlcService>();
        services.AddSingleton<IVoicemodService, VoicemodService>();
        services.AddSingleton<IHomeAssistantService, HomeAssistantService>();
        services.AddSingleton<IYouTubeService>(_ => new YouTubeService());
        services.AddSingleton<IStreamlabsService>(_ => new StreamlabsService());
        services.AddSingleton<IRazerService, RazerService>();
        services.AddSingleton<IWootingService, WootingService>();
        services.AddSingleton<IStreamDeckPluginHost, StreamDeckPluginHost>();
        services.AddSingleton<IPluginCatalogService, PluginCatalogService>();

        // Core actions & rendering & localization
        services.AddSingleton<ILocalizationService>(_ => LocalizationService.Instance);
        services.AddSingleton<IKeyInteractionEngine, KeyInteractionEngine>();
        services.AddSingleton<IKeyRenderer>(sp => new KeyRenderer(sp.GetRequiredService<IMasterHubManager>()));
        services.AddSingleton<IAnimatedKeyRenderer>(sp => new AnimatedKeyRenderer(sp.GetRequiredService<IKeyRenderer>()));
        services.AddSingleton<IAppIconExtractorService, AppIconExtractorService>();

        // Avalonia Presentation Services
        services.AddSingleton<IMasterHubDeviceService, MasterHubDeviceService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ITrayService, TrayService>();

        // ViewModels
        services.AddSingleton<NavigationViewModel>();
        services.AddSingleton<CanvasViewModel>();
        services.AddSingleton<DeviceHeaderViewModel>();
        services.AddSingleton<ActionLibraryViewModel>();
        services.AddSingleton<InspectorViewModel>();
        services.AddSingleton<MasterHub.Avalonia.ViewModels.Pages.ProfilesPageViewModel>();
        services.AddSingleton<MasterHub.Avalonia.ViewModels.Pages.ActionStorePageViewModel>();
        services.AddSingleton<MasterHub.Avalonia.ViewModels.Pages.LogsPageViewModel>();
        services.AddSingleton<MasterHub.Avalonia.ViewModels.Pages.SettingsPageViewModel>();
        services.AddSingleton<MainViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }
}