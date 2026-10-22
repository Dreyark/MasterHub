using System;
using System.IO;
using SkiaSharp;
using Svg.Skia;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Pomocnik do rasteryzacji plików wektorowych SVG do formatów rastrowych (SKBitmap, PNG) za pomocą SkiaSharp i Svg.Skia.
/// </summary>
public static class SvgRasterizer
{
    public static SKBitmap? RasterizeSvg(string svgPath, int targetWidth = 144, int targetHeight = 144)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(svgPath) || !File.Exists(svgPath))
                return null;

            var svg = new SKSvg();
            svg.Load(svgPath);
            if (svg.Picture == null) return null;

            var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap(info);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            float cullW = svg.Picture.CullRect.Width;
            float cullH = svg.Picture.CullRect.Height;
            if (cullW > 0 && cullH > 0)
            {
                float scale = Math.Min((float)targetWidth / cullW, (float)targetHeight / cullH);
                float dx = (targetWidth - cullW * scale) / 2f;
                float dy = (targetHeight - cullH * scale) / 2f;
                canvas.Translate(dx, dy);
                canvas.Scale(scale);
            }

            canvas.DrawPicture(svg.Picture);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? RasterizeSvgToPngBytes(string svgPath, int targetWidth = 144, int targetHeight = 144)
    {
        try
        {
            using var bmp = RasterizeSvg(svgPath, targetWidth, targetHeight);
            if (bmp == null) return null;

            using var image = SKImage.FromBitmap(bmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
