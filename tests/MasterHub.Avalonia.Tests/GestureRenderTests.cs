using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels.Canvas;
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
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;

namespace MasterHub.UiTests;

/// <summary>
/// Press / Hold / Double share one picture: the key's. A Hold or Double action runs on a throw-away key with the real key's
/// index; drawing that one put its bare default look on the LCD of the real key.
/// </summary>
public sealed class GestureRenderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_gesture_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private (CanvasViewModel Canvas, RecordingProxy<IMasterHubDeviceService> Deck) Create()
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var (deck, log) = RecordingProxy<IMasterHubDeviceService>.Create();
        var canvas = new CanvasViewModel(deck, F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles);
        canvas.ApplyProfile(profiles.GetActiveProfile());
        log.Clear();
        return (canvas, log);
    }

    private static List<byte[]> ImagesOf(RecordingProxy<IMasterHubDeviceService> deck, int key) =>
        deck.Calls.Where(c => c.Name == "SetKeyImage" && (int)c.Args[0]! == key).Select(c => (byte[])c.Args[1]!).ToList();

    [AvaloniaTheory]
    [InlineData(KeyGesture.Hold)]
    [InlineData(KeyGesture.Double)]
    public void AGestureThatRedrawsTheKey_DrawsTheRealKey_NotABareOne(KeyGesture gesture)
    {
        var (canvas, deck) = Create();
        var key = canvas.Keys[0];
        key.CustomTitle = "REAL";
        key.AssignedAction = DeckActionType.MediaNext;
        key.HoldAction = DeckActionType.MediaStop;
        key.DoubleAction = DeckActionType.MediaStop;
        canvas.RenderKeyVisual(key, sendToHardware: true);
        byte[] real = ImagesOf(deck, 0).Last();
        deck.Clear();

        canvas.RunGesture(0, gesture);   // "stop" redraws the key but leaves its state as it was: nobody redraws it afterwards

        var drawn = ImagesOf(deck, 0);
        Assert.NotEmpty(drawn);
        Assert.Equal(real, drawn.Last());
    }

    [AvaloniaFact]
    public void ARedrawAskedForAThrowAwayKey_ComesOutAsTheRealKey()
    {
        var (canvas, deck) = Create();
        var real = canvas.Keys[0];
        real.CustomTitle = "REAL";
        canvas.RenderKeyVisual(real, sendToHardware: true);
        byte[] expected = ImagesOf(deck, 0).Last();
        deck.Clear();

        using var temp = new MasterHub.Avalonia.ViewModels.KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.MediaStop };
        canvas.RenderKeyVisual(temp, sendToHardware: true);

        Assert.Equal(expected, ImagesOf(deck, 0).Single());
    }
}
