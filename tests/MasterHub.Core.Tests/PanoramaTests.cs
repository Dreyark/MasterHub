using MasterHub.Core.Actions;
using MasterHub.Core.Rendering;
using SkiaSharp;

namespace MasterHub.Core.Tests;

/// <summary>The wallpaper of the whole keypad: composing it with the keys' text, moving it as one piece, playing it.</summary>
public sealed class PanoramaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_pano_" + Guid.NewGuid().ToString("N"));

    public PanoramaTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        PanoramaMedia.ClearCache();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static byte[] Jpeg(SKColor color, int w = ButtonImageRenderer.PanelWidth, int h = ButtonImageRenderer.PanelHeight)
    {
        using var bitmap = new SKBitmap(w, h);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = JpegEncoding.Encode(image, 95);
        return data.ToArray();
    }

    private static byte[] Png(SKColor color, int size = 72)
    {
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKColor PixelOf(byte[] jpeg, int x, int y)
    {
        using var bitmap = SKBitmap.Decode(jpeg);
        return bitmap.GetPixel(x, y);
    }

    private static bool Near(SKColor actual, SKColor expected, int tolerance = 40) =>
        Math.Abs(actual.Red - expected.Red) <= tolerance && Math.Abs(actual.Green - expected.Green) <= tolerance && Math.Abs(actual.Blue - expected.Blue) <= tolerance;

    private static readonly SKColor Blue = new(30, 60, 220), Red = new(220, 30, 30), Green = new(30, 200, 60);

    // ── composing ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AStillWallpaper_IsSentOnce_AsOneFullPanel()
    {
        var sent = new List<byte[]>();
        using var deck = new PanoramaDeck(sent.Add, useTimer: false);

        deck.SetSource(new[] { Jpeg(Blue) });

        var panel = Assert.Single(sent);
        using var decoded = SKBitmap.Decode(panel);
        Assert.Equal((480, 272), (decoded.Width, decoded.Height));
        Assert.True(Near(PixelOf(panel, 240, 136), Blue));
        Assert.False(deck.IsAnimated);
    }

    [Fact]
    public void AKeysOverlay_LiesOnItsOwnSpotOfTheWallpaper_NothingElseChanges()
    {
        var sent = new List<byte[]>();
        using var deck = new PanoramaDeck(sent.Add, useTimer: false);
        deck.SetOverlay(7, Png(Red));   // key 8: the second row, third column
        deck.SetSource(new[] { Jpeg(Blue) });

        var (x, y) = PanoramaDeck.KeyOrigin(7);
        var panel = sent.Last();
        Assert.True(Near(PixelOf(panel, x + 36, y + 36), Red), "the key's own spot shows its overlay");
        Assert.True(Near(PixelOf(panel, 10, 10), Blue), "the rest is the wallpaper");
        Assert.True(Near(PixelOf(panel, x + 36, y + 36 + 90), Blue) || y + 36 + 90 >= 272, "another row is the wallpaper");
    }

    [Fact]
    public void ATransparentOverlay_LetsTheWallpaperShowThrough()
    {
        using var deck = new PanoramaDeck(_ => { }, useTimer: false);
        deck.SetOverlay(0, Png(SKColors.Transparent));
        deck.SetSource(new[] { Jpeg(Green) });

        Assert.True(Near(PixelOf(deck.ComposeCurrent()!, 36, 36), Green));
    }

    [Fact]
    public void AKeyWithNothingToAdd_ShowsTheWallpaper_AndRemovingAnOverlayBringsItBack()
    {
        using var deck = new PanoramaDeck(_ => { }, useTimer: false);
        deck.SetSource(new[] { Jpeg(Blue) });
        deck.SetOverlay(0, Png(Red));
        Assert.True(Near(PixelOf(deck.ComposeCurrent()!, 36, 36), Red));

        deck.SetOverlay(0, null);

        Assert.True(Near(PixelOf(deck.ComposeCurrent()!, 36, 36), Blue));
    }

    [Fact]
    public void EveryKeyGetsItsOwnPiece_ForThePreview_AndTheOverlayIsInIt()
    {
        var pieces = new Dictionary<int, byte[]>();
        using var deck = new PanoramaDeck(_ => { }, (key, jpeg) => pieces[key] = jpeg, useTimer: false);
        deck.SetOverlay(3, Png(Red));
        deck.SetSource(new[] { Jpeg(Blue) });

        Assert.Equal(15, pieces.Count);
        using var piece = SKBitmap.Decode(pieces[3]);
        Assert.Equal((72, 72), (piece.Width, piece.Height));
        Assert.True(Near(PixelOf(pieces[3], 36, 36), Red));
        Assert.True(Near(PixelOf(pieces[4], 36, 36), Blue));
    }

    [Fact]
    public void TheKeysSpotsFollowTheKeypadGrid()
    {
        Assert.Equal((0, 0), PanoramaDeck.KeyOrigin(0));
        Assert.Equal((408, 0), PanoramaDeck.KeyOrigin(4));
        Assert.Equal((0, 100), PanoramaDeck.KeyOrigin(5));
        Assert.Equal((408, 200), PanoramaDeck.KeyOrigin(14));
    }

    // ── moving ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AMovingWallpaper_StepsThroughItsFrames_AndLoops()
    {
        var sent = new List<byte[]>();
        using var deck = new PanoramaDeck(sent.Add, useTimer: false);
        deck.SetSource(new[] { Jpeg(Red), Jpeg(Green), Jpeg(Blue) });

        deck.Tick();
        deck.Tick();
        deck.Tick();   // back to the first

        Assert.True(deck.IsAnimated);
        var colours = sent.Select(p => PixelOf(p, 240, 136)).ToList();
        Assert.True(Near(colours[0], Red));
        Assert.True(Near(colours[1], Green));
        Assert.True(Near(colours[2], Blue));
        Assert.True(Near(colours[3], Red));
    }

    [Fact]
    public void TheOverlaysStayOnEveryFrame()
    {
        var sent = new List<byte[]>();
        using var deck = new PanoramaDeck(sent.Add, useTimer: false);
        deck.SetOverlay(0, Png(Red));
        deck.SetSource(new[] { Jpeg(Green), Jpeg(Blue) });

        deck.Tick();

        Assert.All(sent, p => Assert.True(Near(PixelOf(p, 36, 36), Red)));
        Assert.True(Near(PixelOf(sent[1], 300, 200), Blue));
    }

    [Fact]
    public void WhilePaused_NothingMovesOrIsSent_AndItResumesWhereItWas()
    {
        var sent = new List<byte[]>();
        using var deck = new PanoramaDeck(sent.Add, useTimer: false);
        deck.SetSource(new[] { Jpeg(Red), Jpeg(Green) });
        deck.Tick();
        int before = sent.Count;

        deck.SetPaused(true);
        deck.Tick();
        deck.Tick();
        Assert.Equal(before, sent.Count);
        Assert.Equal(1, deck.CurrentFrame);

        deck.SetPaused(false);
        deck.Tick();
        Assert.Equal(0, deck.CurrentFrame);
    }

    [Fact]
    public void AfterStop_ThereIsNoWallpaperToSend()
    {
        var sent = new List<byte[]>();
        using var deck = new PanoramaDeck(sent.Add, useTimer: false);
        deck.SetSource(new[] { Jpeg(Red) });
        sent.Clear();

        deck.Stop();
        deck.Refresh();
        deck.Tick();

        Assert.Empty(sent);
        Assert.False(deck.HasSource);
    }

    [Fact]
    public void AFailingSend_DoesNotStopTheWallpaper()
    {
        int calls = 0;
        using var deck = new PanoramaDeck(_ => { calls++; throw new IOException("the deck is gone"); }, useTimer: false);
        deck.SetSource(new[] { Jpeg(Red), Jpeg(Green) });

        deck.Tick();
        deck.Tick();

        Assert.Equal(3, calls);
    }

    [Fact]
    public void ARealTimer_PlaysTheFrames()
    {
        int sent = 0;
        using var deck = new PanoramaDeck(_ => Interlocked.Increment(ref sent));
        deck.SetSource(new[] { Jpeg(Red), Jpeg(Green) }, fps: 30);

        // a generous limit: a busy build machine can starve the timer thread for seconds
        SpinWait.SpinUntil(() => Volatile.Read(ref sent) >= 4, TimeSpan.FromSeconds(30));

        Assert.True(Volatile.Read(ref sent) >= 4);
    }

    // ── the picture itself: fit, zoom, move ─────────────────────────────────────────────────

    private string TwoHalves()
    {
        string path = Path.Combine(_dir, "halves.png");
        using var bitmap = new SKBitmap(480, 272);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var left = new SKPaint { Color = Red };
            using var right = new SKPaint { Color = Blue };
            canvas.DrawRect(0, 0, 240, 272, left);
            canvas.DrawRect(240, 0, 240, 272, right);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [Fact]
    public void AStillPicture_IsOneFrame_OfTheFullPanelSize()
    {
        var frames = PanoramaMedia.ExtractFrames(TwoHalves());

        var frame = Assert.Single(frames);
        using var decoded = SKBitmap.Decode(frame);
        Assert.Equal((480, 272), (decoded.Width, decoded.Height));
        Assert.True(Near(PixelOf(frame, 60, 136), Red));
        Assert.True(Near(PixelOf(frame, 420, 136), Blue));
    }

    [Fact]
    public void Zooming_AndMoving_PlaceTheWholeWallpaperAsOnePiece()
    {
        string path = TwoHalves();

        var movedRight = PanoramaMedia.ExtractFrames(path, "Cover", 200, 100, 0)[0];
        var movedLeft = PanoramaMedia.ExtractFrames(path, "Cover", 200, -100, 0)[0];

        Assert.True(Near(PixelOf(movedRight, 100, 136), Blue), "moved one way, the blue half fills the left of the panel");
        Assert.True(Near(PixelOf(movedLeft, 400, 136), Red), "moved the other way, the red half fills the right");
    }

    [Fact]
    public void TheDefaults_LeaveThePictureWhereItWas()
    {
        string path = TwoHalves();

        var plain = PanoramaMedia.ExtractFrames(path)[0];
        var explicitDefaults = PanoramaMedia.ExtractFrames(path, "Cover", 100, 0, 0)[0];

        Assert.Equal(plain, explicitDefaults);
    }

    [Fact]
    public void AMissingOrBrokenFile_GivesOneBlankFrame_NeverThrows()
    {
        string broken = Path.Combine(_dir, "broken.mp4");
        File.WriteAllText(broken, "this is not a video");

        Assert.Single(PanoramaMedia.ExtractFrames(Path.Combine(_dir, "nothing.png")));
        Assert.Single(PanoramaMedia.ExtractFrames(null));
        Assert.NotEmpty(PanoramaMedia.ExtractFrames(broken));
    }

    [Theory]
    [InlineData("a.gif", true)]
    [InlineData("a.MP4", true)]
    [InlineData("a.webm", true)]
    [InlineData("a.png", false)]
    [InlineData("a.jpg", false)]
    [InlineData("", false)]
    public void MovingPictures_AreRecognisedByTheirType(string name, bool moving) =>
        Assert.Equal(moving, PanoramaMedia.IsMoving(name));

    // ── the overlay of a key ────────────────────────────────────────────────────────────────

    private static byte[] KeyImage() => ButtonImageRenderer.RenderCustomKeyJpeg(DeckActionType.MediaNext, 1, "Next", "", null, null, null);

    [Fact]
    public void AnOverlay_IsATransparentPicture_ThatKeepsOnlyTheText()
    {
        var overlay = ButtonImageRenderer.RenderOverlayPng(KeyImage);

        using var decoded = SKBitmap.Decode(overlay);
        Assert.Equal((72, 72), (decoded.Width, decoded.Height));
        Assert.Equal(0, decoded.GetPixel(1, 1).Alpha);   // the corner is see-through: no background, no rounded black corner
        bool anyText = false;
        for (int y = 0; y < 72 && !anyText; y++)
            for (int x = 0; x < 72 && !anyText; x++)
                anyText = decoded.GetPixel(x, y).Alpha > 200;
        Assert.True(anyText, "the key's text is there");
    }

    [Fact]
    public void AnOverlay_DoesNotLeakIntoTheNormalKeyPicture()
    {
        var overlay = ButtonImageRenderer.RenderOverlayPng(KeyImage);
        var normal = KeyImage();

        Assert.Equal(0x89, overlay[0]);   // PNG
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, normal[..2]);   // JPEG: the ordinary render is untouched, also from the cache
        Assert.Equal(255, PixelOf(normal, 1, 1).Alpha);
    }
}
