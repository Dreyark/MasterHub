using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MasterHub.Core.Actions;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

public static partial class ButtonImageRenderer
{

    /// <summary>
    /// Renderuje obraz 240x240 JPEG ze wskazanego pliku z dopasowaniem do okrągłego ekranu IPS modułu Dial (M131E).
    /// </summary>
    public static byte[] RenderDialImage(string imagePath, string fitMode = "Cover")
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return GetBlankDialJpeg();
        }

        try
        {
            using var source = SKBitmap.Decode(imagePath);
            if (source == null) return GetBlankDialJpeg();
            return RenderDialBitmap(source, fitMode);
        }
        catch
        {
            return GetBlankDialJpeg();
        }
    }

    /// <summary>
    /// Renderuje obraz 240x240 JPEG z surowych bajtów graficznych dla okrągłego ekranu IPS modułu Dial (M131E).
    /// </summary>
    public static byte[] RenderDialImage(byte[] imageBytes, string fitMode = "Cover")
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            return GetBlankDialJpeg();
        }

        try
        {
            using var source = SKBitmap.Decode(imageBytes);
            if (source == null) return GetBlankDialJpeg();
            return RenderDialBitmap(source, fitMode);
        }
        catch
        {
            return GetBlankDialJpeg();
        }
    }

    public static byte[] GetBlankDialJpeg(SKColor? color = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(DialWidth, DialHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(color ?? SKColors.Black);
        using var img = surface.Snapshot();
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(img, 95);
        return data.ToArray();
    }

    private static byte[] RenderDialBitmap(SKBitmap source, string fitMode)
    {
        using var surface = SKSurface.Create(new SKImageInfo(DialWidth, DialHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        using var paint = new SKPaint { IsAntialias = true };

        SKRect destRect;
        switch (fitMode.ToLowerInvariant())
        {
            case "stretch":
                destRect = new SKRect(0, 0, DialWidth, DialHeight);
                break;
            case "contain":
                float scaleContain = Math.Min((float)DialWidth / source.Width, (float)DialHeight / source.Height);
                float wContain = source.Width * scaleContain;
                float hContain = source.Height * scaleContain;
                float xContain = (DialWidth - wContain) / 2f;
                float yContain = (DialHeight - hContain) / 2f;
                destRect = new SKRect(xContain, yContain, xContain + wContain, yContain + hContain);
                break;
            case "cover":
            default:
                float scaleCover = Math.Max((float)DialWidth / source.Width, (float)DialHeight / source.Height);
                float wCover = source.Width * scaleCover;
                float hCover = source.Height * scaleCover;
                float xCover = (DialWidth - wCover) / 2f;
                float yCover = (DialHeight - hCover) / 2f;
                destRect = new SKRect(xCover, yCover, xCover + wCover, yCover + hCover);
                break;
        }

        canvas.DrawBitmap(source, destRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);

        using var image = surface.Snapshot();
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 95);
        return data.ToArray();
    }
}
