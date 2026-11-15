using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Core.Actions;
using MasterHub.Core.Rendering;
using SkiaSharp;

namespace MasterHub.UiTests;

/// <summary>T-34 #16: what the Studio shows for a key is the very picture sent to the deck, not a second drawing of it.</summary>
public class KeyPreviewTests
{
    [AvaloniaFact]
    public void ThePreview_IsTheRenderedPictureSentToTheDeck()
    {
        byte[] jpeg = ButtonImageRenderer.RenderCustomKeyJpeg(DeckActionType.None, 1, "A", "", "#DC1E1E", null, null);
        var key = new KeyItemViewModel { Index = 0 };

        key.SetRenderedJpeg(jpeg);

        Assert.Same(jpeg, key.RenderedJpegBytes);
        Assert.NotNull(key.CurrentDisplayBitmap);
        Assert.Same(key.CurrentDisplayBitmap, key.CachedBitmap);
        Assert.Equal(ButtonImageRenderer.ButtonWidth, key.CurrentDisplayBitmap!.PixelSize.Width);
        Assert.Equal(ButtonImageRenderer.ButtonHeight, key.CurrentDisplayBitmap.PixelSize.Height);
    }

    [AvaloniaFact]
    public void ThePreview_PrefersTheRenderedPictureOverTheRawImageFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mh_preview_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string raw = Path.Combine(dir, "wide.png");
            using (var bmp = new SKBitmap(300, 100))
            using (var img = SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SKEncodedImageFormat.Png, 100))
                File.WriteAllBytes(raw, data.ToArray());

            var key = new KeyItemViewModel { Index = 0, ImagePath = raw };
            byte[] jpeg = ButtonImageRenderer.RenderCustomKeyJpeg(DeckActionType.None, 1, "", "", null, null, raw);

            key.SetRenderedJpeg(jpeg);

            // the 300 x 100 file is shown as the 72 x 72 picture the deck gets (zoom / crop / fit applied), never as the raw file
            Assert.Equal(72, key.CurrentDisplayBitmap!.PixelSize.Width);
            Assert.Equal(72, key.CurrentDisplayBitmap.PixelSize.Height);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [AvaloniaFact]
    public void ClearingTheRenderedPicture_FallsBackToNoPreview_WhenThereIsNoImage()
    {
        var key = new KeyItemViewModel { Index = 0 };
        key.SetRenderedJpeg(ButtonImageRenderer.RenderCustomKeyJpeg(DeckActionType.None, 1, "A", "", null, null, null));

        key.SetRenderedJpeg(null);

        Assert.Null(key.CurrentDisplayBitmap);
        Assert.Null(key.RenderedJpegBytes);
    }

    [AvaloniaFact]
    public void AJunkPicture_DoesNotThrow_AndShowsNothing()
    {
        var key = new KeyItemViewModel { Index = 0 };

        var ex = Record.Exception(() => key.SetRenderedJpeg(new byte[] { 0xFF, 0xD8, 1, 2, 3, 4 }));

        Assert.Null(ex);
        Assert.Null(key.CurrentDisplayBitmap);
    }
}
