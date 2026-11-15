using System.Reflection;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using MasterHub.Avalonia;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Avalonia.ViewModels.Inspector;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.YouTube;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;
using MasterHub.Core.Devices;
using NavigationPage = MasterHub.Avalonia.Services.NavigationPage;

namespace MasterHub.UiTests;

/// <summary>Atrapa interfejsu zwracająca puste kolekcje / "" / default (nie rzuca na odczycie kolekcji).</summary>
public class EmptyProxy<T> : DispatchProxy where T : class
{
    public static T Create() => DispatchProxy.Create<T, EmptyProxy<T>>();

    protected override object? Invoke(MethodInfo? m, object?[]? args)
    {
        var t = m?.ReturnType;
        if (t == null || t == typeof(void)) return null;
        if (t == typeof(Task)) return Task.CompletedTask;
        if (t == typeof(string)) return "";
        if (t.IsArray) return Array.CreateInstance(t.GetElementType()!, 0);
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var arg = t.GetGenericArguments()[0];
            if (def == typeof(IReadOnlyList<>) || def == typeof(IEnumerable<>) || def == typeof(IReadOnlyCollection<>))
                return Array.CreateInstance(arg, 0);
            if (def == typeof(Task<>))
                return typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(arg).Invoke(null, new[] { arg.IsValueType ? Activator.CreateInstance(arg) : (arg == typeof(string) ? "" : null) });
        }
        return t.IsValueType ? Activator.CreateInstance(t) : null;
    }
}

/// <summary>Pełne okno aplikacji (prawdziwy MainWindow + ViewModele) na atrapach usług - podgląd całego UI bez sprzętu.</summary>
public class StudioShotTests
{
    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    internal static MainViewModel BuildMain(string tmp, NavigationPage page)
    {
        var profiles = new ProfileService(tmp, new SecretProtectionService());
        var device = F<IMasterHubDeviceService>();
        var canvas = new CanvasViewModel(device, F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles);
        var library = new ActionLibraryViewModel();
        var inspector = new InspectorViewModel(new DeviceHeaderViewModel(device), library, canvas);
        var navService = new NavigationService();
        var nav = new NavigationViewModel(navService, device);
        var macros = new MacrosPageViewModel(F<MasterHub.Core.Macros.IMacroService>());
        var settings = new SettingsPageViewModel(device, canvas, F<IDiscordService>(), F<IObsService>(), F<ITwitchService>(),
            F<IKickService>(), F<IJetKvmService>(), F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(),
            F<IHomeAssistantService>(), profiles, F<ISystemControlService>(), F<IStartupService>());
        var main = new MainViewModel(nav, canvas, inspector, new ProfilesPageViewModel(profiles),
            new ActionStorePageViewModel(F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<IStreamDeckPluginHost>(), F<IPluginCatalogService>()),
            macros, new LogsPageViewModel(device), settings, device, profiles);
        nav.SelectPage(page);
        return main;
    }

    [AvaloniaTheory]
    [InlineData(NavigationPage.Studio, "full_studio", 1600, 900)]
    [InlineData(NavigationPage.Studio, "full_studio_narrow", 1280, 760)]
    [InlineData(NavigationPage.Profiles, "full_profiles", 1440, 800)]
    [InlineData(NavigationPage.ActionStore, "full_store", 1440, 800)]
    [InlineData(NavigationPage.Settings, "full_settings", 1440, 900)]
    [InlineData(NavigationPage.Macros, "full_macros", 1440, 900)]
    [InlineData(NavigationPage.Logs, "full_logs", 1440, 800)]
    public void FullWindow(NavigationPage page, string name, int w, int h)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mh_full_" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = BuildMain(tmp, page);
            var window = new MainWindow { DataContext = vm, Width = w, Height = h };
            window.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(300);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Directory.CreateDirectory(Shot.Dir);
            var frame = window.CaptureRenderedFrame();
#pragma warning disable CS0618
            frame?.Save(Path.Combine(Shot.Dir, name + ".png"));
#pragma warning restore CS0618
            window.Close();
            Assert.NotNull(frame);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
