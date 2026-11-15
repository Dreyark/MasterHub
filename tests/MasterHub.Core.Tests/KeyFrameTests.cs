using MasterHub.Core.Actions;
using MasterHub.Core.Rendering;
using SkiaSharp;

namespace MasterHub.Core.Tests;

/// <summary>
/// A picture on a single LCD key must fill the whole key: no black margin, no rounded corners cut out of it and no outline on top.
/// Keys made of colour and text keep the rounded card.
/// </summary>
public class KeyFrameTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh-keyframe-" + Guid.NewGuid().ToString("N"));

    public KeyFrameTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string SolidPng(SKColor color, int w = 64, int h = 64, string name = "pic.png")
    {
        using var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static bool IsNear(SKColor actual, SKColor expected, int tolerance = 24) =>
        Math.Abs(actual.Red - expected.Red) <= tolerance &&
        Math.Abs(actual.Green - expected.Green) <= tolerance &&
        Math.Abs(actual.Blue - expected.Blue) <= tolerance;

    private static byte[] RenderPictureKey(string picture, bool pressed = false, bool active = false, string? activePicture = null, string fit = "Cover")
        => ButtonImageRenderer.RenderCustomKeyJpeg(DeckActionType.None, 1, "", "", null, null, picture,
            isPressed: pressed, isActiveState: active, activeImagePath: activePicture, imageFitMode: fit, activeImageFitMode: fit);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(71, 0)]
    [InlineData(0, 71)]
    [InlineData(71, 71)]
    [InlineData(1, 36)]
    [InlineData(36, 1)]
    [InlineData(70, 36)]
    [InlineData(36, 70)]
    public void A_picture_reaches_every_corner_and_edge_of_the_key(int x, int y)
    {
        var red = new SKColor(220, 30, 30);
        string pic = SolidPng(red);

        using var key = SKBitmap.Decode(RenderPictureKey(pic));

        Assert.Equal(ButtonImageRenderer.ButtonWidth, key.Width);
        Assert.True(IsNear(key.GetPixel(x, y), red), $"pixel ({x},{y}) is {key.GetPixel(x, y)}, expected the picture's red");
    }

    [Fact]
    public void A_wide_picture_in_cover_mode_still_fills_the_key()
    {
        var green = new SKColor(30, 200, 60);
        string pic = SolidPng(green, 300, 100, "wide.png");

        using var key = SKBitmap.Decode(RenderPictureKey(pic, fit: "Cover"));

        Assert.True(IsNear(key.GetPixel(0, 0), green));
        Assert.True(IsNear(key.GetPixel(71, 71), green));
    }

    [Fact]
    public void A_pressed_picture_is_tinted_edge_to_edge_without_an_outline()
    {
        var red = new SKColor(220, 30, 30);
        string pic = SolidPng(red);

        using var key = SKBitmap.Decode(RenderPictureKey(pic, pressed: true));

        // the tint covers the corner pixel the same way it covers the centre: no outline, no black corner
        var corner = key.GetPixel(0, 0);
        var centre = key.GetPixel(36, 36);
        Assert.True(IsNear(corner, centre, 12), $"corner {corner} differs from centre {centre}");
        Assert.NotEqual(SKColors.Black, corner);
    }

    [Fact]
    public void A_toggled_on_picture_without_its_own_second_picture_gets_a_ring_so_the_state_shows()
    {
        var red = new SKColor(220, 30, 30);
        string pic = SolidPng(red);

        using var key = SKBitmap.Decode(RenderPictureKey(pic, active: true));

        Assert.False(IsNear(key.GetPixel(1, 36), red), "the edge should show the state ring");
        Assert.True(IsNear(key.GetPixel(36, 36), red), "the picture should still fill the rest");
    }

    [Fact]
    public void A_toggled_on_key_with_its_own_second_picture_shows_that_picture_edge_to_edge()
    {
        var red = new SKColor(220, 30, 30);
        var blue = new SKColor(30, 60, 220);
        string pic = SolidPng(red);
        string activePic = SolidPng(blue, name: "on.png");

        using var key = SKBitmap.Decode(RenderPictureKey(pic, active: true, activePicture: activePic));

        Assert.True(IsNear(key.GetPixel(0, 0), blue));
        Assert.True(IsNear(key.GetPixel(1, 36), blue));
    }

    [Fact]
    public void A_key_without_a_picture_keeps_the_rounded_card()
    {
        byte[] jpeg = ButtonImageRenderer.RenderCustomKeyJpeg(DeckActionType.None, 1, "A", "", "#DC1E1E", null, null);

        using var key = SKBitmap.Decode(jpeg);

        Assert.True(IsNear(key.GetPixel(0, 0), SKColors.Black), "the corner outside the rounded card stays black");
        Assert.True(IsNear(key.GetPixel(36, 10), new SKColor(0xDC, 0x1E, 0x1E)));
    }

    [Fact]
    public void A_missing_picture_falls_back_to_the_card()
    {
        byte[] jpeg = RenderPictureKey(Path.Combine(_dir, "gone.png"));

        using var key = SKBitmap.Decode(jpeg);

        Assert.True(IsNear(key.GetPixel(0, 0), SKColors.Black));
    }

    [Fact]
    public void A_broken_picture_file_does_not_throw_and_falls_back_to_the_card()
    {
        string junk = Path.Combine(_dir, "junk.png");
        File.WriteAllBytes(junk, [1, 2, 3, 4, 5]);

        byte[] jpeg = RenderPictureKey(junk);

        Assert.NotEmpty(jpeg);
    }

    [Fact]
    public void Full_bleed_frame_has_no_margin_no_clip_and_no_outline_at_rest()
    {
        using var frame = KeyFrame.FullBleed(144, 144);

        Assert.True(frame.IsFullBleed);
        Assert.Equal(new SKRect(0, 0, 144, 144), frame.ContentRect);
        Assert.False(frame.ShouldDrawOutline(isPressed: false, isActiveState: false, showsActivePicture: false));
        Assert.False(frame.ShouldDrawOutline(isPressed: true, isActiveState: false, showsActivePicture: false));
        Assert.False(frame.ShouldDrawOutline(isPressed: false, isActiveState: true, showsActivePicture: true));
        Assert.True(frame.ShouldDrawOutline(isPressed: false, isActiveState: true, showsActivePicture: false));
    }

    [Fact]
    public void Card_frame_keeps_the_scaled_margin_and_always_has_an_outline()
    {
        using var frame = KeyFrame.Card(144, 144, 2);

        Assert.False(frame.IsFullBleed);
        Assert.Equal(new SKRect(4, 4, 140, 140), frame.ContentRect);
        Assert.True(frame.ShouldDrawOutline(false, false, false));
    }

    [Fact]
    public void Full_bleed_outline_stays_inside_the_key()
    {
        using var frame = KeyFrame.FullBleed(144, 144);
        using var outline = frame.OutlineFor(4);

        Assert.Equal(new SKRect(2, 2, 142, 142), outline.Rect);
    }

    [Fact]
    public void A_picture_replaced_under_the_same_path_is_drawn_again_not_taken_from_the_cache()
    {
        string pic = SolidPng(new SKColor(220, 30, 30));
        File.SetLastWriteTimeUtc(pic, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        using (var first = SKBitmap.Decode(RenderPictureKey(pic)))
            Assert.True(IsNear(first.GetPixel(36, 36), new SKColor(220, 30, 30)));

        var blue = new SKColor(30, 60, 220);
        SolidPng(blue);   // same file name, new content
        File.SetLastWriteTimeUtc(pic, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        using var second = SKBitmap.Decode(RenderPictureKey(pic));
        Assert.True(IsNear(second.GetPixel(36, 36), blue));
    }

    [Fact]
    public void Animation_and_video_frames_fill_the_whole_key_without_a_frame()
    {
        var red = new SKColor(220, 30, 30);
        using var source = new SKBitmap(72, 72, SKColorType.Rgba8888, SKAlphaType.Premul);
        source.Erase(red);

        using var key = SKBitmap.Decode(ButtonImageRenderer.RenderBitmapJpeg(source));

        foreach (var (x, y) in new[] { (0, 0), (71, 0), (0, 71), (71, 71), (1, 36), (70, 36) })
            Assert.True(IsNear(key.GetPixel(x, y), red), $"pixel ({x},{y}) is {key.GetPixel(x, y)}");
    }

    [Fact]
    public void A_gif_on_a_key_fills_the_whole_key_in_the_still_preview_too()
    {
        var red = new SKColor(220, 30, 30);
        string gifLike = SolidPng(red, name: "anim.gif");   // a PNG under a .gif name decodes as one frame
        MediaFrameExtractor.ClearCache();

        using var key = SKBitmap.Decode(RenderPictureKey(gifLike));

        Assert.True(IsNear(key.GetPixel(0, 0), red), $"corner is {key.GetPixel(0, 0)}");
        Assert.True(IsNear(key.GetPixel(1, 36), red), $"edge is {key.GetPixel(1, 36)}");
    }

    [Fact]
    public void A_stream_deck_plugin_fallback_picture_fills_the_whole_key()
    {
        var red = new SKColor(220, 30, 30);
        string pic = SolidPng(red);

        byte[] jpeg = ButtonImageRenderer.RenderStreamDeckKeyJpeg(null, "Hi", pic);

        using var key = SKBitmap.Decode(jpeg);
        Assert.True(IsNear(key.GetPixel(0, 0), red));
        Assert.True(IsNear(key.GetPixel(71, 71), red));
    }
}
