using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MasterHub.Core.Actions;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// High-performance button image generator for the MasterHub 15 Key Deck LCD buttons.
/// Utilizes SkiaSharp to render 72x72 JPEGs with zero-allocation in-memory caching.
/// Eliminates color bleeding outside borders, supports custom typography, font sizing, and multi-line text.
/// </summary>
public static class ButtonImageRenderer
{
    public const int ButtonWidth = 72;
    public const int ButtonHeight = 72;

    public const int PanelWidth = 480;
    public const int PanelHeight = 272;
    public const int KeyPitchX = 102;
    public const int KeyPitchY = 100;
    public const int KeyWindowWidth = 79;
    public const int KeyWindowHeight = 75;

    public const int DialWidth = 240;
    public const int DialHeight = 240;

    private sealed class BoundedCache
    {
        private readonly int _maxSize;
        private readonly ConcurrentDictionary<string, byte[]> _dict = new();
        private readonly ConcurrentQueue<string> _keys = new();

        public BoundedCache(int maxSize) => _maxSize = maxSize;

        public byte[] GetOrAdd(string key, Func<string, byte[]> valueFactory)
        {
            if (_dict.TryGetValue(key, out var existing))
                return existing;

            var value = valueFactory(key);
            if (_dict.TryAdd(key, value))
            {
                _keys.Enqueue(key);
                while (_keys.Count > _maxSize && _keys.TryDequeue(out var oldKey))
                {
                    _dict.TryRemove(oldKey, out _);
                }
            }
            return value;
        }

        public void Clear()
        {
            _dict.Clear();
            while (_keys.TryDequeue(out _)) { }
        }
    }

    private static readonly BoundedCache s_cache = new(256);

    static ButtonImageRenderer()
    {
        PrewarmCache();
    }

    /// <summary>
    /// Pre-renders default and action button images to eliminate latency on user presses.
    /// </summary>
    public static void PrewarmCache()
    {
        GetBlankJpeg();

        for (int i = 1; i <= 15; i++)
        {
            GetButtonJpeg(i, $"BTN {i}", false);
            GetButtonJpeg(i, "KLIK", true);
        }

        foreach (DeckActionType action in Enum.GetValues<DeckActionType>())
        {
            GetActionJpeg(action, 1, false);
            GetActionJpeg(action, 1, true);
        }
    }

    public static byte[] GetBlankJpeg()
    {
        return s_cache.GetOrAdd("blank", _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0, 0, 0));
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Obraca bufor JPEG 72x72 o zadany kąt (90°, 180°, 270°) wokół środka kafelka.
    /// Jeśli kąt wynosi 0°, zwraca oryginalny bufor bez re-enkodowania.
    /// </summary>
    public static byte[] RotateJpeg(byte[] jpegBytes, int angleDegrees)
    {
        int normAngle = KeyOrientationHelper.NormalizeAngle(angleDegrees);
        if (normAngle == 0 || jpegBytes == null || jpegBytes.Length == 0)
        {
            return jpegBytes ?? Array.Empty<byte>();
        }

        try
        {
            using var originalBmp = SKBitmap.Decode(jpegBytes);
            if (originalBmp == null) return jpegBytes;

            using var rotatedBmp = new SKBitmap(ButtonWidth, ButtonHeight);
            using (var canvas = new SKCanvas(rotatedBmp))
            {
                canvas.Clear(SKColors.Black);
                canvas.Save();
                canvas.Translate(ButtonWidth / 2f, ButtonHeight / 2f);
                canvas.RotateDegrees(normAngle);
                canvas.Translate(-ButtonWidth / 2f, -ButtonHeight / 2f);
                canvas.DrawBitmap(originalBmp, 0, 0, SKSamplingOptions.Default, null);
                canvas.Restore();
            }

            using var image = SKImage.FromBitmap(rotatedBmp);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        }
        catch
        {
            return jpegBytes;
        }
    }

    /// <summary>
    /// Obraca pełnopanelowy bufor JPEG 480x272 o 180° w przypadku odwróconego montażu decka.
    /// </summary>
    public static byte[] RotatePanoramicJpeg(byte[] jpegBytes, int angleDegrees)
    {
        int normAngle = KeyOrientationHelper.NormalizeAngle(angleDegrees);
        if (normAngle != 180 || jpegBytes == null || jpegBytes.Length == 0)
        {
            return jpegBytes ?? Array.Empty<byte>();
        }

        try
        {
            using var originalBmp = SKBitmap.Decode(jpegBytes);
            if (originalBmp == null) return jpegBytes;

            using var rotatedBmp = new SKBitmap(PanelWidth, PanelHeight);
            using (var canvas = new SKCanvas(rotatedBmp))
            {
                canvas.Clear(SKColors.Black);
                canvas.Save();
                canvas.Translate(PanelWidth / 2f, PanelHeight / 2f);
                canvas.RotateDegrees(180);
                canvas.Translate(-PanelWidth / 2f, -PanelHeight / 2f);
                canvas.DrawBitmap(originalBmp, 0, 0, SKSamplingOptions.Default, null);
                canvas.Restore();
            }

            using var image = SKImage.FromBitmap(rotatedBmp);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
            return data.ToArray();
        }
        catch
        {
            return jpegBytes;
        }
    }

