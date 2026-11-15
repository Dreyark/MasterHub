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
        int activeImageOffsetY = 0,
        bool isTextWrappingEnabled = true,
        string? titleFontFamily = null,
        int titleFontSize = 0,
        bool titleIsBold = true,
        bool titleIsItalic = false,
        bool titleIsStrikeThrough = false,
        bool titleIsTextWrappingEnabled = true,
        string titleAlignment = "Center",
        string titleVerticalPosition = "Center",
        int titleOffsetX = 0,
        int titleOffsetY = 0,
        string? subtitleFontFamily = null,
        int subtitleFontSize = 0,
        bool subtitleIsBold = false,
        bool subtitleIsItalic = false,
        bool subtitleIsStrikeThrough = false,
        bool subtitleIsTextWrappingEnabled = true,
        string subtitleAlignment = "Center",
        string subtitleVerticalPosition = "Bottom",
        int subtitleOffsetX = 0,
        int subtitleOffsetY = 0)
    {
        string key = $"cust:{(s_overlayMode ? "ov" : "")}:{action}:{buttonNumber}:{customTitle}:{customSubtitle}:{bgColorHex}:{textColorHex}:{subtitleColorHex}:{imagePath}@{FileStamp(imagePath)}:{activeImagePath}@{FileStamp(activeImagePath)}:{isActiveState}:{fontFamily}:{fontSize}:{isPressed}:{isBold}:{isItalic}:{isStrikeThrough}:{activeCustomTitle}:{activeCustomSubtitle}:{activeBgColorHex}:{activeTextColorHex}:{activeSubtitleColorHex}:{imageFitMode}:{imageScale}:{imageOffsetX}:{imageOffsetY}:{activeImageFitMode}:{activeImageScale}:{activeImageOffsetX}:{activeImageOffsetY}:{isTextWrappingEnabled}:{titleFontFamily}:{titleFontSize}:{titleIsBold}:{titleIsItalic}:{titleIsStrikeThrough}:{titleIsTextWrappingEnabled}:{titleAlignment}:{titleVerticalPosition}:{titleOffsetX}:{titleOffsetY}:{subtitleFontFamily}:{subtitleFontSize}:{subtitleIsBold}:{subtitleIsItalic}:{subtitleIsStrikeThrough}:{subtitleIsTextWrappingEnabled}:{subtitleAlignment}:{subtitleVerticalPosition}:{subtitleOffsetX}:{subtitleOffsetY}";
        return s_cache.GetOrAdd(key, _ =>
            CreateCustomKeyJpeg(action, buttonNumber, customTitle, customSubtitle, bgColorHex, textColorHex, imagePath, fontFamily, fontSize, isPressed, activeImagePath, isActiveState, subtitleColorHex, isBold, isItalic, isStrikeThrough, activeCustomTitle, activeCustomSubtitle, activeBgColorHex, activeTextColorHex, activeSubtitleColorHex, imageFitMode, imageScale, imageOffsetX, imageOffsetY, activeImageFitMode, activeImageScale, activeImageOffsetX, activeImageOffsetY, isTextWrappingEnabled, titleFontFamily, titleFontSize, titleIsBold, titleIsItalic, titleIsStrikeThrough, titleIsTextWrappingEnabled, titleAlignment, titleVerticalPosition, titleOffsetX, titleOffsetY, subtitleFontFamily, subtitleFontSize, subtitleIsBold, subtitleIsItalic, subtitleIsStrikeThrough, subtitleIsTextWrappingEnabled, subtitleAlignment, subtitleVerticalPosition, subtitleOffsetX, subtitleOffsetY));
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
        bool isOk = false,
        bool isTextWrappingEnabled = true,
        int offsetX = 0,
        int offsetY = 0)
    {
        // Optymalizacja: czysty obraz wtyczki bez tekstu i nakładek
        if (!isPressed && !isAlert && !isOk && string.IsNullOrWhiteSpace(title) && pluginImageJpeg != null && pluginImageJpeg.Length > 0)
        {
            return pluginImageJpeg;
        }

        const int Scale = 2;
        int renderW = ButtonWidth * Scale;
        int renderH = ButtonHeight * Scale;

        using var surface = SKSurface.Create(new SKImageInfo(renderW, renderH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        // The plugin's picture (or the manifest's fallback picture) fills the whole LCD like in the official app; without one the key is a card
        using SKBitmap? basePicture = DecodePluginPicture(pluginImageJpeg) ?? LoadKeyPicture(fallbackImagePath, false, renderW, renderH, "Stretch", 100, 0, 0);
        bool drawnBase = basePicture != null;
        using var frame = KeyFrame.For(drawnBase, renderW, renderH, Scale);

        canvas.Clear(SKColors.Black);
        canvas.Save();
        frame.Clip(canvas);

        if (basePicture != null)
        {
            using var p = new SKPaint { IsAntialias = true, IsDither = true };
            canvas.DrawBitmap(basePicture, frame.ContentRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), p);
        }
        else
        {
            using var bgPaint = new SKPaint { Color = new SKColor(0x18, 0x18, 0x1B), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(frame.Shape, bgPaint);
        }

        if (isPressed)
        {
            using var tintPaint = new SKPaint { Color = new SKColor(0x00, 0xFF, 0xCC, 0x40), Style = SKPaintStyle.Fill };
            canvas.DrawRoundRect(frame.Shape, tintPaint);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            string chosenFont = !string.IsNullOrWhiteSpace(fontFamily) ? fontFamily : "Segoe UI";
            var weight = isBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
            var slant = isItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
            using var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
            SKTypeface typeface = GetOrCreateTypeface(chosenFont, style);

            float maxAllowedWidth = ButtonWidth - 8;
            float effectiveSize = fontSize > 0 ? Math.Clamp(fontSize, 6, 96) : 11f;

            List<string> lines;
            if (isTextWrappingEnabled)
            {
                using var wrapFont = new SKFont(typeface, effectiveSize);
                lines = WrapTextLines(title, wrapFont, maxAllowedWidth);
            }
            else
            {
                lines = title.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            }

            if (lines.Count > 0)
            {
                if (fontSize <= 0)
                {
                    effectiveSize = lines.Count switch
                    {
                        1 => lines[0].Length <= 4 ? 16f : (lines[0].Length <= 8 ? 12.5f : 10.5f),
                        2 => 10.5f,
                        3 => 9f,
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
                        float totalHeight = lines.Count * (effectiveSize * 1.25f);
                        if (maxW <= maxAllowedWidth && totalHeight <= (ButtonHeight - 6)) break;
                        effectiveSize -= 0.5f;
                    }
                }

                using var font = new SKFont(typeface, effectiveSize * Scale);
                float lineSpacing = effectiveSize * 1.25f;
                float totalBlockHeight = lines.Count * lineSpacing;
                float startY = (ButtonHeight - totalBlockHeight) / 2f + effectiveSize * 0.88f + offsetY;

                SKColor fgCol = SKColors.White;
                if (!string.IsNullOrWhiteSpace(textColorHex) && SKColor.TryParse(textColorHex, out var parsedCol))
                {
                    fgCol = parsedCol;
                }

                if (lines.Count >= 3)
                {
                    using var textBgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 160), Style = SKPaintStyle.Fill, IsAntialias = true };
                    using var bgBox = new SKRoundRect(new SKRect(3 * Scale, Math.Max(2 * Scale, (startY - effectiveSize) * Scale), (ButtonWidth - 3) * Scale, Math.Min((ButtonHeight - 2) * Scale, (startY + (lines.Count - 1) * lineSpacing + 4) * Scale)), 4 * Scale, 4 * Scale);
                    canvas.DrawRoundRect(bgBox, textBgPaint);
                }

                using var strokePaint = new SKPaint
                {
                    Color = new SKColor(0, 0, 0, 220),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2.5f * Scale,
                    IsAntialias = true
                };
                using var fillPaint = new SKPaint
                {
                    Color = fgCol,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };

                float drawX = ((ButtonWidth / 2f) + offsetX) * Scale;
                for (int i = 0; i < lines.Count; i++)
                {
                    float lineY = (startY + (i * lineSpacing)) * Scale;
                    if (lineY > -effectiveSize * Scale && lineY < (ButtonHeight + effectiveSize) * Scale)
                    {
                        canvas.DrawText(lines[i], drawX, lineY, SKTextAlign.Center, font, strokePaint);
                        canvas.DrawText(lines[i], drawX, lineY, SKTextAlign.Center, font, fillPaint);
                    }
                }
            }
        }

        if (isAlert)
        {
            using var alertBgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 190), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(new SKRect((ButtonWidth - 24) * Scale, 2 * Scale, (ButtonWidth - 2) * Scale, 24 * Scale), 4 * Scale, 4 * Scale, alertBgPaint);
            using var alertFont = new SKFont(s_emojiTypeface, 13f * Scale);
            using var alertPaint = new SKPaint { Color = SKColors.Yellow, IsAntialias = true };
            canvas.DrawText("⚠️", (ButtonWidth - 13) * Scale, 18 * Scale, SKTextAlign.Center, alertFont, alertPaint);
        }
        else if (isOk)
        {
            using var okBgPaint = new SKPaint { Color = new SKColor(0, 0, 0, 190), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(new SKRect((ButtonWidth - 24) * Scale, 2 * Scale, (ButtonWidth - 2) * Scale, 24 * Scale), 4 * Scale, 4 * Scale, okBgPaint);
            using var okFont = new SKFont(s_emojiTypeface, 13f * Scale);
            using var okPaint = new SKPaint { Color = SKColors.LimeGreen, IsAntialias = true };
            canvas.DrawText("✅", (ButtonWidth - 13) * Scale, 18 * Scale, SKTextAlign.Center, okFont, okPaint);
        }

        canvas.Restore();

        // Próbkowanie wstecz (Mitchell-Netravali) do natywnego 72x72
        using var finalSurface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var finalCanvas = finalSurface.Canvas;
        using var hiResSnapshot = surface.Snapshot();
        using var downsamplePaint = new SKPaint
        {
            IsAntialias = true,
            IsDither = true
        };
        finalCanvas.DrawImage(hiResSnapshot, new SKRect(0, 0, ButtonWidth, ButtonHeight), new SKSamplingOptions(SKCubicResampler.Mitchell), downsamplePaint);

        using var finalImage = finalSurface.Snapshot();
        using var encoded = MasterHub.Core.Rendering.JpegEncoding.Encode(finalImage, 100);
        return encoded.ToArray();
    }

    /// <summary>
    /// The file's last write time, part of the render cache key: a picture replaced under the same path is drawn again instead of
    /// the old cached key. 0 for no file.
    /// </summary>
    internal static long FileStamp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return 0; }
    }

    private static SKBitmap? DecodePluginPicture(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try { return SKBitmap.Decode(bytes); }
        catch (Exception) { return null; }   // a plugin may send junk; the key then falls back to its manifest picture
    }
}
