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

        // Poza klasycznym desktopem (np. headless UI testy renderujące widoki do PNG) nie startujemy usług runtime
        // (host pluginów otwiera port i uruchamia procesy, auto-connect integracji, hook zamykania sprzętu).
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (s, e) => ShutdownHardware();

        base.OnFrameworkInitializationCompleted();

        // T-08: a draft that a crash left behind, or the setup tour on a deck with nothing set up (never forced)
        try { Services.GetRequiredService<MasterHub.Avalonia.ViewModels.MainViewModel>().OfferStartupHelp(); } catch { }

        // Makra: wczytanie biblioteki i (tylko przy aktywnych przypisaniach) włączenie globalnych haków wejścia.
        try { Services.GetRequiredService<MasterHub.Core.Macros.IMacroService>().Start(); } catch { }
        _ = Services.GetRequiredService<IStreamDeckPluginHost>().StartAsync();
        // T-25: an app (or a Claude Code session) that waits for the user makes the edge light pulse orange
        try
        {
            var board = Services.GetRequiredService<MasterHub.Core.Status.IStatusBoard>();
            var lights = Services.GetRequiredService<MasterHub.Core.Devices.DeckStatusLightService>();
            board.Changed += () => lights.Attention = board.Count(MasterHub.Core.Status.AppState.Waiting) > 0;
        }
        catch { /* the edge light is optional */ }
        try { Services.GetRequiredService<MasterHub.Core.Api.ILocalApiServer>().Start(); } catch { /* port range busy: the API is optional */ }
        // T-34 #5: one log line a minute with the outbound calls per host
        try { Services.GetRequiredService<MasterHub.Core.Diagnostics.OutboundCallCounter>().Start(MasterHub.Core.Diagnostics.AppLog.Write); } catch { }

        var haSettings = Services.GetRequiredService<IProfileService>().GetHomeAssistantSettings();
        if (haSettings.AutoConnect && !string.IsNullOrWhiteSpace(haSettings.AccessToken))
            _ = Services.GetRequiredService<IHomeAssistantService>().ConnectAsync();
    }

    private static int s_isShuttingDown;

    public static void ShutdownHardware()
    {
        if (System.Threading.Interlocked.Exchange(ref s_isShuttingDown, 1) != 0) return;
        HardwareWatchdog.SignalCleanExit();
        var sp = Services;
        Services = null!;
        try { sp?.GetService<MasterHub.Core.Api.ILocalApiServer>()?.Stop(); } catch { }
        try { sp?.GetService<MasterHub.Core.Diagnostics.OutboundCallCounter>()?.Dispose(); } catch { }
        try { sp?.GetService<MasterHub.Core.Macros.IMacroService>()?.Dispose(); } catch { }
        try { sp?.GetService<IStreamDeckPluginHost>()?.StopAsync().GetAwaiter().GetResult(); } catch { }
        try { sp?.GetService<CanvasViewModel>()?.SaveCurrentStateToActiveProfile(); } catch { }
        try { sp?.GetService<MasterHub.Core.Profiles.Auto.IAppUsageService>()?.Stop(); } catch { }   // T-37: writes what was counted
        try { sp?.GetService<MasterHub.Core.Profiles.Auto.IPressCounter>()?.Flush(); } catch { }
        try { sp?.GetService<IMasterHubDeviceService>()?.Dispose(); } catch { }
        try { sp?.GetService<IMasterHubManager>()?.Dispose(); } catch { }
        try { MasterHubManager.ExtinguishAllHardware(); } catch { }
        try { (sp as IDisposable)?.Dispose(); } catch { }
    }
}
