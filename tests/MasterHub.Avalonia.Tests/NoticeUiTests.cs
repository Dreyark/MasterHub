using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Avalonia.Views.Controls;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
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
using MasterHub.Core.Notices;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;

namespace MasterHub.UiTests;

/// <summary>T-34 #8 / #12 in the app: failed actions and elevated targets become banners in the window.</summary>
public sealed class NoticeUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_notice_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private sealed class FakeProbe : IElevationProbe
    {
        public bool IsSelfElevated { get; set; }
        public ForegroundTarget? Foreground { get; set; }
        public ForegroundTarget? GetForeground() => Foreground;
    }

    private CanvasViewModel Create(NoticeCenter notices, FakeProbe probe)
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var canvas = new CanvasViewModel(F<IMasterHubDeviceService>(), F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles)
        {
            Notices = notices,
            ElevationProbe = probe
        };
        canvas.ApplyProfile(profiles.GetActiveProfile());
        return canvas;
    }

    [AvaloniaFact]
    public void AHotkeyKey_WithAnElevatedProgramInFront_WarnsInTheWindow()
    {
        var notices = new NoticeCenter();
        var probe = new FakeProbe { Foreground = new ForegroundTarget(42, "Game", true) };
        var canvas = Create(notices, probe);
        var key = canvas.Keys[0];
        key.AssignedAction = DeckActionType.Hotkey;
        key.ActionParameter = "Ctrl+S";

        canvas.ExecuteKeyAction(key);
        canvas.ExecuteKeyAction(key);   // a second press does not add a second banner

        var notice = Assert.Single(notices.Current);
        Assert.Equal(NoticeLevel.Warning, notice.Level);
        Assert.Contains("Game", notice.Text);
    }

    [AvaloniaFact]
    public void NoWarning_WhenMasterHubIsElevatedToo_OrTheKeySendsNoKeystrokes()
    {
        var notices = new NoticeCenter();
        var probe = new FakeProbe { Foreground = new ForegroundTarget(42, "Game", true), IsSelfElevated = true };
        var canvas = Create(notices, probe);
        canvas.Keys[0].AssignedAction = DeckActionType.Hotkey;
        canvas.ExecuteKeyAction(canvas.Keys[0]);

        probe.IsSelfElevated = false;
        canvas.Keys[1].AssignedAction = DeckActionType.MediaNext;
        canvas.ExecuteKeyAction(canvas.Keys[1]);

        Assert.Empty(notices.Current);
    }

    [AvaloniaFact]
    public void AFailedAction_ShowsItsReason_AsAnErrorBanner()
    {
        var notices = new NoticeCenter();
        var canvas = Create(notices, new FakeProbe());
        var key = canvas.Keys[3];
        key.AssignedAction = DeckActionType.ObsSceneSwitch;

        canvas.ReportActionResult(key, ActionResult.Failed("OBS is not connected"));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var notice = Assert.Single(notices.Current);
        Assert.Equal(NoticeLevel.Error, notice.Level);
        Assert.Contains("4", notice.Text);
        Assert.Contains("OBS is not connected", notice.Text);
    }

    [AvaloniaFact]
    public void TheBanner_MirrorsTheCenter_AndCanBeDismissed_AndRenders()
    {
        var notices = new NoticeCenter();
        var banner = new NoticeBannerViewModel(notices, startTimer: false);
        notices.Post(NoticeLevel.Error, "Key 4: the action did not work – OBS is not connected");
        notices.Post(NoticeLevel.Warning, "\"Game\" runs as administrator, so Windows ignores key presses from MasterHub.");
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, banner.Items.Count);
        string path = Shot.Render(new NoticeBannerView { DataContext = banner }, "notice_banners", 420, 200);
        Assert.True(new FileInfo(path).Length > 2000);

        banner.DismissCommand.Execute(banner.Items[0].Id);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Single(banner.Items);
        banner.Dispose();
    }
}
