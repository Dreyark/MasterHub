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
    /// Renderuje pełnopanelowy obraz 480x272 JPEG ze wskazanego pliku graficznego z dopasowaniem (Cover/Contain/Stretch).
    /// </summary>
    public static byte[] RenderPanoramicImage(string imagePath, string fitMode = "Cover", int scale = 100, int offsetX = 0, int offsetY = 0)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return GetBlankPanoramicJpeg();
        }

        try
        {
            using var source = SKBitmap.Decode(imagePath);
            if (source == null) return GetBlankPanoramicJpeg();
            return RenderPanoramicBitmap(source, fitMode, scale, offsetX, offsetY);
        }
        catch
        {
            return GetBlankPanoramicJpeg();
        }
    }

    /// <summary>
    /// Renderuje pełnopanelowy obraz 480x272 JPEG z surowych bajtów graficznych.
    /// </summary>
    public static byte[] RenderPanoramicImage(byte[] imageBytes, string fitMode = "Cover", int scale = 100, int offsetX = 0, int offsetY = 0)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            return GetBlankPanoramicJpeg();
        }

        try
        {
            using var source = SKBitmap.Decode(imageBytes);
            if (source == null) return GetBlankPanoramicJpeg();
            return RenderPanoramicBitmap(source, fitMode, scale, offsetX, offsetY);
        }
        catch
        {
            return GetBlankPanoramicJpeg();
        }
    }

    /// <summary>
    /// The whole-deck picture: the source is fitted (Cover / Contain / Stretch), zoomed (20–400 %) and moved (−100…+100 % of the travel,
    /// the same rules as the picture of a single key) inside the 480 x 272 panel.
    /// </summary>
    public static byte[] RenderPanoramicFrame(SKBitmap source, string fitMode, int scale = 100, int offsetX = 0, int offsetY = 0) =>
        RenderPanoramicBitmap(source, fitMode, scale, offsetX, offsetY);

    private static byte[] RenderPanoramicBitmap(SKBitmap source, string fitMode, int scale = 100, int offsetX = 0, int offsetY = 0)
    {
        using var surface = SKSurface.Create(new SKImageInfo(PanelWidth, PanelHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        DrawTransformedBitmap(canvas, source, new SKRect(0, 0, PanelWidth, PanelHeight), fitMode, scale, offsetX, offsetY);

        using var snapshot = surface.Snapshot();
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(snapshot, 92);
        return data.ToArray();
    }

    /// <summary>
    /// Wycina fragment 72x72 z pełnopanelowego obrazu 480x272 dla zadanego klawisza (0..14).
    /// Układ siatki: krok 102 px w poziomie, krok 100 px w pionie.
    /// </summary>
    public static byte[] SlicePanoramicKeyJpeg(byte[] panoramicJpeg480x272, int keyIndex)
    {
        if (panoramicJpeg480x272 == null || panoramicJpeg480x272.Length == 0)
        {
            return GetBlankJpeg();
        }

        try
        {
            using var source = SKBitmap.Decode(panoramicJpeg480x272);
            if (source == null) return GetBlankJpeg();

            int col = Math.Clamp(keyIndex % 5, 0, 4);
            int row = Math.Clamp(keyIndex / 5, 0, 2);

            int x = Math.Clamp(col * KeyPitchX, 0, Math.Max(0, source.Width - ButtonWidth));
            int y = Math.Clamp(row * KeyPitchY, 0, Math.Max(0, source.Height - ButtonHeight));

            using var sliceBmp = new SKBitmap(ButtonWidth, ButtonHeight);
            using (var sliceCanvas = new SKCanvas(sliceBmp))
            {
                sliceCanvas.Clear(SKColors.Black);
                var srcRect = new SKRectI(x, y, x + ButtonWidth, y + ButtonHeight);
                var destRect = new SKRect(0, 0, ButtonWidth, ButtonHeight);
                sliceCanvas.DrawBitmap(source, srcRect, destRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);
            }

            using var sliceImg = SKImage.FromBitmap(sliceBmp);
            using var sliceData = MasterHub.Core.Rendering.JpegEncoding.Encode(sliceImg, 95);
            return sliceData.ToArray();
        }
        catch
        {
            return GetBlankJpeg();
        }
    }

    /// <summary>
    /// Wycina komplet 15 kafelków 72x72 z pełnopanelowego obrazu 480x272, dekodując obraz źródłowy tylko raz.
    /// Zapewnia znaczną oszczędność pamięci RAM i cykli CPU przy przełączaniu profili.
    /// </summary>
    public static List<byte[]> SliceAllPanoramicKeyJpegs(byte[] panoramicJpeg480x272)
    {
        var result = new List<byte[]>(15);
        if (panoramicJpeg480x272 == null || panoramicJpeg480x272.Length == 0)
        {
            var blank = GetBlankJpeg();
            for (int i = 0; i < 15; i++) result.Add(blank);
            return result;
        }

        try
        {
            using var source = SKBitmap.Decode(panoramicJpeg480x272);
            if (source == null)
            {
                var blank = GetBlankJpeg();
                for (int i = 0; i < 15; i++) result.Add(blank);
                return result;
            }

            for (int keyIndex = 0; keyIndex < 15; keyIndex++)
            {
                int col = Math.Clamp(keyIndex % 5, 0, 4);
                int row = Math.Clamp(keyIndex / 5, 0, 2);

                int x = Math.Clamp(col * KeyPitchX, 0, Math.Max(0, source.Width - ButtonWidth));
                int y = Math.Clamp(row * KeyPitchY, 0, Math.Max(0, source.Height - ButtonHeight));

                using var sliceBmp = new SKBitmap(ButtonWidth, ButtonHeight);
                using (var sliceCanvas = new SKCanvas(sliceBmp))
                {
                    sliceCanvas.Clear(SKColors.Black);
                    var srcRect = new SKRectI(x, y, x + ButtonWidth, y + ButtonHeight);
                    var destRect = new SKRect(0, 0, ButtonWidth, ButtonHeight);
                    sliceCanvas.DrawBitmap(source, srcRect, destRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);
                }

                using var sliceImg = SKImage.FromBitmap(sliceBmp);
                using var sliceData = MasterHub.Core.Rendering.JpegEncoding.Encode(sliceImg, 95);
                result.Add(sliceData.ToArray());
            }
            return result;
        }
        catch
        {
            var blank = GetBlankJpeg();
            for (int i = 0; i < 15; i++) result.Add(blank);
            return result;
        }
    }

    public static byte[] GetBlankPanoramicJpeg()
    {
        return s_cache.GetOrAdd("blank_panoramic", _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(PanelWidth, PanelHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Black);
            using var snapshot = surface.Snapshot();
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(snapshot, 90);
            return data.ToArray();
        });
    }
}