    /// <summary>
    /// Renderuje pełnopanelowy obraz 480x272 JPEG ze wskazanego pliku graficznego z dopasowaniem (Cover/Contain/Stretch).
    /// </summary>
    public static byte[] RenderPanoramicImage(string imagePath, string fitMode = "Cover")
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return GetBlankPanoramicJpeg();
        }

        try
        {
            using var source = SKBitmap.Decode(imagePath);
            if (source == null) return GetBlankPanoramicJpeg();
            return RenderPanoramicBitmap(source, fitMode);
        }
        catch
        {
            return GetBlankPanoramicJpeg();
        }
    }

    /// <summary>
    /// Renderuje pełnopanelowy obraz 480x272 JPEG z surowych bajtów graficznych.
    /// </summary>
    public static byte[] RenderPanoramicImage(byte[] imageBytes, string fitMode = "Cover")
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            return GetBlankPanoramicJpeg();
        }

        try
        {
            using var source = SKBitmap.Decode(imageBytes);
            if (source == null) return GetBlankPanoramicJpeg();
            return RenderPanoramicBitmap(source, fitMode);
        }
        catch
        {
            return GetBlankPanoramicJpeg();
        }
    }

    private static byte[] RenderPanoramicBitmap(SKBitmap source, string fitMode)
    {
        using var surface = SKSurface.Create(new SKImageInfo(PanelWidth, PanelHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        using var paint = new SKPaint
        {
            IsAntialias = true
        };

        SKRect destRect;
        switch (fitMode.ToLowerInvariant())
        {
            case "stretch":
                destRect = new SKRect(0, 0, PanelWidth, PanelHeight);
                break;

            case "contain":
                float scaleContain = Math.Min((float)PanelWidth / source.Width, (float)PanelHeight / source.Height);
                float wContain = source.Width * scaleContain;
                float hContain = source.Height * scaleContain;
                float xContain = (PanelWidth - wContain) / 2f;
                float yContain = (PanelHeight - hContain) / 2f;
                destRect = new SKRect(xContain, yContain, xContain + wContain, yContain + hContain);
                break;

            case "cover":
            default:
                float scaleCover = Math.Max((float)PanelWidth / source.Width, (float)PanelHeight / source.Height);
                float wCover = source.Width * scaleCover;
                float hCover = source.Height * scaleCover;
                float xCover = (PanelWidth - wCover) / 2f;
                float yCover = (PanelHeight - hCover) / 2f;
                destRect = new SKRect(xCover, yCover, xCover + wCover, yCover + hCover);
                break;
        }

        canvas.DrawBitmap(source, destRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, 92);
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
            using var sliceData = sliceImg.Encode(SKEncodedImageFormat.Jpeg, 95);
            return sliceData.ToArray();
        }
        catch
        {
            return GetBlankJpeg();
        }
    }

    public static byte[] GetBlankPanoramicJpeg()
    {
        using var surface = SKSurface.Create(new SKImageInfo(PanelWidth, PanelHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

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
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 95);
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
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }

    /// <summary>
    /// Retrieves a cached JPEG for a specific DeckActionType.
    /// </summary>
    public static byte[] GetActionJpeg(DeckActionType action, int buttonNumber, bool isPressed = false)
    {
        (string icon, string subText) = GetActionDetails(action, buttonNumber);
        string key = $"act:{action}:{icon}:{subText}:{isPressed}";

        return s_cache.GetOrAdd(key, _ => CreateActionJpeg(icon, subText, isPressed));
    }

    public static byte[] RenderCustomKeyJpeg(
        DeckActionType action,
        int buttonNumber,
        string? customTitle,
        string? customSubtitle,
        string? bgColorHex,
        string? textColorHex,
        string? imagePath,
        string? fontFamily = "Segoe UI",
        int fontSize = 0,
        bool isPressed = false,
        string? activeImagePath = null,
        bool isActiveState = false,
        string? subtitleColorHex = null,
        bool isBold = true,
        bool isItalic = false,
        bool isStrikeThrough = false,
        string? activeCustomTitle = null,
        string? activeCustomSubtitle = null,
        string? activeBgColorHex = null,
        string? activeTextColorHex = null,
        string? activeSubtitleColorHex = null,
        string? imageFitMode = "Cover",
        int imageScale = 100,
        int imageOffsetX = 0,
        int imageOffsetY = 0,
        string? activeImageFitMode = "Cover",
        int activeImageScale = 100,
        int activeImageOffsetX = 0,
        int activeImageOffsetY = 0)
    {
        string key = $"cust:{action}:{buttonNumber}:{customTitle}:{customSubtitle}:{bgColorHex}:{textColorHex}:{subtitleColorHex}:{imagePath}:{activeImagePath}:{isActiveState}:{fontFamily}:{fontSize}:{isPressed}:{isBold}:{isItalic}:{isStrikeThrough}:{activeCustomTitle}:{activeCustomSubtitle}:{activeBgColorHex}:{activeTextColorHex}:{activeSubtitleColorHex}:{imageFitMode}:{imageScale}:{imageOffsetX}:{imageOffsetY}:{activeImageFitMode}:{activeImageScale}:{activeImageOffsetX}:{activeImageOffsetY}";
        return s_cache.GetOrAdd(key, _ =>
            CreateCustomKeyJpeg(action, buttonNumber, customTitle, customSubtitle, bgColorHex, textColorHex, imagePath, fontFamily, fontSize, isPressed, activeImagePath, isActiveState, subtitleColorHex, isBold, isItalic, isStrikeThrough, activeCustomTitle, activeCustomSubtitle, activeBgColorHex, activeTextColorHex, activeSubtitleColorHex, imageFitMode, imageScale, imageOffsetX, imageOffsetY, activeImageFitMode, activeImageScale, activeImageOffsetX, activeImageOffsetY));
    }

    /// <summary>
    /// Renderuje wyjściowy JPEG 72x72 dla wtyczki Stream Deck, składając dynamiczną grafikę z WebSocketu,
    /// dynamiczny lub statyczny tytuł (z obsługą wielu linii), wskaźniki błędów/sukcesu oraz stan wciśnięcia.
    /// </summary>
    public static byte[] RenderStreamDeckKeyJpeg(
        byte[]? pluginImageJpeg,
        string? title,
        string? fallbackImagePath,
        bool isPressed = false,
        string? fontFamily = "Segoe UI",
        int fontSize = 0,
        bool isBold = true,
        bool isItalic = false,
        string? textColorHex = null,
        bool isAlert = false,
        bool isOk = false)
    {
        // Optymalizacja: czysty obraz wtyczki bez tekstu i nakładek
        if (!isPressed && !isAlert && !isOk && string.IsNullOrWhiteSpace(title) && pluginImageJpeg != null && pluginImageJpeg.Length > 0)
        {
            return pluginImageJpeg;
        }

        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        canvas.Clear(SKColors.Black);
        var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        bool drawnBase = false;
        if (pluginImageJpeg != null && pluginImageJpeg.Length > 0)
        {
            try
            {
                using var bmp = SKBitmap.Decode(pluginImageJpeg);
                if (bmp != null)
                {
                    var destRect = new SKRect(0, 0, ButtonWidth, ButtonHeight);
                    using var p = new SKPaint { IsAntialias = true, IsDither = true };
                    canvas.DrawBitmap(bmp, destRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), p);
                    drawnBase = true;
                }
            }
            catch { }
        }

        if (!drawnBase && !string.IsNullOrWhiteSpace(fallbackImagePath) && File.Exists(fallbackImagePath))
        {
            try
            {
                SKBitmap? bmp = null;
                if (fallbackImagePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    bmp = SvgRasterizer.RasterizeSvg(fallbackImagePath, ButtonWidth * 2, ButtonHeight * 2);
                }
                else
                {
                    bmp = SKBitmap.Decode(fallbackImagePath);
                }

                if (bmp != null)
                {
                    using (bmp)
                    {
                        var destRect = new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2);
                        using var p = new SKPaint { IsAntialias = true, IsDither = true };
                        canvas.DrawBitmap(bmp, destRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), p);
                        drawnBase = true;
                    }
                }
            }
            catch { }
        }

        if (!drawnBase)
        {
            using var bgPaint = new SKPaint { Color = new SKColor(0x18, 0x18, 0x1B), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        if (isPressed)
        {
            using var tintPaint = new SKPaint { Color = new SKColor(0x00, 0xFF, 0xCC, 0x40), Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(buttonRect, tintPaint);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            var rawLines = title.Replace("\r\n", "\n").Split('\n');
            var lines = rawLines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count > 0)
            {
                string chosenFont = !string.IsNullOrWhiteSpace(fontFamily) ? fontFamily : "Segoe UI";
                var weight = isBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
                var slant = isItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
                var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
                SKTypeface typeface;
                try
                {
                    typeface = SKTypeface.FromFamilyName(chosenFont, style) ?? SKTypeface.FromFamilyName("Segoe UI", style) ?? SKTypeface.Default;
                }
                catch
                {
                    typeface = SKTypeface.Default;
                }

                float effectiveSize = fontSize > 0 ? Math.Clamp(fontSize, 6, 20) : lines.Count switch
                {
                    1 => lines[0].Length <= 4 ? 16f : (lines[0].Length <= 8 ? 12f : 10f),
                    2 => 10f,
                    3 => 8.5f,
                    4 => 7.5f,
                    _ => 6.5f
                };

                while (effectiveSize > 6.0f)
                {
                    using var testFont = new SKFont(typeface, effectiveSize);
                    float maxW = 0;
                    foreach (var l in lines)
                    {
                        float w = testFont.MeasureText(l);
                        if (w > maxW) maxW = w;
                    }
                    if (maxW <= ButtonWidth - 8) break;
                    effectiveSize -= 0.5f;
                }

                using var font = new SKFont(typeface, effectiveSize);
                float lineSpacing = effectiveSize * 1.25f;
                float totalBlockHeight = lines.Count * lineSpacing;
                float startY = (ButtonHeight - totalBlockHeight) / 2f + effectiveSize * 0.88f;

                SKColor fgCol = SKColors.White;
                if (!string.IsNullOrWhiteSpace(textColorHex) && SKColor.TryParse(textColorHex, out var parsedCol))
                {
                    fgCol = parsedCol;
                }

                if (lines.Count >= 3)
                {
                    using var textBgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 160), Style = SKPaintStyle.Fill, IsAntialias = true };
                    var bgBox = new SKRoundRect(new SKRect(3, Math.Max(2, startY - effectiveSize), ButtonWidth - 3, Math.Min(ButtonHeight - 2, startY + (lines.Count - 1) * lineSpacing + 4)), 4, 4);
                    canvas.DrawRoundRect(bgBox, textBgPaint);
                }

                using var strokePaint = new SKPaint
                {
                    Color = new SKColor(0, 0, 0, 220),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2.5f,
                    IsAntialias = true
                };
                using var fillPaint = new SKPaint
                {
                    Color = fgCol,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };

                for (int i = 0; i < lines.Count; i++)
                {
                    float lineY = startY + (i * lineSpacing);
                    if (lineY > 0 && lineY < ButtonHeight + effectiveSize)
                    {
                        canvas.DrawText(lines[i], ButtonWidth / 2f, lineY, SKTextAlign.Center, font, strokePaint);
                        canvas.DrawText(lines[i], ButtonWidth / 2f, lineY, SKTextAlign.Center, font, fillPaint);
                    }
                }
            }
        }

        if (isAlert)
        {
            using var alertBgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 190), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(ButtonWidth - 24, 2, ButtonWidth - 2, 24), 4, 4, alertBgPaint);
            using var alertFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI Emoji") ?? SKTypeface.Default, 13f);
            using var alertPaint = new SKPaint { Color = SKColors.Yellow, IsAntialias = true };
            canvas.DrawText("⚠️", ButtonWidth - 13, 18, SKTextAlign.Center, alertFont, alertPaint);
        }
        else if (isOk)
        {
            using var okBgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 190), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(ButtonWidth - 24, 2, ButtonWidth - 2, 24), 4, 4, okBgPaint);
            using var okFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI Emoji") ?? SKTypeface.Default, 13f);
            using var okPaint = new SKPaint { Color = SKColors.LimeGreen, IsAntialias = true };
            canvas.DrawText("✅", ButtonWidth - 13, 18, SKTextAlign.Center, okFont, okPaint);
        }

        canvas.Restore();
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, 90);
        return encoded.ToArray();
    }

    public static (string icon, string subtitle) GetActionDetails(DeckActionType action, int buttonNumber) =>
        GetActionDetails(action, buttonNumber, false);

    public static (string icon, string subtitle) GetActionDetails(DeckActionType action, int buttonNumber, bool isActiveState)
    {
        if (isActiveState)
        {
            return action switch
            {
                DeckActionType.ObsToggleStream => ("🔴", "LIVE"),
                DeckActionType.ObsStartStream => ("🔴", "LIVE"),
                DeckActionType.ObsStopStream => ("⏹", "STRM OFF"),
                DeckActionType.ObsToggleRecord => ("⏹", "NAGRYWA"),
                DeckActionType.ObsStartRecord => ("⏹", "NAGRYWA"),
                DeckActionType.ObsStopRecord => ("⏹", "REC OFF"),
                DeckActionType.ObsToggleVirtualCam => ("🟢", "VCAM ON"),
                DeckActionType.ObsStudioModeToggle => ("🎬", "STUDIO ON"),
                DeckActionType.DiscordMuteToggle => ("🔇", "WYCISZ"),
                DeckActionType.DiscordDeafenToggle => ("🔕", "GŁUCHY"),
                DeckActionType.DiscordCameraToggle => ("🚫", "CAM OFF"),
                DeckActionType.DiscordScreenShareToggle => ("📡", "DC LIVE"),
                DeckActionType.DiscordPushToTalk => ("🎙️", "NADAJE"),
                DeckActionType.MediaPlayPause => ("⏸", "PAUZA"),
                DeckActionType.MasterMute => ("✕", "WYCISZ"),
                DeckActionType.MicMute => ("🔇", "WYCISZ"),
                DeckActionType.TwitchChatEmoteOnly => ("🔒", "EMOTKI ON"),
                DeckActionType.TwitchChatSubOnly => ("🌟", "SUB ON"),
                DeckActionType.TwitchChatSlowMode => ("⏱️", "SLOW ON"),
                DeckActionType.KickChatEmoteOnly => ("🔒", "K-EMOTE ON"),
                DeckActionType.KickChatSubOnly => ("🌟", "K-SUB ON"),
                DeckActionType.KickChatSlowMode => ("⏱️", "K-SLOW ON"),
                DeckActionType.JetKvmPowerStatus => ("💡", "SRV ON"),
                DeckActionType.JetKvmPowerShort => ("🔌", "SRV ON"),
                DeckActionType.HotkeySwitch => ("2️⃣", "SKRÓT 2"),
                DeckActionType.TeamsMuteToggle => ("🔇", "WYCISZ"),
                DeckActionType.TeamsCameraToggle => ("🚫", "CAM OFF"),
                DeckActionType.TeamsRaiseHand => ("🙋", "W GÓRZE"),
                DeckActionType.SystemShowDesktop => ("🗗", "PRZYWRÓĆ"),
                DeckActionType.VoicemodVoiceToggle => ("🗣️", "MOD WŁ"),
                DeckActionType.VoicemodMuteVoice => ("🔇", "WYCISZ"),
                DeckActionType.VoicemodHearMyVoiceToggle => ("🎧", "ODSŁUCH WŁ"),
                DeckActionType.VoicemodBackgroundToggle => ("🎧", "TŁO WŁ"),
                DeckActionType.HomeAssistantToggle => ("💡", "ON"),
                DeckActionType.HomeAssistantTurnOn => ("💡", "ON"),
                DeckActionType.HomeAssistantTurnOff => ("🔌", "OFF"),
                DeckActionType.HomeAssistantCallService => ("⚙️", "HA SVC"),
                _ => GetActionDetailsDefault(action, buttonNumber)
            };
        }

        return GetActionDetailsDefault(action, buttonNumber);
    }

    public static (string icon, string subtitle) GetActionDetailsDefault(DeckActionType action, int buttonNumber) => action switch
    {
        DeckActionType.None => ("", ""),
        DeckActionType.MediaPlayPause => ("▶⏸", "PLAY"),
        DeckActionType.MediaNext => ("▶▶", "NEXT"),
        DeckActionType.MediaPrevious => ("◀◀", "PREV"),
        DeckActionType.MediaStop => ("■", "STOP"),
        DeckActionType.MasterMute => ("✕", "MUTE"),
        DeckActionType.MicMute => ("🎙", "MIC"),
        DeckActionType.VolumeUp => ("▲", "VOL +"),
        DeckActionType.VolumeDown => ("▼", "VOL -"),
        DeckActionType.Screenshot => ("✂", "ZRZUT"),
        DeckActionType.TaskManager => ("≡", "TASK"),
        DeckActionType.Calculator => ("+-", "CALC"),
        DeckActionType.Browser => ("🌐", "WEB"),
        DeckActionType.DefaultNumber => (buttonNumber.ToString(), $"BTN {buttonNumber}"),

        // --- ETAP 1 ---
        DeckActionType.OpenApplication => ("🚀", "APP"),
        DeckActionType.OpenWebsite => ("🌐", "URL"),
        DeckActionType.OpenFile => ("📂", "PLIK"),
        DeckActionType.CloseApplication => ("❌", "ZAMKNIJ"),
        DeckActionType.TextPaste => ("📋", "TEKST"),
        DeckActionType.Hotkey => ("⌨️", "SKRÓT"),
        DeckActionType.HotkeySwitch => ("🔁", "SKRÓT 2"),
        DeckActionType.MultiAction => ("⚡", "SEKWENCJA"),
        DeckActionType.Delay => ("⏳", "CZEKAJ"),
        DeckActionType.SystemSleep => ("💤", "UŚPIJ"),
        DeckActionType.SystemShutdown => ("⏻", "WYŁĄCZ"),
        DeckActionType.SystemRestart => ("🔄", "RESTART"),
        DeckActionType.SystemLockScreen => ("🔒", "BLOKADA"),
        DeckActionType.SystemScreensaver => ("🖥️", "WYGASZ"),
        DeckActionType.SystemVirtualDesktopNext => ("🗔▶", "VD DALEJ"),
        DeckActionType.SystemVirtualDesktopPrev => ("◀🗔", "VD WSTECZ"),
        DeckActionType.SystemVirtualDesktopCreate => ("➕🗔", "NOWY VD"),
        DeckActionType.SystemVirtualDesktopClose => ("✖️🗔", "ZAMKNIJ VD"),
        DeckActionType.SystemTaskView => ("🗂️", "ZADANIA"),
        DeckActionType.SystemShowDesktop => ("🖥️", "PULPIT"),
        DeckActionType.SystemClipboardHistory => ("📋", "SCHOWEK"),
        DeckActionType.ConditionalAction => ("🔀", "IF / ELSE"),

        // --- ETAP 2: NAWIGACJA, STRONY I FOLDERY ---
        DeckActionType.GoToPage => ("📄", "STRONA"),
        DeckActionType.GoToProfile => ("🔀", "PROFIL"),
        DeckActionType.OpenFolder => ("📁", "FOLDER"),
        DeckActionType.GoBack => ("⬅️", "WRÓĆ"),
        DeckActionType.GoHome => ("🏠", "DOM"),
        DeckActionType.NextPage => ("▶", "NASTĘPNA"),
        DeckActionType.PreviousPage => ("◀", "POPRZEDNIA"),

        // --- ETAP 3: LIVE DATA & SYSTEM MONITORING ---
        DeckActionType.DisplayCpuUsage => ("📊", "CPU %"),
        DeckActionType.DisplayRamUsage => ("🧠", "RAM"),
        DeckActionType.DisplayGpuUsage => ("🎮", "GPU %"),
        DeckActionType.DisplayCpuTemp => ("CPU", "CPU °C"),
        DeckActionType.DisplayGpuTemp => ("GPU", "GPU °C"),
        DeckActionType.DisplayNetworkSpeed => ("📡", "SIEĆ"),
        DeckActionType.DisplayDiskUsage => ("💾", "DYSK"),
        DeckActionType.DisplayClock => ("🕐", "ZEGAR"),
        DeckActionType.DisplayDate => ("📅", "DATA"),
        DeckActionType.NowPlaying => ("🎵", "PLAYING"),
        DeckActionType.DisplayFps => ("🎯", "FPS"),

        // --- ETAP 4: STREAMING OBS ---
        DeckActionType.ObsToggleStream => ("📡", "STREAM"),
        DeckActionType.ObsStartStream => ("▶", "STRM ON"),
        DeckActionType.ObsStopStream => ("⏹", "STRM OFF"),
        DeckActionType.ObsToggleRecord => ("🔴", "REC"),
        DeckActionType.ObsStartRecord => ("▶", "REC ON"),
        DeckActionType.ObsStopRecord => ("⏹", "REC OFF"),
        DeckActionType.ObsToggleVirtualCam => ("📹", "VCAM"),
        DeckActionType.ObsSaveReplayBuffer => ("💾", "REPLAY"),
        DeckActionType.ObsSceneSwitch => ("🎬", "SCENA"),
        DeckActionType.ObsStudioModeToggle => ("🎭", "STUDIO"),
        DeckActionType.ObsTriggerTransition => ("🔀", "TRANS"),

        DeckActionType.TwitchChatEmoteOnly => ("😀", "EMOTE"),
        DeckActionType.TwitchChatSlowMode => ("🐢", "SLOW"),
        DeckActionType.TwitchChatSubOnly => ("⭐", "SUBS"),
        DeckActionType.TwitchClearChat => ("🧹", "CLEAR"),
        DeckActionType.TwitchSendMessage => ("💬", "CHAT"),
        DeckActionType.TwitchCreateClip => ("🎬", "CLIP"),
        DeckActionType.TwitchCreateMarker => ("📍", "MARKER"),
        DeckActionType.TwitchStartRaid => ("🚀", "RAID"),
        DeckActionType.TwitchCancelRaid => ("🛑", "ANULUJ"),
        DeckActionType.TwitchRunCommercial => ("📺", "REKLAMA"),
        DeckActionType.KickChatEmoteOnly => ("😀", "K-EMOTE"),
        DeckActionType.KickChatSlowMode => ("🐢", "K-SLOW"),
        DeckActionType.KickChatSubOnly => ("⭐", "K-SUBS"),
        DeckActionType.KickClearChat => ("🧹", "K-CLEAR"),
        DeckActionType.KickSendMessage => ("💬", "K-CHAT"),
        DeckActionType.KickCreateClip => ("🎬", "K-CLIP"),
        DeckActionType.JetKvmPowerShort => ("🔌", "SRV OFF"),
        DeckActionType.JetKvmPowerLong => ("⏻", "PWR CUT"),
        DeckActionType.JetKvmReset => ("🔄", "RESET"),
        DeckActionType.JetKvmPowerStatus => ("💡", "SRV OFF"),
        DeckActionType.YouTubeLiveViewers => ("▶️", "YT LIVE"),
        DeckActionType.StreamlabsSceneSwitch => ("🎥", "SL SCENA"),
        DeckActionType.DiscordMuteToggle => ("🎙️", "DC MUTE"),
        DeckActionType.DiscordDeafenToggle => ("🎧", "DC DEAF"),
        DeckActionType.DiscordCameraToggle => ("📷", "DC CAM"),
        DeckActionType.DiscordScreenShareToggle => ("🖥️", "DC STREAM"),
        DeckActionType.DiscordPushToTalk => ("🎙️", "DC PTT"),
        DeckActionType.DiscordJoinVoiceChannel => ("🔊", "DC KANAŁ"),
        DeckActionType.TeamsMuteToggle => ("🎙️", "MS MUTE"),
        DeckActionType.TeamsCameraToggle => ("📷", "MS KAMERA"),
        DeckActionType.TeamsRaiseHand => ("✋", "MS RĘKA"),
        DeckActionType.TeamsLeaveMeeting => ("📞", "MS OPUŚĆ"),
        DeckActionType.VoicemodVoiceSelect => ("🤖", "GŁOS"),
        DeckActionType.VoicemodVoiceToggle => ("🎙️", "MOD WYŁ"),
        DeckActionType.VoicemodMuteVoice => ("🎙️", "MIKROFON"),
        DeckActionType.VoicemodHearMyVoiceToggle => ("🎧", "ODSŁUCH WYŁ"),
        DeckActionType.VoicemodBackgroundToggle => ("🔇", "BEZ TŁA"),
        DeckActionType.VoicemodPlaySound => ("🔊", "DŹWIĘK"),
        DeckActionType.VoicemodStopAllSounds => ("⏹️", "STOP DŹWIĘK"),

        DeckActionType.RazerDpiCycle => ("🎯", "RZ DPI"),
        DeckActionType.RazerDpiUp => ("▲", "RZ DPI+"),
        DeckActionType.RazerDpiDown => ("▼", "RZ DPI-"),
        DeckActionType.RazerDpi400 => ("400", "RZ DPI"),
        DeckActionType.RazerDpi800 => ("800", "RZ DPI"),
        DeckActionType.RazerDpi1600 => ("1600", "RZ DPI"),
        DeckActionType.RazerDpi3200 => ("3200", "RZ DPI"),
        DeckActionType.RazerDpi6400 => ("6400", "RZ DPI"),
        DeckActionType.RazerProfileNext => ("🐍▶", "RZ PROF+"),
        DeckActionType.RazerProfilePrev => ("◀🐍", "RZ PROF-"),
        DeckActionType.RazerProfile1 => ("P1", "RAZER 1"),
        DeckActionType.RazerProfile2 => ("P2", "RAZER 2"),
        DeckActionType.RazerProfile3 => ("P3", "RAZER 3"),
        DeckActionType.RazerProfile4 => ("P4", "RAZER 4"),
        DeckActionType.RazerProfile5 => ("P5", "RAZER 5"),
        DeckActionType.RazerPollingRateCycle => ("⚡", "RZ HZ"),
        DeckActionType.RazerPollingRateUp => ("▲", "HZ +"),
        DeckActionType.RazerPollingRateDown => ("▼", "HZ -"),
        DeckActionType.RazerPollingRate125 => ("125", "HZ"),
        DeckActionType.RazerPollingRate500 => ("500", "HZ"),
        DeckActionType.RazerPollingRate1000 => ("1K", "1000HZ"),
        DeckActionType.RazerPollingRate2000 => ("2K", "2000HZ"),
        DeckActionType.RazerPollingRate4000 => ("4K", "4000HZ"),
        DeckActionType.RazerPollingRate8000 => ("8K", "8000HZ"),
        DeckActionType.WootingProfileCycle => ("⌨️", "WOOT CYKL"),
        DeckActionType.WootingProfileNext => ("⌨️▶", "WOOT PROF+"),
        DeckActionType.WootingProfilePrev => ("◀⌨️", "WOOT PROF-"),
        DeckActionType.WootingProfileDigital => ("W0", "WOOT DIGI"),
        DeckActionType.WootingProfile1 => ("W1", "WOOT PRO1"),
        DeckActionType.WootingProfile2 => ("W2", "WOOT PRO2"),
        DeckActionType.WootingProfile3 => ("W3", "WOOT PRO3"),
        DeckActionType.RazerShortcutNextProfile => ("🐍▶", "RZ PROF+"),
        DeckActionType.RazerShortcutPrevProfile => ("◀🐍", "RZ PROF-"),
        DeckActionType.RazerShortcutProfile1 => ("P1", "RAZER 1"),
        DeckActionType.RazerShortcutProfile2 => ("P2", "RAZER 2"),
        DeckActionType.RazerShortcutProfile3 => ("P3", "RAZER 3"),
        DeckActionType.RazerShortcutProfile4 => ("P4", "RAZER 4"),
        DeckActionType.RazerShortcutProfile5 => ("P5", "RAZER 5"),
        DeckActionType.RazerShortcutDpiUp => ("▲", "RZ DPI+"),
        DeckActionType.RazerShortcutDpiDown => ("▼", "RZ DPI-"),
        DeckActionType.RazerShortcutDpiCycle => ("🎯", "RZ DPI"),
        DeckActionType.RazerApiNextProfile => ("🐍▶", "RZ PROF+"),
        DeckActionType.RazerApiPrevProfile => ("◀🐍", "RZ PROF-"),
        DeckActionType.RazerApiSelectProfile => ("🐍", "RZ PROF"),
        DeckActionType.HomeAssistantToggle => ("⚪", "OFF"),
        DeckActionType.HomeAssistantTurnOn => ("💡", "WŁĄCZ"),
        DeckActionType.HomeAssistantTurnOff => ("🔌", "WYŁĄCZ"),
        DeckActionType.HomeAssistantCallService => ("⚙️", "HA SVC"),
        _ => ("", "")
    };

    public static byte[] GetButtonJpeg(int buttonNumber, string title = "", bool isPressed = false)
    {
        if (string.IsNullOrEmpty(title) && !isPressed)
        {
            return GetBlankJpeg();
        }

        string subText = !string.IsNullOrEmpty(title) ? title : (isPressed ? "KLIK" : "");
        string key = $"num:{buttonNumber}:{subText}:{isPressed}";

        return s_cache.GetOrAdd(key, _ => CreateButtonJpeg(buttonNumber, subText, isPressed));
    }

    private static bool IsEmojiOrSymbol(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
        {
            if (char.IsSurrogate(c) || c > 0x2000) return true;
        }
        return false;
    }

    private static SKTypeface ResolveTypeface(string text, string? requestedFont, bool isBold = true, bool isItalic = false)
    {
        if (IsEmojiOrSymbol(text))
        {
            try
            {
                var emojiTf = SKTypeface.FromFamilyName("Segoe UI Emoji");
                if (emojiTf != null) return emojiTf;
            }
            catch { }
            try
            {
                var symTf = SKTypeface.FromFamilyName("Segoe UI Symbol");
                if (symTf != null) return symTf;
            }
            catch { }
        }

        string chosenFont = !string.IsNullOrWhiteSpace(requestedFont) ? requestedFont : "Segoe UI";
        var weight = isBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = isItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
        try
        {
            return SKTypeface.FromFamilyName(chosenFont, style)
                   ?? SKTypeface.FromFamilyName("Segoe UI", style)
                   ?? SKTypeface.Default;
        }
        catch
        {
            return SKTypeface.Default;
        }
    }

    private static byte[] CreateActionJpeg(string icon, string subText, bool isPressed)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        // Czysto czarna ramka bazowa – zapobiega jakimkolwiek wyciekom koloru poza obramówkę
        canvas.Clear(new SKColor(0, 0, 0));

        var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        SKColor bgColor = isPressed ? new SKColor(0x3B, 0x1A, 0x6E) : new SKColor(0x0E, 0x10, 0x18);
        using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true, IsDither = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        if (!string.IsNullOrEmpty(icon))
        {
            using var fontBig = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);

            // Podwójny przebieg: kontrastowy obrys (halo) + nasycone wypełnienie
            using var iconOutline = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 230),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2.5f,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            };
            using var iconPaint = new SKPaint
            {
                Color = isPressed ? new SKColor(0x00, 0xFF, 0xCC) : new SKColor(0xFF, 0xFF, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                IsDither = true
            };

            canvas.DrawText(icon, ButtonWidth / 2f, 37, SKTextAlign.Center, fontBig, iconOutline);
            canvas.DrawText(icon, ButtonWidth / 2f, 37, SKTextAlign.Center, fontBig, iconPaint);
        }

        if (!string.IsNullOrEmpty(subText))
        {
            using var fontSmall = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);

            using var subOutline = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 210),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.8f,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            };
            using var subPaint = new SKPaint
            {
                Color = isPressed ? new SKColor(0xFF, 0xFF, 0xFF) : new SKColor(0x8C, 0x96, 0xB6),
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                IsDither = true
            };

            canvas.DrawText(subText, ButtonWidth / 2f, 56, SKTextAlign.Center, fontSmall, subOutline);
            canvas.DrawText(subText, ButtonWidth / 2f, 56, SKTextAlign.Center, fontSmall, subPaint);
        }

        canvas.Restore();

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = isPressed ? 3f : 1.5f,
            Color = isPressed ? new SKColor(0x00, 0xFF, 0xCC) : new SKColor(0x22, 0x28, 0x3D),
            IsAntialias = true
        };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        // Jakość 100: wyłącza kompresję 4:2:0 na korzyść pełnego 4:4:4, eliminując artefakty szumu i ringing w tle na matrycach LCD
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Rysuje przeskalowany i wykadrowany obraz na kanwie z uwzględnieniem trybu dopasowania (Cover, Contain, Stretch),
    /// przybliżenia (zoom 20-400%) oraz przesunięcia w osiach X i Y (-100% do +100%).
    /// </summary>
    public static void DrawTransformedBitmap(
        SKCanvas canvas,
        SKBitmap bitmap,
        SKRect destBounds,
        string fitMode = "Cover",
        int scalePercent = 100,
        int offsetXPercent = 0,
        int offsetYPercent = 0)
    {
        if (bitmap == null || canvas == null) return;

        float destW = destBounds.Width;
        float destH = destBounds.Height;
        if (destW <= 0 || destH <= 0 || bitmap.Width <= 0 || bitmap.Height <= 0) return;

        float baseW, baseH;
        string mode = (fitMode ?? "Cover").Trim();

        if (mode.Equals("Stretch", StringComparison.OrdinalIgnoreCase))
        {
            baseW = destW;
            baseH = destH;
        }
        else if (mode.Equals("Contain", StringComparison.OrdinalIgnoreCase))
        {
            float scale = Math.Min(destW / bitmap.Width, destH / bitmap.Height);
            baseW = bitmap.Width * scale;
            baseH = bitmap.Height * scale;
        }
        else // Cover (domyślnie)
        {
            float scale = Math.Max(destW / bitmap.Width, destH / bitmap.Height);
            baseW = bitmap.Width * scale;
            baseH = bitmap.Height * scale;
        }

        float zoom = Math.Clamp(scalePercent, 20, 400) / 100.0f;
        float finalW = baseW * zoom;
        float finalH = baseH * zoom;

        // Prawidłowe przesunięcie proporcjonalne do rozmiaru wyrenderowanego obrazu (finalW / finalH)
        // Pozwala na swobodne przemieszczanie kadru aż do samych rogów i krawędzi obrazu przy dowolnym powiększeniu (nawet 400%)
        float maxTravelX = Math.Max(destW / 2.0f, finalW / 2.0f);
        float maxTravelY = Math.Max(destH / 2.0f, finalH / 2.0f);

        float centerX = destBounds.MidX - (Math.Clamp(offsetXPercent, -100, 100) / 100.0f) * maxTravelX;
        float centerY = destBounds.MidY + (Math.Clamp(offsetYPercent, -100, 100) / 100.0f) * maxTravelY;

        var drawRect = new SKRect(
            centerX - finalW / 2.0f,
            centerY - finalH / 2.0f,
            centerX + finalW / 2.0f,
            centerY + finalH / 2.0f);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            IsDither = true
        };

        canvas.DrawBitmap(bitmap, drawRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
    }

    private static byte[] CreateCustomKeyJpeg(
        DeckActionType action,
        int buttonNumber,
        string? customTitle,
        string? customSubtitle,
        string? bgColorHex,
        string? textColorHex,
        string? imagePath,
        string? fontFamily,
        int fontSize,
        bool isPressed,
        string? activeImagePath = null,
        bool isActiveState = false,
        string? subtitleColorHex = null,
        bool isBold = true,
        bool isItalic = false,
        bool isStrikeThrough = false,
        string? activeCustomTitle = null,
        string? activeCustomSubtitle = null,
        string? activeBgColorHex = null,
        string? activeTextColorHex = null,
        string? activeSubtitleColorHex = null,
        string? imageFitMode = "Cover",
        int imageScale = 100,
        int imageOffsetX = 0,
        int imageOffsetY = 0,
        string? activeImageFitMode = "Cover",
        int activeImageScale = 100,
        int activeImageOffsetX = 0,
        int activeImageOffsetY = 0)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        // 1. Czysto czarna krawędź bazowa – zapobiega wyciekom koloru poza obrys klawisza LCD
        canvas.Clear(new SKColor(0, 0, 0));

        var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        // 2. Tło (kolor) – rysowane ZAWSZE jako baza (również pod przezroczystymi grafikami PNG)
        SKColor bg = isPressed ? new SKColor(0x3B, 0x1A, 0x6E) : (isActiveState ? new SKColor(0x1F, 0x16, 0x33) : new SKColor(0x00, 0x00, 0x00));
        string? effectiveBgHex = (isActiveState && !string.IsNullOrWhiteSpace(activeBgColorHex)) ? activeBgColorHex : bgColorHex;
        if (!string.IsNullOrWhiteSpace(effectiveBgHex) && SKColor.TryParse(effectiveBgHex, out var parsedBg))
        {
            bg = isPressed
                ? new SKColor((byte)Math.Min(255, parsedBg.Red + 40), (byte)Math.Min(255, parsedBg.Green + 40), (byte)Math.Min(255, parsedBg.Blue + 40))
                : parsedBg;
        }
        using (var bgPaint = new SKPaint { Color = bg, Style = SKPaintStyle.Fill, IsAntialias = true, IsDither = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        // 3. Własny obrazek / grafika z dysku (jeśli wybrana) – z obsługą stanu aktywnego/toggle i pełnego kadrowania
        bool isStateActive = (isActiveState || isPressed);
        string? effectiveImage = (isStateActive && !string.IsNullOrWhiteSpace(activeImagePath) && File.Exists(activeImagePath))
            ? activeImagePath
            : imagePath;

        if (string.IsNullOrWhiteSpace(effectiveImage) && (action == DeckActionType.OpenApplication || action == DeckActionType.OpenFile))
        {
            if (!string.IsNullOrWhiteSpace(customSubtitle) && File.Exists(customSubtitle) &&
                (customSubtitle.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || customSubtitle.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)))
            {
                effectiveImage = customSubtitle;
            }
        }

        string effectiveFit = (isStateActive && !string.IsNullOrWhiteSpace(activeImagePath) && File.Exists(activeImagePath))
            ? (activeImageFitMode ?? "Cover")
            : (imageFitMode ?? "Cover");

        int effectiveScale = (isStateActive && !string.IsNullOrWhiteSpace(activeImagePath) && File.Exists(activeImagePath))
            ? activeImageScale
            : imageScale;

        int effectiveOffX = (isStateActive && !string.IsNullOrWhiteSpace(activeImagePath) && File.Exists(activeImagePath))
            ? activeImageOffsetX
            : imageOffsetX;

        int effectiveOffY = (isStateActive && !string.IsNullOrWhiteSpace(activeImagePath) && File.Exists(activeImagePath))
            ? activeImageOffsetY
            : imageOffsetY;

        bool hasValidImage = false;
        if (!string.IsNullOrWhiteSpace(effectiveImage) && File.Exists(effectiveImage))
        {
            try
            {
                SKBitmap? bmpToDraw = null;
                if (MediaValidationService.IsAnimatedOrVideo(effectiveImage))
                {
                    bmpToDraw = MediaFrameExtractor.GetFirstFrameBitmap(effectiveImage, effectiveFit, effectiveScale, effectiveOffX, effectiveOffY);
                }
                else if (effectiveImage.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    bmpToDraw = SvgRasterizer.RasterizeSvg(effectiveImage, ButtonWidth * 2, ButtonHeight * 2);
                }
                else if (effectiveImage.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || effectiveImage.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    bmpToDraw = WindowsAppIconExtractor.ExtractHighResAppIcon(effectiveImage, 128);
                }
                else
                {
                    bmpToDraw = SKBitmap.Decode(effectiveImage);
                }

                if (bmpToDraw != null)
                {
                    using (bmpToDraw)
                    {
                        var destRect = new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2);
                        if (MediaValidationService.IsAnimatedOrVideo(effectiveImage))
                        {
                            using var imgPaint = new SKPaint { IsAntialias = true, IsDither = true };
                            canvas.DrawBitmap(bmpToDraw, destRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), imgPaint);
                        }
                        else
                        {
                            DrawTransformedBitmap(canvas, bmpToDraw, destRect, effectiveFit, effectiveScale, effectiveOffX, effectiveOffY);
                        }
                        hasValidImage = true;
                    }
                }
            }
            catch { }
        }

        if (hasValidImage && isPressed)
        {
            using var tintPaint = new SKPaint { Color = new SKColor(0x00, 0xFF, 0xCC, 0x55) };
            canvas.DrawRoundRect(buttonRect, tintPaint);
        }

        // 4. Treść tekstowa z podziałem na wiersze tytułu i podtytułu
        (string actionIcon, string actionSub) = GetActionDetails(action, buttonNumber, isActiveState);
        string? effectiveCustomTitle = (isActiveState && !string.IsNullOrWhiteSpace(activeCustomTitle)) ? activeCustomTitle : customTitle;
        string? effectiveCustomSubtitle = (isActiveState && !string.IsNullOrWhiteSpace(activeCustomSubtitle)) ? activeCustomSubtitle : customSubtitle;

        string finalTitle = !string.IsNullOrWhiteSpace(effectiveCustomTitle) ? effectiveCustomTitle : actionIcon;
        string finalSub = !string.IsNullOrWhiteSpace(effectiveCustomSubtitle) ? effectiveCustomSubtitle : actionSub;

        // Dedykowany kolor tytułu
        string? effectiveTextColorHex = (isActiveState && !string.IsNullOrWhiteSpace(activeTextColorHex)) ? activeTextColorHex : textColorHex;
        SKColor titleCol = isPressed ? new SKColor(0x00, 0xFF, 0xCC) : (isActiveState ? new SKColor(0x00, 0xFF, 0xCC) : new SKColor(0xFF, 0xFF, 0xFF));
        if (action == DeckActionType.HomeAssistantToggle || action == DeckActionType.HomeAssistantTurnOn)
        {
            titleCol = isPressed
                ? new SKColor(0xFF, 0xEA, 0x00)
                : (isActiveState ? new SKColor(0xFF, 0xD7, 0x00) : new SKColor(0x71, 0x71, 0x7A));
        }
        if (!string.IsNullOrWhiteSpace(effectiveTextColorHex) && SKColor.TryParse(effectiveTextColorHex, out var parsedTitle))
        {
            titleCol = isPressed ? new SKColor(0x00, 0xFF, 0xCC) : parsedTitle;
        }

        // Dedykowany kolor podtytułu
        string? effectiveSubColorHex = (isActiveState && !string.IsNullOrWhiteSpace(activeSubtitleColorHex)) ? activeSubtitleColorHex : subtitleColorHex;
        SKColor subCol = isPressed ? new SKColor(0x00, 0xFF, 0xCC) : (isActiveState ? new SKColor(0xFF, 0xFF, 0xFF) : new SKColor(0xA1, 0xA1, 0xAA));
        if (!string.IsNullOrWhiteSpace(effectiveSubColorHex) && SKColor.TryParse(effectiveSubColorHex, out var parsedSub))
        {
            subCol = isPressed ? new SKColor(0x00, 0xFF, 0xCC) : parsedSub;
        }
        else if (!string.IsNullOrWhiteSpace(effectiveTextColorHex) && SKColor.TryParse(effectiveTextColorHex, out var fallbackSub))
        {
            subCol = isPressed ? new SKColor(0x00, 0xFF, 0xCC) : fallbackSub;
        }

        var titleLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(finalTitle))
        {
            var split = finalTitle.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            titleLines.AddRange(split.Select(s => s.Trim()));
        }
        var subLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(finalSub))
        {
            var split = finalSub.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            subLines.AddRange(split.Select(s => s.Trim()));
        }

        var textLines = new List<string>();
        textLines.AddRange(titleLines);
        textLines.AddRange(subLines);

        // 5. Analiza fontu i rozmiaru tekstu

        if (textLines.Count > 0)
        {
            string chosenFont = !string.IsNullOrWhiteSpace(fontFamily) ? fontFamily : "Segoe UI";
            var weight = isBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
            var slant = isItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
            var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
            SKTypeface typeface;
            try
            {
                typeface = SKTypeface.FromFamilyName(chosenFont, style) 
                           ?? SKTypeface.FromFamilyName("Segoe UI", style);
            }
            catch
            {
                typeface = SKTypeface.Default;
            }

            // Dobór rozmiaru fontu
            float effectiveSize;
            if (fontSize > 0)
            {
                effectiveSize = Math.Clamp(fontSize, 7, 24);
            }
            else
            {
                if (textLines.Count == 1 && IsEmojiOrSymbol(textLines[0]))
                {
                    effectiveSize = 26f;
                }
                else
                {
                    effectiveSize = textLines.Count switch
                    {
                        1 => textLines[0].Length <= 3 ? 20f : (textLines[0].Length <= 6 ? 13f : 10.5f),
                        2 => 11f,
                        3 => 9f,
                        _ => 8f
                    };
                }
            }

            // Dynamiczne skalowanie aby długi tekst (lub powtarzające się litery np. xxxxxx) nie wystawał poza ekran LCD 72x72
            var primaryTypeface = ResolveTypeface(textLines[0], fontFamily, isBold, isItalic);
            while (effectiveSize > 7.5f)
            {
                using var testFont = new SKFont(primaryTypeface, effectiveSize);
                float maxW = 0;
                foreach (var line in textLines)
                {
                    float w = testFont.MeasureText(line);
                    if (w > maxW) maxW = w;
                }
                if (maxW <= ButtonWidth - 10) break;
                effectiveSize -= 0.5f;
            }

            using var font = new SKFont(primaryTypeface, effectiveSize);

            float lineSpacing = effectiveSize * 1.25f;
            float totalBlockHeight = textLines.Count * lineSpacing;
            float startY = (ButtonHeight - totalBlockHeight) / 2f + effectiveSize * 0.88f;

            using var textPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
                IsDither = true
            };

            for (int i = 0; i < textLines.Count; i++)
            {
                float lineY = startY + (i * lineSpacing);
                if (lineY > 0 && lineY < ButtonHeight)
                {
                    bool isSubLine = i >= titleLines.Count;
                    SKColor lineCol = isSubLine ? subCol : titleCol;
                    textPaint.Color = lineCol;

                    bool isLineEmoji = IsEmojiOrSymbol(textLines[i]);
                    using var currentTf = ResolveTypeface(textLines[i], fontFamily, isBold, isItalic);
                    using var currentFont = new SKFont(currentTf, effectiveSize);

                    // Rysowanie czystego tekstu w wybranym kolorze (bez białej poświaty/obrysu)
                    canvas.DrawText(textLines[i], ButtonWidth / 2f, lineY, SKTextAlign.Center, currentFont, textPaint);

                    // Obsługa przekreślenia tekstu (strikethrough)
                    if (isStrikeThrough && !isLineEmoji)
                    {
                        float textWidth = currentFont.MeasureText(textLines[i]);
                        float strikeStartX = (ButtonWidth - textWidth) / 2f;
                        float strikeY = lineY - (effectiveSize * 0.32f);
                        using var strikePaint = new SKPaint
                        {
                            Color = lineCol,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = Math.Clamp(effectiveSize * 0.09f, 1f, 2f),
                            IsAntialias = true
                        };
                        canvas.DrawLine(strikeStartX, strikeY, strikeStartX + textWidth, strikeY, strikePaint);
                    }
                }
            }
        }

        canvas.Restore();

        // 6. Zaokrąglona ramka na krawędzi
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = isPressed ? 3f : (isActiveState ? 2f : 1.5f),
            Color = isPressed ? new SKColor(0x00, 0xFF, 0xCC) : (isActiveState ? new SKColor(0x00, 0xFF, 0xCC) : new SKColor(0x22, 0x28, 0x3D)),
            IsAntialias = true
        };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        // Jakość 100: absolutny brak artefaktów DCT/Gibbsa ("mosquito noise") oraz subsamplingu 4:2:0 na ekranie LCD
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    public static byte[] CreateButtonJpeg(int buttonNumber, string title = "", bool isPressed = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        canvas.Clear(new SKColor(0, 0, 0));

        var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        SKColor bgColor = isPressed ? new SKColor(0x3B, 0x1A, 0x6E) : new SKColor(0x18, 0x18, 0x22);
        using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true, IsDither = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        using var fontBig = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 26);
        string numStr = buttonNumber.ToString();

        using var numOutline = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 220),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.4f,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };
        using var numPaint = new SKPaint
        {
            Color = isPressed ? new SKColor(0xFF, 0xFF, 0xFF) : new SKColor(0x8A, 0x92, 0xB2),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            IsDither = true
        };

        canvas.DrawText(numStr, ButtonWidth / 2f, 38, SKTextAlign.Center, fontBig, numOutline);
        canvas.DrawText(numStr, ButtonWidth / 2f, 38, SKTextAlign.Center, fontBig, numPaint);

        string subText = !string.IsNullOrEmpty(title) ? title : (isPressed ? "KLIK" : $"BTN {buttonNumber}");
        using var fontSmall = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 10);

        using var subOutline = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 200),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.6f,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };
        using var subPaint = new SKPaint
        {
            Color = isPressed ? new SKColor(0x00, 0xFF, 0xCC) : new SKColor(0x5A, 0x60, 0x7E),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            IsDither = true
        };

        canvas.DrawText(subText, ButtonWidth / 2f, 56, SKTextAlign.Center, fontSmall, subOutline);
        canvas.DrawText(subText, ButtonWidth / 2f, 56, SKTextAlign.Center, fontSmall, subPaint);

        canvas.Restore();

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = isPressed ? 3f : 1.5f,
            Color = isPressed ? new SKColor(0x9D, 0x4E, 0xDD) : new SKColor(0x2E, 0x30, 0x48),
            IsAntialias = true
        };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    #region Live Data & System Monitoring Renderers (Etap 3)
    public static byte[] RenderMetricGaugeKeyJpeg(string header, string valueText, float percent, SKColor accentColor, bool isPressed = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        canvas.Clear(new SKColor(0, 0, 0));
        var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        // Tło
        SKColor bg = isPressed ? new SKColor(0x13, 0x24, 0x38) : new SKColor(0x0C, 0x10, 0x19);
        using (var bgPaint = new SKPaint { Color = bg, Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        // 1. Nagłówek (np. CPU, RAM, GPU)
        using (var fontHeader = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f))
        using (var headerPaint = new SKPaint { Color = new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(header, ButtonWidth / 2f, 18, SKTextAlign.Center, fontHeader, headerPaint);
        }

        // 2. Wartość główna (np. 42%, 8.4G)
        float fontSize = valueText.Length <= 4 ? 18f : (valueText.Length <= 6 ? 15f : 12f);
        using (var fontValue = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), fontSize))
        using (var valPaint = new SKPaint { Color = isPressed ? new SKColor(0xFF, 0xFF, 0xFF) : accentColor, Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(valueText, ButtonWidth / 2f, 43, SKTextAlign.Center, fontValue, valPaint);
        }

        // 3. Pasek postępu (Gauge / Progress meter)
        float barX = 8f;
        float barY = 56f;
        float barW = ButtonWidth - 16f;
        float barH = 3.5f;
        var barTrack = new SKRoundRect(new SKRect(barX, barY, barX + barW, barY + barH), 2, 2);
        using (var trackPaint = new SKPaint { Color = new SKColor(0x1A, 0x22, 0x34), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(barTrack, trackPaint);
        }

        float fillW = Math.Clamp(barW * (percent / 100f), 0f, barW);
        if (fillW > 0.5f)
        {
            var barFill = new SKRoundRect(new SKRect(barX, barY, barX + fillW, barY + barH), 2, 2);
            using var fillPaint = new SKPaint { Color = accentColor, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(barFill, fillPaint);
        }

        canvas.Restore();

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = isPressed ? 2.5f : 1.5f,
            Color = isPressed ? new SKColor(0x00, 0xF0, 0xB5) : new SKColor(0x1E, 0x29, 0x3B),
            IsAntialias = true
        };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    public static byte[] RenderCpuUsageJpeg(float cpuPercent, bool isPressed = false)
    {
        SKColor color = cpuPercent switch
        {
            > 85f => new SKColor(0xEF, 0x44, 0x44), // Czerwony
            > 70f => new SKColor(0xF5, 0x9E, 0x0B), // Bursztynowy
            _ => new SKColor(0x00, 0xF0, 0xB5)      // Szmaragdowy
        };
        return RenderMetricGaugeKeyJpeg("CPU", $"{cpuPercent:0}%", cpuPercent, color, isPressed);
    }

    public static byte[] RenderRamUsageJpeg(float ramPercent, float ramGb, bool isPressed = false)
    {
        SKColor color = ramPercent > 85f ? new SKColor(0xEF, 0x44, 0x44) : new SKColor(0x38, 0xBD, 0xF8);
        string ramText = $"{ramGb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}G";
        return RenderMetricGaugeKeyJpeg("RAM", ramText, ramPercent, color, isPressed);
    }

    public static byte[] RenderGpuUsageJpeg(float gpuPercent, bool isPressed = false)
    {
        SKColor color = gpuPercent > 85f ? new SKColor(0xEF, 0x44, 0x44) : new SKColor(0xA7, 0x8B, 0xFA);
        return RenderMetricGaugeKeyJpeg("GPU", $"{gpuPercent:0}%", gpuPercent, color, isPressed);
    }

    public static byte[] RenderCpuTempJpeg(float tempC, bool isPressed = false)
    {
        SKColor color = tempC > 80f ? new SKColor(0xEF, 0x44, 0x44) : (tempC > 65f ? new SKColor(0xF5, 0x9E, 0x0B) : new SKColor(0x38, 0xBD, 0xF8));
        string tempText = tempC > 0.5f ? $"{tempC:0}°" : "--";
        return RenderMetricGaugeKeyJpeg("CPU °C", tempText, Math.Clamp(tempC, 0f, 100f), color, isPressed);
    }

    public static byte[] RenderGpuTempJpeg(float tempC, bool isPressed = false)
    {
        SKColor color = tempC > 80f ? new SKColor(0xEF, 0x44, 0x44) : (tempC > 65f ? new SKColor(0xF5, 0x9E, 0x0B) : new SKColor(0x38, 0xBD, 0xF8));
        string tempText = tempC > 0.5f ? $"{tempC:0}°" : "--";
        return RenderMetricGaugeKeyJpeg("GPU °C", tempText, Math.Clamp(tempC, 0f, 100f), color, isPressed);
    }

    public static byte[] RenderNetworkSpeedJpeg(float downMbps, float upMbps, bool isPressed = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0, 0, 0));
        var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        using (var bgPaint = new SKPaint { Color = isPressed ? new SKColor(0x13, 0x24, 0x38) : new SKColor(0x0C, 0x10, 0x19), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        using (var fontHeader = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f))
        using (var headerPaint = new SKPaint { Color = new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText("SIEĆ", ButtonWidth / 2f, 18, SKTextAlign.Center, fontHeader, headerPaint);
        }

        using (var fontVal = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 13f))
        using (var valPaint = new SKPaint { Color = new SKColor(0x38, 0xBD, 0xF8), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            string downText = downMbps < 100
                ? $"↓ {downMbps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"
                : $"↓ {downMbps:0}";
            canvas.DrawText(downText, ButtonWidth / 2f, 38, SKTextAlign.Center, fontVal, valPaint);
        }

        using (var fontSub = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9f))
        using (var subPaint = new SKPaint { Color = new SKColor(0x00, 0xF0, 0xB5), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText("Mb/s", ButtonWidth / 2f, 54, SKTextAlign.Center, fontSub, subPaint);
        }

        canvas.Restore();

        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(0x1E, 0x29, 0x3B), IsAntialias = true };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    public static byte[] RenderDiskUsageJpeg(float diskPercent, bool isPressed = false)
    {
        SKColor color = diskPercent > 85f ? new SKColor(0xEF, 0x44, 0x44) : new SKColor(0xF5, 0x9E, 0x0B);
        return RenderMetricGaugeKeyJpeg("DYSK C:", $"{diskPercent:0}%", diskPercent, color, isPressed);
    }

    public static byte[] RenderClockJpeg(DateTime time, bool isPressed = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0, 0, 0));
        var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        using (var bgPaint = new SKPaint { Color = isPressed ? new SKColor(0x13, 0x24, 0x38) : new SKColor(0x0C, 0x10, 0x19), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        string dayName = time.ToString("ddd", System.Globalization.CultureInfo.CurrentCulture).ToUpperInvariant();
        using (var fontHeader = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9f))
        using (var headerPaint = new SKPaint { Color = new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(dayName, ButtonWidth / 2f, 17, SKTextAlign.Center, fontHeader, headerPaint);
        }

        using (var fontTime = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 18f))
        using (var timePaint = new SKPaint { Color = new SKColor(0x00, 0xF0, 0xB5), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(time.ToString("HH:mm"), ButtonWidth / 2f, 41, SKTextAlign.Center, fontTime, timePaint);
        }

        using (var fontSec = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9f))
        using (var secPaint = new SKPaint { Color = new SKColor(0x38, 0xBD, 0xF8), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText($":{time.Second:D2}", ButtonWidth / 2f, 56, SKTextAlign.Center, fontSec, secPaint);
        }

        canvas.Restore();

        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(0x1E, 0x29, 0x3B), IsAntialias = true };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    public static byte[] RenderDateJpeg(DateTime date, bool isPressed = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0, 0, 0));
        var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        using (var bgPaint = new SKPaint { Color = isPressed ? new SKColor(0x13, 0x24, 0x38) : new SKColor(0x0C, 0x10, 0x19), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        string monthName = date.ToString("MMM", System.Globalization.CultureInfo.CurrentCulture).ToUpperInvariant();
        using (var fontHeader = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f))
        using (var headerPaint = new SKPaint { Color = new SKColor(0xEF, 0x44, 0x44), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(monthName, ButtonWidth / 2f, 17, SKTextAlign.Center, fontHeader, headerPaint);
        }

        using (var fontDay = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22f))
        using (var dayPaint = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(date.Day.ToString(), ButtonWidth / 2f, 43, SKTextAlign.Center, fontDay, dayPaint);
        }

        string dayOfWeek = date.ToString("ddd", System.Globalization.CultureInfo.CurrentCulture).ToUpperInvariant();
        using (var fontSub = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8.5f))
        using (var subPaint = new SKPaint { Color = new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(dayOfWeek, ButtonWidth / 2f, 56, SKTextAlign.Center, fontSub, subPaint);
        }

        canvas.Restore();

        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(0x1E, 0x29, 0x3B), IsAntialias = true };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    public static byte[] RenderFpsJpeg(float fps, bool isPressed = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        canvas.Clear(new SKColor(0, 0, 0));
        var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        // Tło
        SKColor bg = isPressed ? new SKColor(0x13, 0x24, 0x38) : new SKColor(0x0C, 0x10, 0x19);
        using (var bgPaint = new SKPaint { Color = bg, Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        // 1. Nagłówek "FPS"
        using (var fontHeader = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f))
        using (var headerPaint = new SKPaint { Color = new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText("FPS", ButtonWidth / 2f, 20, SKTextAlign.Center, fontHeader, headerPaint);
        }

        // 2. Wartość główna FPS bez sztucznego paska limitu
        string valueText = fps <= 0.5f ? "--" : $"{fps:0}";
        SKColor accentColor = fps <= 0.5f
            ? new SKColor(0x64, 0x74, 0x8B)
            : (fps >= 60f
                ? new SKColor(0x00, 0xF0, 0xB5)
                : (fps >= 30f ? new SKColor(0xFB, 0xBF, 0x24) : new SKColor(0xEF, 0x44, 0x44)));

        float fontSize = valueText.Length <= 3 ? 24f : (valueText.Length <= 4 ? 20f : 16f);
        using (var fontValue = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), fontSize))
        using (var valPaint = new SKPaint { Color = isPressed ? new SKColor(0xFF, 0xFF, 0xFF) : accentColor, Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(valueText, ButtonWidth / 2f, 48, SKTextAlign.Center, fontValue, valPaint);
        }

        canvas.Restore();

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = isPressed ? 2.5f : 1.5f,
            Color = isPressed ? new SKColor(0x00, 0xF0, 0xB5) : new SKColor(0x1E, 0x29, 0x3B),
            IsAntialias = true
        };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    public static byte[] RenderNowPlayingJpeg(string artist, string title, bool isPlaying, bool isPressed = false, byte[]? artworkJpeg = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0, 0, 0));
        var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        using (var bgPaint = new SKPaint { Color = isPressed ? new SKColor(0x13, 0x24, 0x38) : new SKColor(0x0C, 0x10, 0x19), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        // Jeśli dostępna jest okładka albumu, narysuj jako tło z subtelną winietą
        if (artworkJpeg != null && artworkJpeg.Length > 0)
        {
            try
            {
                using var artBmp = SKBitmap.Decode(artworkJpeg);
                if (artBmp != null)
                {
                    var destRect = new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2);
                    using var artPaint = new SKPaint { IsAntialias = true, IsDither = true, Color = new SKColor(255, 255, 255, 160) };
                    canvas.DrawBitmap(artBmp, destRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), artPaint);

                    using var scrimPaint = new SKPaint { Color = new SKColor(0, 0, 0, 140), Style = SKPaintStyle.Fill };
                    canvas.DrawRoundRect(buttonRect, scrimPaint);
                }
            }
            catch { }
        }

        using (var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 14f))
        using (var iconPaint = new SKPaint { Color = isPlaying ? new SKColor(0x10, 0xB9, 0x81) : new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(isPlaying ? "▶ 🎵" : "⏸ 🎵", ButtonWidth / 2f, 20, SKTextAlign.Center, fontIcon, iconPaint);
        }

        string cleanTitle = !string.IsNullOrWhiteSpace(title) ? (title.Length > 9 ? title.Substring(0, 8) + ".." : title) : "MUZYKA";
        using (var fontTitle = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f))
        using (var titlePaint = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(cleanTitle, ButtonWidth / 2f, 38, SKTextAlign.Center, fontTitle, titlePaint);
        }

        string cleanArtist = !string.IsNullOrWhiteSpace(artist) ? (artist.Length > 10 ? artist.Substring(0, 9) + ".." : artist) : (isPlaying ? "GRA" : "PAUZA");
        using (var fontArtist = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8.5f))
        using (var artistPaint = new SKPaint { Color = new SKColor(0x38, 0xBD, 0xF8), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(cleanArtist, ButtonWidth / 2f, 54, SKTextAlign.Center, fontArtist, artistPaint);
        }

        canvas.Restore();

        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = isPlaying ? new SKColor(0x10, 0xB9, 0x81) : new SKColor(0x1E, 0x29, 0x3B), IsAntialias = true };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Renderuje bitmapę SkiaSharp do standardowego 72x72 JPEG z ramką studia LCD.
    /// </summary>
    public static byte[] RenderBitmapJpeg(SKBitmap bmp, bool isPressed = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0, 0, 0));

        var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        var destRect = new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2);
        using (var imgPaint = new SKPaint { IsAntialias = true, IsDither = true })
        {
            canvas.DrawBitmap(bmp, destRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), imgPaint);
        }

        if (isPressed)
        {
            using var tintPaint = new SKPaint { Color = new SKColor(0x00, 0xFF, 0xCC, 0x55) };
            canvas.DrawRoundRect(buttonRect, tintPaint);
        }
        canvas.Restore();

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = isPressed ? 3f : 1.5f,
            Color = isPressed ? new SKColor(0x00, 0xFF, 0xCC) : new SKColor(0x22, 0x28, 0x3D),
            IsAntialias = true
        };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Renderuje dedykowany kafelek stanu streamu OBS (LIVE / STREAM).
    /// </summary>
    public static byte[] RenderObsStreamJpeg(bool isStreaming, bool isPressed = false)
    {
        string key = $"obs_stream:{isStreaming}:{isPressed}";
        return s_cache.GetOrAdd(key, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0, 0, 0));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isStreaming
                ? (isPressed ? new SKColor(0x52, 0x18, 0x99) : new SKColor(0x3B, 0x12, 0x6E))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint
            {
                Color = isStreaming ? new SKColor(0x00, 0xFF, 0xCC) : new SKColor(0xCC, 0xD4, 0xEA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("📡", ButtonWidth / 2f, 36, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10.5f);
            using var labelPaint = new SKPaint
            {
                Color = isStreaming ? new SKColor(0xFF, 0x33, 0x66) : new SKColor(0x9E, 0xAC, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            string label = isStreaming ? "● LIVE" : "STREAM";
            canvas.DrawText(label, ButtonWidth / 2f, 58, SKTextAlign.Center, fontLabel, labelPaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isStreaming ? 2.5f : 1.5f,
                Color = isStreaming ? new SKColor(0x91, 0x46, 0xFF) : new SKColor(0x22, 0x28, 0x3D),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje dedykowany kafelek stanu nagrywania OBS (REC / OFF).
    /// </summary>
    public static byte[] RenderObsRecordJpeg(bool isRecording, bool isPressed = false)
    {
        string key = $"obs_record:{isRecording}:{isPressed}";
        return s_cache.GetOrAdd(key, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0, 0, 0));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isRecording
                ? (isPressed ? new SKColor(0x66, 0x0E, 0x15) : new SKColor(0x42, 0x09, 0x0E))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint
            {
                Color = isRecording ? new SKColor(0xFF, 0x22, 0x33) : new SKColor(0xAA, 0xB4, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🔴", ButtonWidth / 2f, 36, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10.5f);
            using var labelPaint = new SKPaint
            {
                Color = isRecording ? new SKColor(0xFF, 0x44, 0x55) : new SKColor(0x9E, 0xAC, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            string label = isRecording ? "● REC" : "NAGRAJ";
            canvas.DrawText(label, ButtonWidth / 2f, 58, SKTextAlign.Center, fontLabel, labelPaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isRecording ? 2.5f : 1.5f,
                Color = isRecording ? new SKColor(0xFF, 0x22, 0x33) : new SKColor(0x22, 0x28, 0x3D),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje dedykowany kafelek stanu wirtualnej kamery OBS (VCAM / OFF).
    /// </summary>
    public static byte[] RenderObsVirtualCamJpeg(bool isVirtualCamActive, bool isPressed = false)
    {
        string key = $"obs_vcam:{isVirtualCamActive}:{isPressed}";
        return s_cache.GetOrAdd(key, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0, 0, 0));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isVirtualCamActive
                ? (isPressed ? new SKColor(0x0A, 0x47, 0x44) : new SKColor(0x06, 0x2E, 0x2C))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint
            {
                Color = isVirtualCamActive ? new SKColor(0x00, 0xE5, 0xFF) : new SKColor(0xAA, 0xB4, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("📹", ButtonWidth / 2f, 36, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f);
            using var labelPaint = new SKPaint
            {
                Color = isVirtualCamActive ? new SKColor(0x00, 0xF5, 0xD4) : new SKColor(0x9E, 0xAC, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            string label = isVirtualCamActive ? "● V-CAM" : "KAMERA";
            canvas.DrawText(label, ButtonWidth / 2f, 58, SKTextAlign.Center, fontLabel, labelPaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isVirtualCamActive ? 2.5f : 1.5f,
                Color = isVirtualCamActive ? new SKColor(0x00, 0xE5, 0xFF) : new SKColor(0x22, 0x28, 0x3D),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje dedykowany kafelek bufora powtórek OBS (Instant Replay / Save).
    /// </summary>
    public static byte[] RenderObsReplayBufferJpeg(bool isReplayBufferActive, bool isPressed = false)
    {
        string key = $"obs_replay:{isReplayBufferActive}:{isPressed}";
        return s_cache.GetOrAdd(key, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0, 0, 0));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isReplayBufferActive
                ? (isPressed ? new SKColor(0x5E, 0x36, 0x05) : new SKColor(0x3E, 0x24, 0x03))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint
            {
                Color = isReplayBufferActive ? new SKColor(0xFF, 0xA0, 0x00) : new SKColor(0xAA, 0xB4, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("💾", ButtonWidth / 2f, 36, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f);
            using var labelPaint = new SKPaint
            {
                Color = isReplayBufferActive ? new SKColor(0xFF, 0xB7, 0x4D) : new SKColor(0x9E, 0xAC, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            string label = isReplayBufferActive ? "● REPLAY" : "POWTÓRKA";
            canvas.DrawText(label, ButtonWidth / 2f, 58, SKTextAlign.Center, fontLabel, labelPaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isReplayBufferActive ? 2.5f : 1.5f,
                Color = isReplayBufferActive ? new SKColor(0xFF, 0xA0, 0x00) : new SKColor(0x22, 0x28, 0x3D),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje dedykowany kafelek sceny OBS (Nazwa sceny, status aktywności na żywo).
    /// </summary>
    public static byte[] RenderObsSceneJpeg(string sceneName, bool isActiveScene, bool isPressed = false)
    {
        string label = !string.IsNullOrWhiteSpace(sceneName) ? sceneName.Trim() : "SCENA";
        string key = $"obs_scene:{label}:{isActiveScene}:{isPressed}";
        return s_cache.GetOrAdd(key, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0, 0, 0));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isActiveScene
                ? (isPressed ? new SKColor(0x06, 0x4E, 0x3B) : new SKColor(0x02, 0x2C, 0x22))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 20);
            using var iconPaint = new SKPaint
            {
                Color = isActiveScene ? new SKColor(0x10, 0xB9, 0x81) : new SKColor(0xAA, 0xB4, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🎬", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            string displayLabel = label.Length > 8 ? label.Substring(0, 7) + "…" : label.ToUpperInvariant();
            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isActiveScene ? new SKColor(0x34, 0xD3, 0x99) : new SKColor(0x9E, 0xAC, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(displayLabel, ButtonWidth / 2f, 50, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = isActiveScene ? new SKColor(0x10, 0xB9, 0x81) : new SKColor(0x60, 0x6E, 0x8C),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            string badgeText = isActiveScene ? "● AKTYWNA" : "SCENA";
            canvas.DrawText(badgeText, ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isActiveScene ? 2.5f : 1.5f,
                Color = isActiveScene ? new SKColor(0x10, 0xB9, 0x81) : new SKColor(0x22, 0x28, 0x3D),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje dedykowany kafelek LCD dla przełącznika trybu studyjnego OBS (Studio Mode).
    /// </summary>
    public static byte[] RenderObsStudioModeJpeg(bool isStudioMode, bool isPressed = false)
    {
        string cacheKey = $"obs_studiomode_{isStudioMode}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;

            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isStudioMode
                ? (isPressed ? new SKColor(0x0C, 0x4A, 0x6E) : new SKColor(0x08, 0x2F, 0x49))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint
            {
                Color = isStudioMode ? new SKColor(0x38, 0xBD, 0xF8) : new SKColor(0x94, 0xA3, 0xB8),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🎭", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f);
            using var labelPaint = new SKPaint
            {
                Color = isStudioMode ? new SKColor(0x7D, 0xD3, 0xFC) : new SKColor(0xE2, 0xE8, 0xF0),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("STUDIO", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8.5f);
            using var badgePaint = new SKPaint
            {
                Color = isStudioMode ? new SKColor(0x38, 0xBD, 0xF8) : new SKColor(0x64, 0x74, 0x8B),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            string badgeText = isStudioMode ? "● WŁĄCZONE" : "WYŁĄCZONE";
            canvas.DrawText(badgeText, ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isStudioMode ? 2.5f : 1.5f,
                Color = isStudioMode ? new SKColor(0x38, 0xBD, 0xF8) : new SKColor(0x1E, 0x29, 0x3B),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje dedykowany kafelek LCD dla wyzwalacza przejścia OBS (Trigger Transition).
    /// </summary>
    public static byte[] RenderObsTransitionJpeg(bool isPressed = false)
    {
        string cacheKey = $"obs_transition_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;

            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x4C, 0x1D, 0x95) : new SKColor(0x2E, 0x10, 0x65);

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint
            {
                Color = new SKColor(0xC0, 0x84, 0xFC),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🔀", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f);
            using var labelPaint = new SKPaint
            {
                Color = new SKColor(0xE9, 0xD5, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("PRZEJŚCIE", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = new SKColor(0xA8, 0x55, 0xF7),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("TRANSITION", ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isPressed ? 2.5f : 1.5f,
                Color = isPressed ? new SKColor(0xD8, 0xB4, 0xFE) : new SKColor(0x7E, 0x22, 0xCE),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD dla trybu czatu Twitch tylko dla emotek (Emote-Only).
    /// </summary>
    public static byte[] RenderTwitchEmoteOnlyJpeg(bool isEmoteOnly, bool isPressed = false)
    {
        string cacheKey = $"twitch_emote_{isEmoteOnly}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isEmoteOnly
                ? (isPressed ? new SKColor(0x4C, 0x1D, 0x95) : new SKColor(0x2E, 0x10, 0x65))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint { Color = isEmoteOnly ? new SKColor(0xD8, 0xB4, 0xFE) : new SKColor(0x94, 0xA3, 0xB8), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("😀", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f);
            using var labelPaint = new SKPaint { Color = isEmoteOnly ? new SKColor(0xF3, 0xE8, 0xFF) : new SKColor(0xE2, 0xE8, 0xF0), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("EMOTE", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8.5f);
            using var badgePaint = new SKPaint { Color = isEmoteOnly ? new SKColor(0xC0, 0x84, 0xFC) : new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true };
            string badgeText = isEmoteOnly ? "● WŁĄCZONE" : "WYŁĄCZONE";
            canvas.DrawText(badgeText, ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isEmoteOnly ? 2.5f : 1.5f,
                Color = isEmoteOnly ? new SKColor(0xA8, 0x55, 0xF7) : new SKColor(0x1E, 0x29, 0x3B),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD dla trybu czatu Twitch tylko dla subskrybentów (Sub-Only).
    /// </summary>
    public static byte[] RenderTwitchSubOnlyJpeg(bool isSubOnly, bool isPressed = false)
    {
        string cacheKey = $"twitch_sub_{isSubOnly}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isSubOnly
                ? (isPressed ? new SKColor(0x58, 0x1C, 0x87) : new SKColor(0x3B, 0x07, 0x64))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint { Color = isSubOnly ? new SKColor(0xFA, 0xCC, 0x15) : new SKColor(0x94, 0xA3, 0xB8), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("⭐", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f);
            using var labelPaint = new SKPaint { Color = isSubOnly ? new SKColor(0xFE, 0xF0, 0x8A) : new SKColor(0xE2, 0xE8, 0xF0), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("SUB-ONLY", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8.5f);
            using var badgePaint = new SKPaint { Color = isSubOnly ? new SKColor(0xFA, 0xCC, 0x15) : new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true };
            string badgeText = isSubOnly ? "● WŁĄCZONE" : "WYŁĄCZONE";
            canvas.DrawText(badgeText, ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isSubOnly ? 2.5f : 1.5f,
                Color = isSubOnly ? new SKColor(0xE9, 0xD5, 0xFF) : new SKColor(0x1E, 0x29, 0x3B),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD dla trybu powolnego czatu Twitch (Slow Mode).
    /// </summary>
    public static byte[] RenderTwitchSlowModeJpeg(bool isSlowMode, int seconds = 30, bool isPressed = false)
    {
        string cacheKey = $"twitch_slow_{isSlowMode}_{seconds}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isSlowMode
                ? (isPressed ? new SKColor(0x1E, 0x1B, 0x4B) : new SKColor(0x31, 0x2E, 0x81))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint { Color = isSlowMode ? new SKColor(0xA5, 0xB4, 0xFC) : new SKColor(0x94, 0xA3, 0xB8), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("🐢", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f);
            using var labelPaint = new SKPaint { Color = isSlowMode ? new SKColor(0xE0, 0xE7, 0xFF) : new SKColor(0xE2, 0xE8, 0xF0), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("SLOW", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8.5f);
            using var badgePaint = new SKPaint { Color = isSlowMode ? new SKColor(0x81, 0x8C, 0xF8) : new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true };
            string badgeText = isSlowMode ? $"● {seconds}S" : "WYŁĄCZONE";
            canvas.DrawText(badgeText, ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isSlowMode ? 2.5f : 1.5f,
                Color = isSlowMode ? new SKColor(0x63, 0x66, 0xF1) : new SKColor(0x1E, 0x29, 0x3B),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD dla czyszczenia czatu Twitch (/clear).
    /// </summary>
    public static byte[] RenderTwitchClearChatJpeg(bool isPressed = false)
    {
        string cacheKey = $"twitch_clear_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x4C, 0x1D, 0x95) : new SKColor(0x2E, 0x10, 0x65);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xE9, 0xD5, 0xFF), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("🧹", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xF3, 0xE8, 0xFF), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("WYCZYŚĆ", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8.5f);
            using var badgePaint = new SKPaint { Color = new SKColor(0xC0, 0x84, 0xFC), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("CZAT", ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isPressed ? 2.5f : 1.5f,
                Color = isPressed ? new SKColor(0xD8, 0xB4, 0xFE) : new SKColor(0x7E, 0x22, 0xCE),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD dla wysyłania predefiniowanej wiadomości Twitch.
    /// </summary>
    public static byte[] RenderTwitchSendMessageJpeg(string? messageText, bool isPressed = false)
    {
        string label = !string.IsNullOrWhiteSpace(messageText) ? messageText.Trim() : "CZAT";
        if (label.Length > 8) label = label[..8];

        string cacheKey = $"twitch_msg_{label}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x58, 0x1C, 0x87) : new SKColor(0x2E, 0x10, 0x65);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xC0, 0x84, 0xFC), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("💬", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("TWITCH", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint { Color = new SKColor(0xD8, 0xB4, 0xFE), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText(label.ToUpperInvariant(), ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isPressed ? 2.5f : 1.5f,
                Color = isPressed ? new SKColor(0xC0, 0x84, 0xFC) : new SKColor(0x93, 0x33, 0xEA),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD dla tworzenia klipu Twitch.
    /// </summary>
    public static byte[] RenderTwitchCreateClipJpeg(bool isPressed = false)
    {
        string cacheKey = $"twitch_clip_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x58, 0x1C, 0x87) : new SKColor(0x2E, 0x10, 0x65);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xF0, 0xAB, 0xFC), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("🎬", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("UTWÓRZ", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8.5f);
            using var badgePaint = new SKPaint { Color = new SKColor(0xC0, 0x84, 0xFC), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("KLIP", ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isPressed ? 2.5f : 1.5f,
                Color = isPressed ? new SKColor(0xE8, 0x79, 0xF9) : new SKColor(0x93, 0x33, 0xEA),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD dla tworzenia znacznika transmisji (Stream Marker) Twitch.
    /// </summary>
    public static byte[] RenderTwitchCreateMarkerJpeg(string? description = null, bool isPressed = false)
    {
        string label = !string.IsNullOrWhiteSpace(description) ? description.Trim() : "MARKER";
        if (label.Length > 8) label = label[..8];

        string cacheKey = $"twitch_marker_{label}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x58, 0x1C, 0x87) : new SKColor(0x2E, 0x10, 0x65);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xF4, 0x72, 0xB6), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("📍", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("ZNACZNIK", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint { Color = new SKColor(0xE8, 0x79, 0xF9), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText(label.ToUpperInvariant(), ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isPressed ? 2.5f : 1.5f,
                Color = isPressed ? new SKColor(0xF4, 0x72, 0xB6) : new SKColor(0xA8, 0x55, 0xF7),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD dla uruchomienia raidu Twitch.
    /// </summary>
    public static byte[] RenderTwitchStartRaidJpeg(string? targetChannel = null, bool isPressed = false)
    {
        string label = !string.IsNullOrWhiteSpace(targetChannel) ? targetChannel.Trim().TrimStart('#') : "START";
        if (label.Length > 8) label = label[..8];

        string cacheKey = $"twitch_raid_{label}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x58, 0x1C, 0x87) : new SKColor(0x2E, 0x10, 0x65);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xC0, 0x84, 0xFC), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("🚀", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 10f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("RAID", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8.5f);
            using var badgePaint = new SKPaint { Color = new SKColor(0xE8, 0x79, 0xF9), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText(label.ToUpperInvariant(), ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isPressed ? 2.5f : 1.5f,
                Color = isPressed ? new SKColor(0xE8, 0x79, 0xF9) : new SKColor(0x93, 0x33, 0xEA),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD dla anulowania raidu Twitch.
    /// </summary>
    public static byte[] RenderTwitchCancelRaidJpeg(bool isPressed = false)
    {
        string cacheKey = $"twitch_cancel_raid_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x7F, 0x1D, 0x1D) : new SKColor(0x45, 0x0A, 0x0A);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xEF, 0x44, 0x44), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("🛑", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("ANULUJ", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8.5f);
            using var badgePaint = new SKPaint { Color = new SKColor(0xFC, 0xA5, 0xA5), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("RAID", ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isPressed ? 2.5f : 1.5f,
                Color = isPressed ? new SKColor(0xF8, 0x71, 0x71) : new SKColor(0xDC, 0x26, 0x26),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD dla uruchomienia przerwy reklamowej (Commercial) Twitch.
    /// </summary>
    public static byte[] RenderTwitchCommercialJpeg(string? durationOrParam = null, bool isPressed = false)
    {
        string label = !string.IsNullOrWhiteSpace(durationOrParam) ? durationOrParam.Trim() : "30s";
        if (!label.EndsWith("s", StringComparison.OrdinalIgnoreCase) && int.TryParse(label, out _))
        {
            label += "s";
        }
        if (label.Length > 8) label = label[..8];

        string cacheKey = $"twitch_commercial_{label}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x78, 0x35, 0x0F) : new SKColor(0x45, 0x1A, 0x03);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xFB, 0xBF, 0x24), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("📺", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("REKLAMA", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8.5f);
            using var badgePaint = new SKPaint { Color = new SKColor(0xFD, 0xBA, 0x74), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText(label.ToUpperInvariant(), ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isPressed ? 2.5f : 1.5f,
                Color = isPressed ? new SKColor(0xFB, 0xBF, 0x24) : new SKColor(0xD9, 0x77, 0x06),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD dla statusu transmisji i licznika widzów YouTube Live (Etap 4.9).
    /// W stanie LIVE prezentuje czerwony motyw YouTube z plakietką "● LIVE" oraz sformatowaną liczbą widzów.
    /// W stanie OFFLINE prezentuje ciemny motyw z napisem "OFFLINE".
    /// </summary>
    public static byte[] RenderYouTubeLiveViewersJpeg(bool isLive, int concurrentViewers, string? channelOrLabel = null, bool isPressed = false)
    {
        string formattedViewers = MasterHub.Core.Integrations.YouTube.YouTubeService.FormatViewerCount(concurrentViewers);
        string bottomLabel = !string.IsNullOrWhiteSpace(channelOrLabel)
            ? channelOrLabel.Trim().TrimStart('@')
            : (isLive ? "WIDZÓW" : "YOUTUBE");
        if (bottomLabel.Length > 9) bottomLabel = bottomLabel[..9];

        string cacheKey = $"yt_live_{isLive}_{formattedViewers}_{bottomLabel}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isLive
                ? (isPressed ? new SKColor(0x99, 0x1B, 0x1B) : new SKColor(0x45, 0x0A, 0x0A))
                : (isPressed ? new SKColor(0x1E, 0x29, 0x3B) : new SKColor(0x11, 0x18, 0x27));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            // Górna belka / plakietka statusu YT LIVE / OFFLINE
            using var fontTop = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8.5f);
            using var topPaint = new SKPaint
            {
                Color = isLive ? new SKColor(0xFF, 0x4D, 0x4D) : new SKColor(0x94, 0xA3, 0xB8),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(isLive ? "● YT LIVE" : "▶️ YOUTUBE", ButtonWidth / 2f, 17, SKTextAlign.Center, fontTop, topPaint);

            // Środkowa główna wartość: liczba widzów lub OFFLINE
            string mainText = isLive ? formattedViewers : "OFFLINE";
            float mainFontSize = isLive ? (formattedViewers.Length > 5 ? 14f : 17f) : 11.5f;
            using var fontMain = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), mainFontSize);
            using var mainPaint = new SKPaint
            {
                Color = isLive ? new SKColor(0xFF, 0xFF, 0xFF) : new SKColor(0x64, 0x74, 0x8B),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(mainText, ButtonWidth / 2f, 42, SKTextAlign.Center, fontMain, mainPaint);

            // Dolny podpis (nazwa kanału lub "WIDZÓW")
            using var fontBottom = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var bottomPaint = new SKPaint
            {
                Color = isLive ? new SKColor(0xFC, 0xA5, 0xA5) : new SKColor(0x47, 0x55, 0x69),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(bottomLabel.ToUpperInvariant(), ButtonWidth / 2f, 61, SKTextAlign.Center, fontBottom, bottomPaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isPressed ? 2.5f : 1.5f,
                Color = isLive
                    ? (isPressed ? new SKColor(0xFF, 0x6B, 0x6B) : new SKColor(0xEF, 0x44, 0x44))
                    : (isPressed ? new SKColor(0x64, 0x74, 0x8B) : new SKColor(0x33, 0x41, 0x55)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje dedykowany kafelek LCD dla przełączania sceny w Streamlabs Desktop (Etap 4.10).
    /// Wykorzystuje charakterystyczny turkusowo-szmaragdowy motyw Streamlabs (#00F5D4).
    /// </summary>
    public static byte[] RenderStreamlabsSceneJpeg(string? sceneName, bool isActiveScene, bool isPressed = false)
    {
        string label = !string.IsNullOrWhiteSpace(sceneName) ? sceneName.Trim() : "SCENA SL";
        string cacheKey = $"streamlabs_scene:{label}:{isActiveScene}:{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isActiveScene
                ? (isPressed ? new SKColor(0x0D, 0x5C, 0x52) : new SKColor(0x07, 0x3B, 0x36))
                : (isPressed ? new SKColor(0x13, 0x2F, 0x33) : new SKColor(0x0C, 0x19, 0x1E));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 21);
            using var iconPaint = new SKPaint
            {
                Color = isActiveScene ? new SKColor(0x00, 0xF5, 0xD4) : new SKColor(0x5E, 0xEA, 0xD4),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🎥", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            string displayLabel = label.Length > 8 ? label.Substring(0, 7) + "…" : label.ToUpperInvariant();
            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isActiveScene ? new SKColor(0xCC, 0xFB, 0xF1) : new SKColor(0xE2, 0xE8, 0xF0),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(displayLabel, ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = isActiveScene ? new SKColor(0x00, 0xF5, 0xD4) : new SKColor(0x4D, 0x9C, 0x93),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            string badgeText = isActiveScene ? "● AKTYWNA" : "STREAMLABS";
            canvas.DrawText(badgeText, ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isActiveScene || isPressed ? 2.5f : 1.5f,
                Color = isActiveScene
                    ? new SKColor(0x00, 0xF5, 0xD4)
                    : (isPressed ? new SKColor(0x2D, 0xD4, 0xBF) : new SKColor(0x11, 0x5E, 0x59)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD (72x72 JPEG) dla przełączania wyciszenia mikrofonu na Discordzie (Etap 5.2).
    /// </summary>
    public static byte[] RenderDiscordMuteJpeg(bool isMuted, bool isPressed = false)
    {
        string cacheKey = $"discord_mute_{isMuted}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isMuted
                ? (isPressed ? new SKColor(0x6E, 0x14, 0x1C) : new SKColor(0x45, 0x0A, 0x10))
                : (isPressed ? new SKColor(0x31, 0x38, 0x8C) : new SKColor(0x18, 0x1C, 0x42));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 21);
            using var iconPaint = new SKPaint
            {
                Color = isMuted ? new SKColor(0xED, 0x42, 0x45) : new SKColor(0x58, 0x65, 0xF2),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(isMuted ? "🔇" : "🎙️", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isMuted ? new SKColor(0xFE, 0xCA, 0xCA) : new SKColor(0xE0, 0xE7, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("DISCORD", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = isMuted ? new SKColor(0xF8, 0x71, 0x71) : new SKColor(0x81, 0x8C, 0xF8),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(isMuted ? "● MUTE" : "MIKROFON", ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isMuted || isPressed ? 2.5f : 1.5f,
                Color = isMuted
                    ? new SKColor(0xED, 0x42, 0x45)
                    : (isPressed ? new SKColor(0x81, 0x8C, 0xF8) : new SKColor(0x58, 0x65, 0xF2)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD (72x72 JPEG) dla przełączania wyciszenia odsłuchu na Discordzie (Etap 5.2).
    /// </summary>
    public static byte[] RenderDiscordDeafenJpeg(bool isDeafened, bool isPressed = false)
    {
        string cacheKey = $"discord_deaf_{isDeafened}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isDeafened
                ? (isPressed ? new SKColor(0x6E, 0x14, 0x1C) : new SKColor(0x45, 0x0A, 0x10))
                : (isPressed ? new SKColor(0x31, 0x38, 0x8C) : new SKColor(0x18, 0x1C, 0x42));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 21);
            using var iconPaint = new SKPaint
            {
                Color = isDeafened ? new SKColor(0xED, 0x42, 0x45) : new SKColor(0x58, 0x65, 0xF2),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(isDeafened ? "🔇" : "🎧", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isDeafened ? new SKColor(0xFE, 0xCA, 0xCA) : new SKColor(0xE0, 0xE7, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("DISCORD", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = isDeafened ? new SKColor(0xF8, 0x71, 0x71) : new SKColor(0x81, 0x8C, 0xF8),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(isDeafened ? "● DEAFEN" : "ODSŁUCH", ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isDeafened || isPressed ? 2.5f : 1.5f,
                Color = isDeafened
                    ? new SKColor(0xED, 0x42, 0x45)
                    : (isPressed ? new SKColor(0x81, 0x8C, 0xF8) : new SKColor(0x58, 0x65, 0xF2)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD (72x72 JPEG) dla przełączania kamery wideo na Discordzie (Etap 5.3).
    /// </summary>
    public static byte[] RenderDiscordCameraJpeg(bool isCameraEnabled, bool isPressed = false)
    {
        string cacheKey = $"discord_cam_{isCameraEnabled}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isCameraEnabled
                ? (isPressed ? new SKColor(0x16, 0x65, 0x34) : new SKColor(0x0A, 0x3F, 0x22))
                : (isPressed ? new SKColor(0x31, 0x38, 0x8C) : new SKColor(0x18, 0x1C, 0x42));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 21);
            using var iconPaint = new SKPaint
            {
                Color = isCameraEnabled ? new SKColor(0x57, 0xF2, 0x87) : new SKColor(0x58, 0x65, 0xF2),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("📷", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isCameraEnabled ? new SKColor(0xDC, 0xFC, 0xE7) : new SKColor(0xE0, 0xE7, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("DISCORD", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = isCameraEnabled ? new SKColor(0x57, 0xF2, 0x87) : new SKColor(0x81, 0x8C, 0xF8),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(isCameraEnabled ? "● CAM ON" : "KAMERA", ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isCameraEnabled || isPressed ? 2.5f : 1.5f,
                Color = isCameraEnabled
                    ? new SKColor(0x23, 0xA5, 0x5A)
                    : (isPressed ? new SKColor(0x81, 0x8C, 0xF8) : new SKColor(0x58, 0x65, 0xF2)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD (72x72 JPEG) dla przełączania udostępniania ekranu (Go Live) na Discordzie (Etap 5.3).
    /// </summary>
    public static byte[] RenderDiscordScreenShareJpeg(bool isScreenSharing, bool isPressed = false)
    {
        string cacheKey = $"discord_screen_{isScreenSharing}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isScreenSharing
                ? (isPressed ? new SKColor(0x6E, 0x14, 0x1C) : new SKColor(0x45, 0x0A, 0x10))
                : (isPressed ? new SKColor(0x31, 0x38, 0x8C) : new SKColor(0x18, 0x1C, 0x42));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 21);
            using var iconPaint = new SKPaint
            {
                Color = isScreenSharing ? new SKColor(0xED, 0x42, 0x45) : new SKColor(0x58, 0x65, 0xF2),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🖥️", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isScreenSharing ? new SKColor(0xFE, 0xCA, 0xCA) : new SKColor(0xE0, 0xE7, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("DISCORD", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = isScreenSharing ? new SKColor(0xF8, 0x71, 0x71) : new SKColor(0x81, 0x8C, 0xF8),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(isScreenSharing ? "● GO LIVE" : "EKRAN", ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isScreenSharing || isPressed ? 2.5f : 1.5f,
                Color = isScreenSharing
                    ? new SKColor(0xED, 0x42, 0x45)
                    : (isPressed ? new SKColor(0x81, 0x8C, 0xF8) : new SKColor(0x58, 0x65, 0xF2)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD (72x72 JPEG) dla Push-To-Talk na Discordzie (Etap 5.4).
    /// </summary>
    public static byte[] RenderDiscordPttJpeg(bool isTransmitting, bool isPressed = false)
    {
        string cacheKey = $"discord_ptt_{isTransmitting}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            bool transmitting = isTransmitting || isPressed;
            SKColor bgColor = transmitting
                ? (isPressed ? new SKColor(0x16, 0x65, 0x34) : new SKColor(0x0A, 0x3F, 0x22))
                : (isPressed ? new SKColor(0x31, 0x38, 0x8C) : new SKColor(0x18, 0x1C, 0x42));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 21);
            using var iconPaint = new SKPaint
            {
                Color = transmitting ? new SKColor(0x57, 0xF2, 0x87) : new SKColor(0x58, 0x65, 0xF2),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(transmitting ? "🗣️" : "🎙️", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = transmitting ? new SKColor(0xDC, 0xFC, 0xE7) : new SKColor(0xE0, 0xE7, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("DISCORD", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = transmitting ? new SKColor(0x57, 0xF2, 0x87) : new SKColor(0x81, 0x8C, 0xF8),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(transmitting ? "● NADAWANIE" : "PTT", ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = transmitting || isPressed ? 2.5f : 1.5f,
                Color = transmitting
                    ? new SKColor(0x23, 0xA5, 0x5A)
                    : (isPressed ? new SKColor(0x81, 0x8C, 0xF8) : new SKColor(0x58, 0x65, 0xF2)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD (72x72 JPEG) dla dołączania do kanału głosowego Discord (Etap 5.5).
    /// </summary>
    public static byte[] RenderDiscordVoiceChannelJpeg(string? channelNameOrId, bool isConnectedToChannel, bool isPressed = false)
    {
        string safeName = string.IsNullOrWhiteSpace(channelNameOrId) ? "KANAŁ" : channelNameOrId.Trim();
        string cacheKey = $"discord_voice_chan_{safeName}_{isConnectedToChannel}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isConnectedToChannel
                ? (isPressed ? new SKColor(0x16, 0x65, 0x34) : new SKColor(0x0A, 0x3F, 0x22))
                : (isPressed ? new SKColor(0x31, 0x38, 0x8C) : new SKColor(0x18, 0x1C, 0x42));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 21);
            using var iconPaint = new SKPaint
            {
                Color = isConnectedToChannel ? new SKColor(0x57, 0xF2, 0x87) : new SKColor(0x58, 0x65, 0xF2),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🔊", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isConnectedToChannel ? new SKColor(0xDC, 0xFC, 0xE7) : new SKColor(0xE0, 0xE7, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("DISCORD", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            string badgeText = isConnectedToChannel ? "● POŁĄCZONO" : (safeName.Length > 8 ? safeName.Substring(0, 8) : safeName);
            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = isConnectedToChannel ? new SKColor(0x57, 0xF2, 0x87) : new SKColor(0x81, 0x8C, 0xF8),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(badgeText.ToUpperInvariant(), ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isConnectedToChannel || isPressed ? 2.5f : 1.5f,
                Color = isConnectedToChannel
                    ? new SKColor(0x23, 0xA5, 0x5A)
                    : (isPressed ? new SKColor(0x81, 0x8C, 0xF8) : new SKColor(0x58, 0x65, 0xF2)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD (72x72 JPEG) dla wyciszania mikrofonu w Microsoft Teams (Etap 5.6).
    /// </summary>
    public static byte[] RenderTeamsMuteJpeg(bool isMuted, bool isPressed = false)
    {
        string cacheKey = $"teams_mute_{isMuted}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isMuted
                ? (isPressed ? new SKColor(0x45, 0x1A, 0x1A) : new SKColor(0x2A, 0x12, 0x12))
                : (isPressed ? new SKColor(0x2B, 0x24, 0x5C) : new SKColor(0x19, 0x18, 0x36));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 21);
            using var iconPaint = new SKPaint
            {
                Color = isMuted ? new SKColor(0xEF, 0x44, 0x44) : new SKColor(0x22, 0xC5, 0x5E),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            string iconStr = isMuted ? "🔇" : "🎙️";
            canvas.DrawText(iconStr, ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = new SKColor(0xC7, 0xD2, 0xFE),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("MS TEAMS", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            string badgeText = isMuted ? "● WYCISZ" : "● MIKRO ON";
            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = isMuted ? new SKColor(0xEF, 0x44, 0x44) : new SKColor(0x22, 0xC5, 0x5E),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(badgeText, ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isMuted || isPressed ? 2.5f : 1.5f,
                Color = isMuted
                    ? new SKColor(0xEF, 0x44, 0x44)
                    : (isPressed ? new SKColor(0x9E, 0xA2, 0xEC) : new SKColor(0x62, 0x64, 0xA7)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD (72x72 JPEG) dla przełączania kamery w Microsoft Teams (Etap 5.6).
    /// </summary>
    public static byte[] RenderTeamsCameraJpeg(bool isCameraOn, bool isPressed = false)
    {
        string cacheKey = $"teams_cam_{isCameraOn}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isCameraOn
                ? (isPressed ? new SKColor(0x16, 0x4E, 0x2E) : new SKColor(0x0E, 0x33, 0x1E))
                : (isPressed ? new SKColor(0x2B, 0x24, 0x5C) : new SKColor(0x19, 0x18, 0x36));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 21);
            using var iconPaint = new SKPaint
            {
                Color = isCameraOn ? new SKColor(0x22, 0xC5, 0x5E) : new SKColor(0x9E, 0xA2, 0xEC),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("📷", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = new SKColor(0xC7, 0xD2, 0xFE),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("MS TEAMS", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            string badgeText = isCameraOn ? "● WIDEO ON" : "● WIDEO WYŁ";
            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = isCameraOn ? new SKColor(0x22, 0xC5, 0x5E) : new SKColor(0x9E, 0xA2, 0xEC),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(badgeText, ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isCameraOn || isPressed ? 2.5f : 1.5f,
                Color = isCameraOn
                    ? new SKColor(0x22, 0xC5, 0x5E)
                    : (isPressed ? new SKColor(0x9E, 0xA2, 0xEC) : new SKColor(0x62, 0x64, 0xA7)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD (72x72 JPEG) dla podnoszenia ręki w Microsoft Teams (Etap 5.6).
    /// </summary>
    public static byte[] RenderTeamsRaiseHandJpeg(bool isHandRaised, bool isPressed = false)
    {
        string cacheKey = $"teams_hand_{isHandRaised}_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isHandRaised
                ? (isPressed ? new SKColor(0x4D, 0x33, 0x05) : new SKColor(0x33, 0x22, 0x03))
                : (isPressed ? new SKColor(0x2B, 0x24, 0x5C) : new SKColor(0x19, 0x18, 0x36));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 21);
            using var iconPaint = new SKPaint
            {
                Color = isHandRaised ? new SKColor(0xFB, 0xBF, 0x24) : new SKColor(0x9E, 0xA2, 0xEC),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("✋", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = new SKColor(0xC7, 0xD2, 0xFE),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("MS TEAMS", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            string badgeText = isHandRaised ? "● W GÓRZE" : "● RĘKA W DÓŁ";
            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = isHandRaised ? new SKColor(0xFB, 0xBF, 0x24) : new SKColor(0x9E, 0xA2, 0xEC),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(badgeText, ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isHandRaised || isPressed ? 2.5f : 1.5f,
                Color = isHandRaised
                    ? new SKColor(0xF5, 0x9E, 0x0B)
                    : (isPressed ? new SKColor(0x9E, 0xA2, 0xEC) : new SKColor(0x62, 0x64, 0xA7)),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje kafelek LCD (72x72 JPEG) dla opuszczania spotkania w Microsoft Teams (Etap 5.6).
    /// </summary>
    public static byte[] RenderTeamsLeaveMeetingJpeg(bool isPressed = false)
    {
        string cacheKey = $"teams_leave_{isPressed}";
        return s_cache.GetOrAdd(cacheKey, _ =>
        {
            using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x0E, 0x10, 0x18));

            var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x57, 0x16, 0x16) : new SKColor(0x38, 0x0F, 0x0F);

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 21);
            using var iconPaint = new SKPaint
            {
                Color = new SKColor(0xEF, 0x44, 0x44),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("📞", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = new SKColor(0xFE, 0xCA, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("MS TEAMS", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            string badgeText = isPressed ? "● OPUSZCZANIE" : "● OPUŚĆ";
            using var fontBadge = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 8f);
            using var badgePaint = new SKPaint
            {
                Color = new SKColor(0xEF, 0x44, 0x44),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(badgeText, ButtonWidth / 2f, 63, SKTextAlign.Center, fontBadge, badgePaint);

            canvas.Restore();

            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = isPressed ? 2.5f : 1.5f,
                Color = isPressed ? new SKColor(0xF8, 0x71, 0x71) : new SKColor(0xDC, 0x26, 0x26),
                IsAntialias = true
            };
            canvas.DrawRoundRect(buttonRect, borderPaint);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 100);
            return data.ToArray();
        });
    }

    /// <summary>
    /// Renderuje tekst z dedykowanym kolorem tła i czcionki do 72x72 JPEG.
    /// </summary>
    public static byte[] RenderTextJpeg(string text, SKColor bg, SKColor fg, bool isPressed = false)
    {
        return CreateCustomKeyJpeg(
            DeckActionType.None,
            1,
            text,
            "",
            $"#{bg.Red:X2}{bg.Green:X2}{bg.Blue:X2}",
            $"#{fg.Red:X2}{fg.Green:X2}{fg.Blue:X2}",
            null,
            "Segoe UI",
            0,
            isPressed);
    }
    #endregion
}
