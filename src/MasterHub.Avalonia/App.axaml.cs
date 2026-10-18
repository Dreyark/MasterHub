using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Devices;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Streamlabs;
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
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
            desktop.ShutdownRequested += (s, e) => ShutdownHardware();
            desktop.Exit += (s, e) => ShutdownHardware();
        }

        AppDomain.CurrentDomain.ProcessExit += (s, e) => ShutdownHardware();

        base.OnFrameworkInitializationCompleted();
    }

    private static void ShutdownHardware()
    {
        try
        {
            var canvas = Services?.GetService<CanvasViewModel>();
            canvas?.SaveCurrentStateToActiveProfile();
        }
        catch { }

        try
        {
            var deviceService = Services?.GetService<IMasterHubDeviceService>();
            deviceService?.Dispose();
        }
        catch { }

        try
        {
            if (Services is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch { }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core hardware & audio/system services
        services.AddSingleton<IMasterHubManager, MasterHubManager>();
        services.AddSingleton<IAudioService, WindowsAudioService>();
        services.AddSingleton<ISystemControlService, WindowsSystemControlService>();
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
        services.AddSingleton<IYouTubeService>(_ => new YouTubeService());
        services.AddSingleton<IStreamlabsService>(_ => new StreamlabsService());
        services.AddSingleton<IRazerService, RazerService>();
        services.AddSingleton<IWootingService, WootingService>();

        // Core actions & rendering & localization
        services.AddSingleton<ILocalizationService>(_ => LocalizationService.Instance);
        services.AddSingleton<IKeyInteractionEngine, KeyInteractionEngine>();
        services.AddSingleton<IKeyRenderer>(sp => new KeyRenderer(sp.GetRequiredService<IMasterHubManager>()));
        services.AddSingleton<IAnimatedKeyRenderer>(sp => new AnimatedKeyRenderer(sp.GetRequiredService<IKeyRenderer>()));

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