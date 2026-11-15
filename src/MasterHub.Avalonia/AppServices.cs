using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Devices;
using MasterHub.Core.Integrations.Discord; using MasterHub.Core.Integrations.HomeAssistant; using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Streamlabs; using MasterHub.Core.Integrations.StreamDeck; using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Kick; using MasterHub.Core.Integrations.JetKvm; using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Vlc; using MasterHub.Core.Integrations.Voicemod; using MasterHub.Core.Integrations.Twitch; using MasterHub.Core.Integrations.YouTube;
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

/// <summary>Rejestracja usług DI aplikacji (wydzielone z App.axaml.cs - limit 150 linii dla plików widoków).</summary>
public partial class App
{
    private static void ConfigureServices(IServiceCollection services)
    {
        // Core hardware & audio/system services
        services.AddSingleton<IMasterHubManager, MasterHubManager>();
        services.AddSingleton<IAudioService, WindowsAudioService>();
        services.AddSingleton<IAudioDeviceService, WindowsAudioDeviceService>();
        services.AddSingleton<MasterHub.Core.Profiles.Packages.IMediaStore>(_ => new MasterHub.Core.Profiles.Packages.MediaStore());
        services.AddSingleton<MasterHub.Core.Profiles.Packages.ProfilePackageService>();
        services.AddSingleton<MasterHub.Core.Profiles.Templates.IProfileTemplateService, MasterHub.Core.Profiles.Templates.ProfileTemplateService>();
        services.AddSingleton(sp => new MasterHub.Core.LiveValues.LiveTokenStore(sp.GetRequiredService<ISecretProtectionService>()));
        services.AddSingleton<MasterHub.Core.LiveValues.ILiveValueService>(sp => new MasterHub.Core.LiveValues.LiveValueService(
            sp.GetRequiredService<MasterHub.Core.LiveValues.LiveTokenStore>(), null,
            entityId => sp.GetService<IHomeAssistantService>()?.GetEntity(entityId)?.State));
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
        services.AddSingleton<MasterHub.Core.Macros.IMacroService, MasterHub.Core.Macros.MacroService>();
        services.AddSingleton<IStreamDeckPluginHost, StreamDeckPluginHost>();
        services.AddSingleton<IPluginCatalogService, PluginCatalogService>();

        // Core actions & rendering & localization
        services.AddSingleton<ILocalizationService>(_ => LocalizationService.Instance);
        services.AddSingleton<IKeyInteractionEngine, KeyInteractionEngine>();
        services.AddSingleton(sp => new MasterHub.Core.Devices.DeckStatusLightService(
            sp.GetRequiredService<IObsService>(), sp.GetRequiredService<IDiscordService>(), sp.GetRequiredService<IAudioService>(),
            () => sp.GetRequiredService<IProfileService>().GetAppSettings().StatusLights));
        // T-25: what connected apps (and Claude Code sessions) report; keys and the edge light read it
        services.AddSingleton<MasterHub.Core.Status.IStatusBoard>(_ => new MasterHub.Core.Status.StatusBoard());
        services.AddSingleton<MasterHub.Core.Status.IAgentTracker>(sp => new MasterHub.Core.Status.AgentTracker(sp.GetRequiredService<MasterHub.Core.Status.IStatusBoard>()));
        services.AddSingleton<MasterHub.Core.Dialogs.IConfirmDialogService, MasterHub.Core.Dialogs.ConfirmDialogService>();
        services.AddSingleton<MasterHub.Core.Notices.INoticeCenter>(MasterHub.Core.Notices.NoticeCenter.Shared);   // T-34 #8 banners
        services.AddSingleton<MasterHub.Core.SystemControl.IElevationProbe, MasterHub.Core.SystemControl.WindowsElevationProbe>();
        services.AddSingleton<MasterHub.Core.Diagnostics.OutboundCallCounter>();   // T-34 #5
        services.AddSingleton<IKeyRenderer>(sp => new KeyRenderer(sp.GetRequiredService<IMasterHubManager>()));
        services.AddSingleton<IAnimatedKeyRenderer>(sp => new AnimatedKeyRenderer(sp.GetRequiredService<IKeyRenderer>()));
        services.AddSingleton<IAppIconExtractorService, AppIconExtractorService>();
        services.AddSingleton<MasterHub.Core.Apps.IInstalledAppsService>(_ => new MasterHub.Core.Apps.InstalledAppsService());

        // Assistant (T-37): profiles that prepare themselves, suggestions, counting. All of it is off until the user says yes.
        services.AddSingleton<MasterHub.Core.Profiles.Auto.IAssistSettingsStore>(sp =>
            new MasterHub.Core.Profiles.Auto.AssistSettingsStore(sp.GetRequiredService<IProfileService>()));
        services.AddSingleton<MasterHub.Core.Profiles.Auto.IAppUsageService>(_ => new MasterHub.Core.Profiles.Auto.AppUsageService());
        services.AddSingleton<MasterHub.Core.Profiles.Auto.IPressCounter>(sp =>
        {
            var store = sp.GetRequiredService<MasterHub.Core.Profiles.Auto.IAssistSettingsStore>();
            return new MasterHub.Core.Profiles.Auto.PressCounter(() => store.PressCountingOn);
        });
        services.AddSingleton<MasterHub.Core.Profiles.Auto.IAutoProfileService>(sp => new MasterHub.Core.Profiles.Auto.AutoProfileService(
            sp.GetRequiredService<IProfileService>(), sp.GetRequiredService<MasterHub.Core.Profiles.Templates.IProfileTemplateService>(),
            sp.GetRequiredService<MasterHub.Core.Apps.IInstalledAppsService>(), sp.GetRequiredService<MasterHub.Core.Profiles.Auto.IAssistSettingsStore>(),
            () => LocalizationService.Instance.CurrentLanguageCode ?? "en"));
        services.AddSingleton<MasterHub.Core.Profiles.Auto.INudgeService>(sp => new MasterHub.Core.Profiles.Auto.NudgeService(
            sp.GetRequiredService<IProfileService>(), sp.GetRequiredService<MasterHub.Core.Profiles.Auto.IAssistSettingsStore>(),
            sp.GetRequiredService<MasterHub.Core.Profiles.Auto.IAppUsageService>(), sp.GetRequiredService<MasterHub.Core.Profiles.Auto.IPressCounter>(),
            sp.GetRequiredService<MasterHub.Core.Apps.IInstalledAppsService>()));

        services.AddSingleton<MasterHub.Core.Updates.IUpdateService>(sp => new MasterHub.Core.Updates.UpdateService(sp.GetRequiredService<IProfileService>()));

        // Avalonia Presentation Services
        services.AddSingleton<IMasterHubDeviceService, MasterHubDeviceService>();
        services.AddSingleton<MasterHub.Core.Timers.ITimerService>(_ => new MasterHub.Core.Timers.TimerService());
        services.AddSingleton<MasterHub.Core.Audio.ISoundboardService>(sp => new MasterHub.Core.Audio.SoundboardService(
            new MasterHub.Core.Audio.NAudioSoundOutputFactory(), sp.GetService<IAudioDeviceService>()));
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ITrayService, TrayService>();

        // ViewModels
        services.AddSingleton<NavigationViewModel>();
        services.AddSingleton<CanvasViewModel>();
        services.AddSingleton<DeviceHeaderViewModel>();
        services.AddSingleton<ActionLibraryViewModel>();
        services.AddSingleton<InspectorViewModel>();
        services.AddSingleton<MasterHub.Avalonia.ViewModels.Pages.ProfilesPageViewModel>();
        // Lokalne API (loopback HTTP + token) dla CLI / skryptów / MCP – start w App, stop przy zamykaniu.
        services.AddSingleton<MasterHub.Core.Api.ILocalApiServer>(sp => new MasterHub.Core.Api.LocalApiServer(
            sp.GetRequiredService<IProfileService>(), sp.GetRequiredService<ISecretProtectionService>(),
            sp.GetRequiredService<IMasterHubManager>(), sp.GetRequiredService<ISmartProfileService>())
        {
            Status = sp.GetRequiredService<MasterHub.Core.Status.IStatusBoard>(),
            Agents = sp.GetRequiredService<MasterHub.Core.Status.IAgentTracker>(),
            Host = new MasterHub.Avalonia.Services.LocalApiHost(
                sp.GetRequiredService<MasterHub.Avalonia.ViewModels.Pages.ProfilesPageViewModel>(),
                sp.GetRequiredService<CanvasViewModel>(), sp.GetRequiredService<IProfileService>())
        });
        services.AddSingleton<MasterHub.Avalonia.ViewModels.Pages.ActionStorePageViewModel>();
        services.AddSingleton<MasterHub.Avalonia.ViewModels.Pages.MacrosPageViewModel>();
        services.AddSingleton<MasterHub.Avalonia.ViewModels.Pages.LogsPageViewModel>();
        services.AddSingleton<MasterHub.Avalonia.ViewModels.Pages.SettingsPageViewModel>();
        services.AddSingleton<MainViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }
}
