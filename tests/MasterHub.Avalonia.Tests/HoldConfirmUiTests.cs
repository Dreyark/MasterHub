using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Dialogs;
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
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;

namespace MasterHub.UiTests;

/// <summary>T-03: the hold bar on power keys and "clear all keys" behind a confirmation.</summary>
public sealed class HoldConfirmUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_holdbar_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private (CanvasViewModel Canvas, RecordingProxy<IMasterHubDeviceService> Deck) Create(IConfirmDialogService? confirm = null)
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var (deck, log) = RecordingProxy<IMasterHubDeviceService>.Create();
        var canvas = new CanvasViewModel(deck, F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles,
            confirmDialog: confirm);
        canvas.ApplyProfile(profiles.GetActiveProfile());
        log.Clear();
        return (canvas, log);
    }

    [AvaloniaFact]
    public void HoldingAPowerKey_SendsTheKeyWithABar_AndReleaseDrawsItAgain()
    {
        var (canvas, deck) = Create();
        var key = canvas.Keys[0];
        key.AssignedAction = DeckActionType.SystemShutdown;
        canvas.RenderKeyVisual(key, sendToHardware: false);
        byte[] plain = key.RenderedJpegBytes!;
        deck.Clear();

        canvas.OnHoldConfirmKey(key, isPressed: true);

        Assert.True(canvas.HoldProgress.IsRunning(0));
        Assert.Equal(1, deck.Count("SetKeyImage"));
        Assert.NotNull(canvas.LastHoldProgressPicture);
        Assert.NotEqual(plain, canvas.LastHoldProgressPicture);
        Assert.Equal(plain, key.RenderedJpegBytes);   // the window keeps the key's own picture

        canvas.TickHoldProgress();
        Assert.Equal(2, deck.Count("SetKeyImage"));

        canvas.OnHoldConfirmKey(key, isPressed: false);
        Assert.False(canvas.HoldProgress.IsAnyRunning);
        Assert.Equal(3, deck.Count("SetKeyImage"));   // drawn again without the bar
    }

    [AvaloniaFact]
    public void OrdinaryKeys_AndOptedOutPowerKeys_ShowNoBar()
    {
        var (canvas, deck) = Create();
        var media = canvas.Keys[0];
        media.AssignedAction = DeckActionType.MediaNext;
        var oneTap = canvas.Keys[1];
        oneTap.AssignedAction = DeckActionType.SystemRestart;
        oneTap.SkipPowerConfirmation = true;

        deck.Clear();
        canvas.OnHoldConfirmKey(media, true);
        canvas.OnHoldConfirmKey(oneTap, true);

        Assert.False(canvas.HoldProgress.IsAnyRunning);
        Assert.Equal(0, deck.Count("SetKeyImage"));
    }

    [AvaloniaFact]
    public void AKeyWithADestructiveHoldGesture_AlsoShowsTheBar()
    {
        var (canvas, _) = Create();
        var key = canvas.Keys[2];
        key.AssignedAction = DeckActionType.MediaPlayPause;
        key.HoldAction = DeckActionType.SystemSleep;

        Assert.Equal(PowerActionGuard.HoldToConfirmMs, key.HoldToConfirmMs);
        canvas.OnHoldConfirmKey(key, true);
        Assert.True(canvas.HoldProgress.IsRunning(2));
    }

    [AvaloniaFact]
    public async Task ClearAllKeys_AsksFirst_AndOneUndoBringsThemBack()
    {
        var confirm = new ConfirmDialogService();
        var (canvas, _) = Create(confirm);
        canvas.Keys[1].AssignedAction = DeckActionType.MediaNext;
        canvas.Keys[4].AssignedAction = DeckActionType.MediaPrevious;

        var cancelled = canvas.ClearAllKeysAsync();
        Assert.NotNull(confirm.Current);
        Assert.Contains("2", confirm.Current!.Message);
        confirm.Cancel();
        Assert.False(await cancelled);
        Assert.Equal(DeckActionType.MediaNext, canvas.Keys[1].AssignedAction);

        var accepted = canvas.ClearAllKeysAsync();
        confirm.Accept();
        Assert.True(await accepted);
        Assert.Equal(DeckActionType.None, canvas.Keys[1].AssignedAction);
        Assert.Equal(DeckActionType.None, canvas.Keys[4].AssignedAction);

        canvas.UndoEdit();
        Assert.Equal(DeckActionType.MediaNext, canvas.Keys[1].AssignedAction);
        Assert.Equal(DeckActionType.MediaPrevious, canvas.Keys[4].AssignedAction);
    }

    [AvaloniaFact]
    public async Task ClearAllKeys_OnAnEmptyPage_AsksNothing()
    {
        var confirm = new ConfirmDialogService();
        var (canvas, _) = Create(confirm);
        foreach (var k in canvas.Keys) k.AssignedAction = DeckActionType.None;

        Assert.False(await canvas.ClearAllKeysAsync());
        Assert.Null(confirm.Current);
    }
}
