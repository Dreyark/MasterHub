using MasterHub.Core.Rendering;
using SkiaSharp;

namespace MasterHub.Core.Tests;

public class JpegEncodingTests
{
    private static SKImage ThinRedLineOnBlue()
    {
        using var bmp = new SKBitmap(72, 72);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(new SKColor(0, 0, 255));
            using var p = new SKPaint { Color = new SKColor(255, 0, 0), IsAntialias = false };
            c.DrawRect(new SKRect(0, 35, 72, 36), p);   // linia 1 px - jak cienki tekst/ikona na klawiszu
        }
        return SKImage.FromBitmap(bmp);
    }

    private static int RedDistance(byte[] jpeg)
    {
        using var decoded = SKBitmap.Decode(jpeg);
        var px = decoded.GetPixel(36, 35);
        return Math.Abs(px.Red - 255) + px.Green + px.Blue;
    }

    [Fact]
    public void Encode_KeepsColourEdges_BetterThanDefaultSubsampling()
    {
        using var image = ThinRedLineOnBlue();
        byte[] sharp = JpegEncoding.Encode(image, 100).ToArray();
        using var legacy = image.Encode(SKEncodedImageFormat.Jpeg, 100);

        Assert.True(RedDistance(sharp) < RedDistance(legacy.ToArray()),
            $"4:4:4 ({RedDistance(sharp)}) powinno wierniej oddać kolor niż domyślne 4:2:0 ({RedDistance(legacy.ToArray())})");
        Assert.Equal(0xFF, sharp[0]);   // nagłówek JPEG (SOI)
        Assert.Equal(0xD8, sharp[1]);
    }
}
