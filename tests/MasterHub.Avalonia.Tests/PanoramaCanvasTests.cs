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
using SkiaSharp;

namespace MasterHub.UiTests;

/// <summary>The wallpaper of the whole keypad on the canvas: keys with text no longer cut a hole in it, and it can be zoomed and moved as one piece.</summary>
public sealed class PanoramaCanvasTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_panoc_" + Guid.NewGuid().ToString("N"));

    public PanoramaCanvasTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        PanoramaMedia.ClearCache();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private string Wallpaper()
    {
        string path = Path.Combine(_dir, "wall.png");
        using var bitmap = new SKBitmap(480, 272);
        bitmap.Erase(new SKColor(20, 60, 200));   // a plain blue wallpaper
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private (CanvasViewModel Canvas, RecordingProxy<IMasterHubDeviceService> Deck, ProfileService Profiles) Create()
    {
        var profiles = new ProfileService(Path.Combine(_dir, "profiles"), new SecretProtectionService());
        var (deck, log) = RecordingProxy<IMasterHubDeviceService>.Create();
        var canvas = new CanvasViewModel(deck, F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles);
        canvas.ApplyProfile(profiles.GetActiveProfile());
        log.Clear();
        return (canvas, log, profiles);
    }

    private static void ShowWallpaper(CanvasViewModel canvas, string path)
    {
        canvas.WallpaperMode = DeckWallpaperMode.PanoramicDeck;
        canvas.PanoramicWallpaperPath = path;
        canvas.SyncPanoramicWallpaperStateToKeys();
        canvas.RestartPanorama();
    }

    private static SKColor PixelOf(byte[] jpeg, int x, int y)
    {
        using var bitmap = SKBitmap.Decode(jpeg);
        return bitmap.GetPixel(x, y);
    }

    private static int BrightPixels(byte[] jpeg, int x0, int y0, int size = 72)
    {
        using var bitmap = SKBitmap.Decode(jpeg);
        int count = 0;
        for (int y = y0; y < y0 + size; y++)
            for (int x = x0; x < x0 + size; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red > 180 && p.Green > 180 && p.Blue > 180) count++;
            }
        return count;
    }

    [AvaloniaFact]
    public void AKeyWithText_ShowsItsTextOnTheWallpaper_AndAnEmptyKeyShowsOnlyTheWallpaper()
    {
        var (canvas, deck, _) = Create();
        var key = canvas.Keys[0];
        key.AssignedAction = DeckActionType.MediaNext;
        key.CustomTitle = "NEXT";
        deck.Clear();

        ShowWallpaper(canvas, Wallpaper());

        byte[] panel = (byte[])deck.Calls.Last(c => c.Name == "SetPanoramicWallpaper").Args[0]!;
        Assert.True(BrightPixels(panel, 0, 0) > 15, "the key's text is on the wallpaper");
        Assert.Equal(0, BrightPixels(panel, 408, 200));   // key 15 is empty: nothing but the wallpaper
        var (x, y) = PanoramaDeck.KeyOrigin(14);
        var wall = PixelOf(panel, x + 36, y + 36);
        Assert.True(wall.Blue > 150 && wall.Red < 80, "the wallpaper is blue behind the empty key");

        // the key's own area is the wallpaper too (not a black or coloured tile around the text)
        var behindText = PixelOf(panel, 3, 3);
        Assert.True(behindText.Blue > 150 && behindText.Red < 80, "the wallpaper shows around the text");
    }

    [AvaloniaFact]
    public void OnAWallpaperDeck_TheKeysAreNotSentOneByOne()
    {
        var (canvas, deck, _) = Create();
        canvas.Keys[0].AssignedAction = DeckActionType.MediaNext;
        deck.Clear();

        ShowWallpaper(canvas, Wallpaper());
        canvas.RenderKeyVisual(canvas.Keys[0], sendToHardware: true);

        Assert.Equal(0, deck.Count("SetKeyImage"));
        Assert.True(deck.Count("SetPanoramicWallpaper") >= 1);
    }

    [AvaloniaFact]
    public void TheKeysPreview_IsTheWallpaperPieceWithItsText()
    {
        var (canvas, _, _) = Create();
        canvas.Keys[0].AssignedAction = DeckActionType.MediaNext;
        canvas.Keys[0].CustomTitle = "NEXT";

        ShowWallpaper(canvas, Wallpaper());
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        byte[] preview = canvas.Keys[0].RenderedJpegBytes!;
        Assert.True(BrightPixels(preview, 0, 0) > 15);
        var corner = PixelOf(preview, 2, 2);
        Assert.True(corner.Blue > 150, "the wallpaper, not a black corner");
    }

    [AvaloniaFact]
    public void ChangingAKey_UpdatesTheWallpaperPanel()
    {
        var (canvas, deck, _) = Create();
        ShowWallpaper(canvas, Wallpaper());
        deck.Clear();

        canvas.Keys[3].AssignedAction = DeckActionType.MediaNext;
        canvas.Keys[3].CustomTitle = "HI";
        canvas.RenderKeyVisual(canvas.Keys[3], sendToHardware: true);
        canvas.Panorama!.Refresh();   // the coalesced refresh, without waiting for its timer

        byte[] panel = (byte[])deck.Calls.Last(c => c.Name == "SetPanoramicWallpaper").Args[0]!;
        var (x, y) = PanoramaDeck.KeyOrigin(3);
        Assert.True(BrightPixels(panel, x, y) > 10);
    }

    [AvaloniaFact]
    public void ZoomAndMove_AreKeptInTheProfile_AndRestoredWithIt()
    {
        var (canvas, _, profiles) = Create();
        string path = Wallpaper();
        ShowWallpaper(canvas, path);

        canvas.ApplyPanoramicTransform(200, 30, -20);
        canvas.OnPanoramaTransformSettled(null, EventArgs.Empty);

        var saved = profiles.GetActiveProfile();
        Assert.Equal((200, 30, -20), (saved.PanoramicWallpaperScale, saved.PanoramicWallpaperOffsetX, saved.PanoramicWallpaperOffsetY));

        var (other, _, _) = Create();
        other.ApplyProfile(saved);
        Assert.Equal((200, 30, -20), (other.PanoramicWallpaperScale, other.PanoramicWallpaperOffsetX, other.PanoramicWallpaperOffsetY));
    }

    [AvaloniaFact]
    public void TheValues_AreKeptInRange()
    {
        var (canvas, _, _) = Create();
        ShowWallpaper(canvas, Wallpaper());

        canvas.ApplyPanoramicTransform(9000, 500, -500);

        Assert.Equal((400, 100, -100), (canvas.PanoramicWallpaperScale, canvas.PanoramicWallpaperOffsetX, canvas.PanoramicWallpaperOffsetY));
    }

    [AvaloniaFact]
    public void TheInspectorSliders_ReachTheCanvas()
    {
        var (canvas, _, _) = Create();
        ShowWallpaper(canvas, Wallpaper());
        var key = canvas.Keys[0];

        key.PanoramicWallpaperScale = 150;
        key.PanoramicWallpaperOffsetX = -40;

        Assert.Equal((150, -40), (canvas.PanoramicWallpaperScale, canvas.PanoramicWallpaperOffsetX));
    }

    [AvaloniaFact]
    public void TheCanvasChangingTheValues_DoesNotBounceBack()
    {
        var (canvas, _, _) = Create();
        ShowWallpaper(canvas, Wallpaper());
        int calls = 0;
        foreach (var k in canvas.Keys) k.PanoramicTransformRequested = (_, _, _) => calls++;

        canvas.PanoramicWallpaperScale = 180;
        canvas.SyncPanoramicWallpaperStateToKeys();

        Assert.Equal(0, calls);   // a value the canvas set is mirrored, it is not a user's slider move
        Assert.Equal(180, canvas.Keys[5].PanoramicWallpaperScale);
    }

    [AvaloniaFact]
    public void ResetCentresTheWallpaper()
    {
        var (canvas, _, _) = Create();
        ShowWallpaper(canvas, Wallpaper());
        canvas.ApplyPanoramicTransform(250, 60, 60);

        canvas.Keys[0].ResetPanoramicTransformCommand.Execute(null);

        Assert.Equal((100, 0, 0), (canvas.PanoramicWallpaperScale, canvas.PanoramicWallpaperOffsetX, canvas.PanoramicWallpaperOffsetY));
        Assert.Equal((100, 0, 0), (canvas.Keys[0].PanoramicWallpaperScale, canvas.Keys[0].PanoramicWallpaperOffsetX, canvas.Keys[0].PanoramicWallpaperOffsetY));
    }

    [AvaloniaFact]
    public void RemovingTheWallpaper_GivesTheKeysTheirOwnPicturesBack()
    {
        var (canvas, deck, _) = Create();
        canvas.Keys[0].AssignedAction = DeckActionType.MediaNext;
        ShowWallpaper(canvas, Wallpaper());
        deck.Clear();

        canvas.ClearPanoramicWallpaper();

        Assert.False(canvas.Panorama!.HasSource);
        Assert.True(deck.Count("SetKeyImage") >= 1);
        Assert.Equal((100, 0, 0), (canvas.PanoramicWallpaperScale, canvas.PanoramicWallpaperOffsetX, canvas.PanoramicWallpaperOffsetY));
    }

    [AvaloniaFact]
    public void ASavedProfileWithoutTheNewFields_StillLoads()
    {
        // an old profile has no zoom / move: the defaults (100 %, centred) apply
        var model = new ProfileModel();
        Assert.Equal((100, 0, 0), (model.PanoramicWallpaperScale, model.PanoramicWallpaperOffsetX, model.PanoramicWallpaperOffsetY));
        Assert.Equal((100, 0, 0), (new KeyPageModel().PanoramicWallpaperScale, new KeyPageModel().PanoramicWallpaperOffsetX, new KeyPageModel().PanoramicWallpaperOffsetY));
    }
}
