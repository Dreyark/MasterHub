using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Wspólny koder JPEG dla ekranów LCD. Domyślny koder Skia używa próbkowania barw 4:2:0, co na małych
/// ekranach (klawisz 72x72) rozmywa kolorowe krawędzie tekstu i ikon nawet przy jakości 100.
/// Tu: 4:4:4 (pełna rozdzielczość barw).
/// </summary>
public static class JpegEncoding
{
    public static SKData Encode(SKImage image, int quality)
    {
        using var pixmap = image.PeekPixels();
        if (pixmap != null)
        {
            var data = pixmap.Encode(new SKJpegEncoderOptions(quality, SKJpegEncoderDownsample.Downsample444, SKJpegEncoderAlphaOption.Ignore));
            if (data != null) return data;
        }
        return image.Encode(SKEncodedImageFormat.Jpeg, quality);
    }
}
