using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Spotify;
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

namespace MasterHub.UiTests;

/// <summary>
/// The Settings page saves its own few settings (language, autostart, hold time …). It once built a whole new settings object for that, which
/// reset everything the other cards own – the assistant's opt-in and answers among them – on the next save.
/// </summary>
public sealed class SettingsKeepOtherCardsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_settings_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    [AvaloniaFact]
    public void SavingAnotherSetting_KeepsTheAssistantAndTheOtherCards()
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var device = F<IMasterHubDeviceService>();
        var canvas = new CanvasViewModel(device, F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles);
        var settings = new SettingsPageViewModel(device, canvas, F<IDiscordService>(), F<IObsService>(), F<ITwitchService>(),
            F<IKickService>(), F<IJetKvmService>(), F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(),
            F<IHomeAssistantService>(), profiles, F<ISystemControlService>(), F<IStartupService>());

        var app = profiles.GetAppSettings();
        app.Assist.Enabled = true;
        app.Assist.Offered.Add("template:gaming");
        app.StatusLights.Enabled = true;
        app.BackupMirrorPath = @"D:\Mirror";

        settings.HoldDurationMs = settings.HoldDurationMs + 100;   // saves the app settings

        var saved = profiles.GetAppSettings();
        Assert.True(saved.Assist.Enabled);
        Assert.Contains("template:gaming", saved.Assist.Offered);
        Assert.True(saved.StatusLights.Enabled);
        Assert.Equal(@"D:\Mirror", saved.BackupMirrorPath);

        // and it reached the file, not only memory
        var reloaded = new ProfileService(_dir, new SecretProtectionService()).GetAppSettings();
        Assert.True(reloaded.Assist.Enabled);
        Assert.Contains("template:gaming", reloaded.Assist.Offered);
    }

    /// <summary>T-34 #11: "Make a language file" writes the English template where the status line says.</summary>
    [AvaloniaFact]
    public void MakeLanguageFile_WritesTheTemplate_AndSaysWhere()
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var device = F<IMasterHubDeviceService>();
        var canvas = new CanvasViewModel(device, F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles);
        var settings = new SettingsPageViewModel(device, canvas, F<IDiscordService>(), F<IObsService>(), F<ITwitchService>(),
            F<IKickService>(), F<IJetKvmService>(), F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(),
            F<IHomeAssistantService>(), profiles, F<ISystemControlService>(), F<IStartupService>())
        {
            LanguagesFolderOverride = Path.Combine(_dir, "Languages")
        };

        settings.MakeLanguageFileCommand.Execute(null);

        string template = Path.Combine(_dir, "Languages", MasterHub.Core.Localization.LocalizationService.TemplateFileName);
        Assert.True(File.Exists(template));
        Assert.Contains(template, settings.LanguageFileStatus);
    }
}
