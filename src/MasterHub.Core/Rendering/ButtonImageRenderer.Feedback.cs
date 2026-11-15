using System;
using System.Security.Cryptography;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

public static partial class ButtonImageRenderer
{
    /// <summary>Size of the red failure triangle as a fraction of the key edge.</summary>
    internal const float FailureCornerFraction = 0.34f;

    /// <summary>
    /// A key whose last press failed shows a small <b>red corner</b> (top right) until a press succeeds (T-07). The corner is
    /// drawn over an already rendered key picture, so every look of the key (image, text, states) keeps working; the result
    /// is cached by the content of the picture.
    /// </summary>
    public static byte[] WithFailureCorner(byte[] keyJpeg)
    {
        if (keyJpeg == null || keyJpeg.Length == 0) return keyJpeg ?? Array.Empty<byte>();

        string key = "fail:" + Convert.ToHexString(SHA1.HashData(keyJpeg));
        return s_cache.GetOrAdd(key, _ => DrawFailureCorner(keyJpeg));
    }

    /// <summary>Height of the hold-to-confirm bar as a fraction of the key edge.</summary>
    internal const float HoldBarFraction = 0.16f;

    /// <summary>Colour of the filled part of the hold-to-confirm bar (orange = "attention", T-18 meanings).</summary>
    public static readonly SKColor HoldBarColor = new(0xFF, 0x8A, 0x1F);

    /// <summary>
    /// Draws the hold-to-confirm bar of a power key (T-03) over an already rendered key picture: the key is dimmed a little
    /// and a bar at the bottom fills from left to right with <paramref name="fraction"/> (0 … 1). Not cached: the picture
    /// changes every frame while the key is held.
    /// </summary>
    public static byte[] WithHoldProgress(byte[] keyJpeg, double fraction)
    {
        if (keyJpeg == null || keyJpeg.Length == 0) return keyJpeg ?? Array.Empty<byte>();
        using var bitmap = TryDecode(keyJpeg);
        if (bitmap == null) return keyJpeg;

        int w = bitmap.Width, h = bitmap.Height;
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        var canvas = surface.Canvas;
        using (var blit = new SKPaint())
        {
            canvas.DrawBitmap(bitmap, new SKRect(0, 0, w, h), new SKSamplingOptions(SKFilterMode.Nearest), blit);
        }

        using (var dim = new SKPaint { Color = new SKColor(0, 0, 0, 0x55) })
        {
            canvas.DrawRect(new SKRect(0, 0, w, h), dim);
        }

        float barH = Math.Max(4f, h * HoldBarFraction);
        float margin = Math.Max(2f, w * 0.06f);
        var track = new SKRect(margin, h - margin - barH, w - margin, h - margin);
        float radius = barH / 2f;
        using (var trackPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0x20, 0x20, 0x24, 0xE6) })
        {
            canvas.DrawRoundRect(track, radius, radius, trackPaint);
        }

        double f = Math.Clamp(fraction, 0, 1);
        if (f > 0)
        {
            var filled = new SKRect(track.Left, track.Top, track.Left + (float)(track.Width * f), track.Bottom);
            if (filled.Width < barH) filled.Right = track.Left + Math.Min(track.Width, barH);   // a visible dot from the first step
            using var fill = new SKPaint { IsAntialias = true, Color = HoldBarColor };
            canvas.DrawRoundRect(filled, radius, radius, fill);
        }

        using var image = surface.Snapshot();
        using var data = JpegEncoding.Encode(image, 100);
        return data.ToArray();
    }

    /// <summary>Decodes a picture; bytes that are not a picture give <c>null</c> (Skia throws for them).</summary>
    private static SKBitmap? TryDecode(byte[] bytes)
    {
        try
        {
            return SKBitmap.Decode(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] DrawFailureCorner(byte[] keyJpeg)
    {
        using var bitmap = TryDecode(keyJpeg);
        if (bitmap == null) return keyJpeg;

        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        var canvas = surface.Canvas;
        using var blit = new SKPaint();
        canvas.DrawBitmap(bitmap, new SKRect(0, 0, bitmap.Width, bitmap.Height), new SKSamplingOptions(SKFilterMode.Nearest), blit);

        float size = bitmap.Width * FailureCornerFraction;
#pragma warning disable CS0618 // SKPath is the simplest way to draw a triangle; SKPathBuilder has no stable API in this SkiaSharp yet
        using var path = new SKPath();
        path.MoveTo(bitmap.Width - size, 0);
        path.LineTo(bitmap.Width, 0);
        path.LineTo(bitmap.Width, size);
        path.Close();
#pragma warning restore CS0618

        using var fill = new SKPaint { IsAntialias = true, Color = new SKColor(0xE0, 0x2B, 0x20), Style = SKPaintStyle.Fill };
        canvas.DrawPath(path, fill);

        using var image = surface.Snapshot();
        using var data = JpegEncoding.Encode(image, 100);
        return data.ToArray();
    }
}
