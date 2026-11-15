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
        const int Scale = 2;
        int renderW = ButtonWidth * Scale;
        int renderH = ButtonHeight * Scale;

        using var surface = SKSurface.Create(new SKImageInfo(renderW, renderH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        // Overlay mode (the whole-deck wallpaper): only what the key adds – its text and icon – on a transparent picture, no background,
        // rounded corners or border, so it can be laid over the wallpaper of that spot.
        bool overlay = s_overlayMode;

        // 1. Obrazek klawisza (jeśli jest) wczytywany najpierw: od niego zależy kształt klawisza (pełny ekran LCD albo karta z ramką)
        bool isStateActive = (isActiveState || isPressed);
        bool showsActivePicture = isStateActive && !string.IsNullOrWhiteSpace(activeImagePath) && File.Exists(activeImagePath);
        string? effectiveImage = showsActivePicture ? activeImagePath : imagePath;

        if (string.IsNullOrWhiteSpace(effectiveImage) && (action == DeckActionType.OpenApplication || action == DeckActionType.OpenFile))
        {
            if (!string.IsNullOrWhiteSpace(customSubtitle) && File.Exists(customSubtitle) &&
                (customSubtitle.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || customSubtitle.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)))
            {
                effectiveImage = customSubtitle;
            }
        }

        string effectiveFit = showsActivePicture ? (activeImageFitMode ?? "Cover") : (imageFitMode ?? "Cover");
        int effectiveScale = showsActivePicture ? activeImageScale : imageScale;
        int effectiveOffX = showsActivePicture ? activeImageOffsetX : imageOffsetX;
        int effectiveOffY = showsActivePicture ? activeImageOffsetY : imageOffsetY;

        bool isMotionPicture = effectiveImage != null && MediaValidationService.IsAnimatedOrVideo(effectiveImage);
        using SKBitmap? picture = LoadKeyPicture(effectiveImage, isMotionPicture, renderW, renderH, effectiveFit, effectiveScale, effectiveOffX, effectiveOffY);
        bool hasValidImage = picture != null;

        // 2. Kształt: obrazek wypełnia cały ekran LCD klawisza (bez czarnego marginesu, zaokrągleń i ramki); kolor + tekst to karta z ramką
        using var frame = KeyFrame.For(hasValidImage, renderW, renderH, Scale);

        canvas.Clear(overlay ? SKColors.Transparent : SKColors.Black);
        canvas.Save();
        if (!overlay) frame.Clip(canvas);

        // 3. Tło (kolor) – rysowane ZAWSZE jako baza (również pod przezroczystymi grafikami PNG)
        SKColor bg = isPressed ? new SKColor(0x3B, 0x1A, 0x6E) : (isActiveState ? new SKColor(0x1F, 0x16, 0x33) : new SKColor(0x00, 0x00, 0x00));
        string? effectiveBgHex = (isActiveState && !string.IsNullOrWhiteSpace(activeBgColorHex)) ? activeBgColorHex : bgColorHex;
        if (!string.IsNullOrWhiteSpace(effectiveBgHex) && SKColor.TryParse(effectiveBgHex, out var parsedBg))
        {
            bg = isPressed
                ? new SKColor((byte)Math.Min(255, parsedBg.Red + 40), (byte)Math.Min(255, parsedBg.Green + 40), (byte)Math.Min(255, parsedBg.Blue + 40))
                : parsedBg;
        }
        if (!overlay)
        {
            using var bgPaint = new SKPaint { Color = bg, Style = SKPaintStyle.Fill, IsAntialias = true, IsDither = true };
            canvas.DrawRoundRect(frame.Shape, bgPaint);
        }

        // 4. Obrazek na całej powierzchni klawisza
        if (picture != null)
        {
            if (isMotionPicture)
            {
                // Pierwsza klatka GIF/wideo jest już wykadrowana do kwadratu klawisza – tylko ją rozciągamy na całość
                using var imgPaint = new SKPaint { IsAntialias = true, IsDither = true };
                canvas.DrawBitmap(picture, frame.ContentRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), imgPaint);
            }
            else
            {
                DrawTransformedBitmap(canvas, picture, frame.ContentRect, effectiveFit, effectiveScale, effectiveOffX, effectiveOffY);
            }
        }

        if (hasValidImage && isPressed)
        {
            using var tintPaint = new SKPaint { Color = new SKColor(0x00, 0xFF, 0xCC, 0x55) };
            canvas.DrawRoundRect(frame.Shape, tintPaint);
        }

        // 4. Treść tekstowa z podziałem na wiersze tytułu i podtytułu
        (string actionIcon, string actionSub) = GetActionDetails(action, buttonNumber, isActiveState);
        string? effectiveCustomTitle = (isActiveState && activeCustomTitle != null) ? activeCustomTitle : customTitle;
        string? effectiveCustomSubtitle = (isActiveState && activeCustomSubtitle != null) ? activeCustomSubtitle : customSubtitle;

        // Jeśli pole tekstowe zostało ustawione (nawet na pusty ciąg ""), szanujemy to.
        // Domyślny fallback do actionIcon/actionSub następuje tylko wtedy, gdy parametr jest null (brak konfiguracji).
        string finalTitle = effectiveCustomTitle != null ? effectiveCustomTitle : actionIcon;
        string finalSub = effectiveCustomSubtitle != null ? effectiveCustomSubtitle : actionSub;

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

        float maxAllowedWidth = ButtonWidth - 10;
        var titleLines = new List<string>();
        var subLines = new List<string>();

        // Typografia tytułu
        string chosenTitleFont = !string.IsNullOrWhiteSpace(titleFontFamily) ? titleFontFamily : (!string.IsNullOrWhiteSpace(fontFamily) ? fontFamily : "Segoe UI");
        var titleWeight = titleIsBold ? SKFontStyleWeight.Bold : (isBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal);
        var titleSlant = (titleIsItalic || isItalic) ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        using var titleStyle = new SKFontStyle(titleWeight, SKFontStyleWidth.Normal, titleSlant);
        SKTypeface titleTypeface = GetOrCreateTypeface(chosenTitleFont, titleStyle);

        float effectiveTitleSize;
        if (titleFontSize > 0)
        {
            effectiveTitleSize = Math.Clamp(titleFontSize, 6, 96);
        }
        else if (fontSize > 0)
        {
            effectiveTitleSize = Math.Clamp(fontSize, 6, 96);
        }
        else
        {
            effectiveTitleSize = (!string.IsNullOrWhiteSpace(finalTitle) && IsEmojiOrSymbol(finalTitle) && string.IsNullOrWhiteSpace(finalSub))
                ? 26f
                : (string.IsNullOrWhiteSpace(finalSub) ? 13.5f : 11.5f);
        }

        if (titleIsTextWrappingEnabled && isTextWrappingEnabled)
        {
            using var measureFont = new SKFont(WithGlyphFallback(titleTypeface, finalTitle, titleStyle), effectiveTitleSize);
            if (!string.IsNullOrWhiteSpace(finalTitle))
            {
                titleLines.AddRange(WrapTextLines(finalTitle, measureFont, maxAllowedWidth));
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(finalTitle))
            {
                var split = finalTitle.Replace("\r\n", "\n").Split('\n');
                foreach (var s in split)
                {
                    var t = s.Trim();
                    if (!string.IsNullOrEmpty(t)) titleLines.Add(t);
                }
            }
        }

        // Typografia podtytułu
        string chosenSubFont = !string.IsNullOrWhiteSpace(subtitleFontFamily) ? subtitleFontFamily : (!string.IsNullOrWhiteSpace(fontFamily) ? fontFamily : "Segoe UI");
        var subWeight = subtitleIsBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var subSlant = subtitleIsItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        using var subStyle = new SKFontStyle(subWeight, SKFontStyleWidth.Normal, subSlant);
        SKTypeface subTypeface = GetOrCreateTypeface(chosenSubFont, subStyle);

        float effectiveSubSize;
        if (subtitleFontSize > 0)
        {
            effectiveSubSize = Math.Clamp(subtitleFontSize, 6, 96);
        }
        else
        {
            effectiveSubSize = 9.5f;
        }

        if (subtitleIsTextWrappingEnabled && isTextWrappingEnabled)
        {
            using var measureFont = new SKFont(WithGlyphFallback(subTypeface, finalSub, subStyle), effectiveSubSize);
            if (!string.IsNullOrWhiteSpace(finalSub))
            {
                subLines.AddRange(WrapTextLines(finalSub, measureFont, maxAllowedWidth));
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(finalSub))
            {
                var split = finalSub.Replace("\r\n", "\n").Split('\n');
                foreach (var s in split)
                {
                    var t = s.Trim();
                    if (!string.IsNullOrEmpty(t)) subLines.Add(t);
                }
            }
        }

        // Dopasowanie rozmiarów tekstu, jeśli nie mieści się w kafelku (tylko w trybie Auto)
        if (titleLines.Count > 0 && titleFontSize <= 0 && fontSize <= 0)
        {
            var primaryTypeface = ResolveTypeface(titleLines[0], chosenTitleFont, titleIsBold, titleIsItalic);
            using var testFont = new SKFont(primaryTypeface, effectiveTitleSize);
            while (effectiveTitleSize > 6.0f)
            {
                testFont.Size = effectiveTitleSize;
                float maxW = 0;
                foreach (var line in titleLines)
                {
                    float w = testFont.MeasureText(line);
                    if (w > maxW) maxW = w;
                }
                if (maxW <= maxAllowedWidth) break;
                effectiveTitleSize -= 0.5f;
            }
        }

        if (subLines.Count > 0 && subtitleFontSize <= 0)
        {
            var primarySubTf = ResolveTypeface(subLines[0], chosenSubFont, subtitleIsBold, subtitleIsItalic);
            using var testFont = new SKFont(primarySubTf, effectiveSubSize);
            while (effectiveSubSize > 6.0f)
            {
                testFont.Size = effectiveSubSize;
                float maxW = 0;
                foreach (var line in subLines)
                {
                    float w = testFont.MeasureText(line);
                    if (w > maxW) maxW = w;
                }
                if (maxW <= maxAllowedWidth) break;
                effectiveSubSize -= 0.5f;
            }
        }

        using var textPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            IsDither = true
        };

        // Rysowanie tytułu
        if (titleLines.Count > 0)
        {
            textPaint.Color = titleCol;
            float titleLineSpacing = effectiveTitleSize * 1.25f;
            float titleBlockHeight = titleLines.Count * titleLineSpacing;
            float titleStartY;

            if (titleVerticalPosition.Equals("Top", StringComparison.OrdinalIgnoreCase))
            {
                titleStartY = 4f + effectiveTitleSize * 0.88f + titleOffsetY;
            }
            else if (titleVerticalPosition.Equals("Bottom", StringComparison.OrdinalIgnoreCase))
            {
                titleStartY = ButtonHeight - 4f - ((titleLines.Count - 1) * titleLineSpacing) - (effectiveTitleSize * 0.12f) + titleOffsetY;
            }
            else // Center
            {
                if (subLines.Count > 0 && subtitleVerticalPosition.Equals("Bottom", StringComparison.OrdinalIgnoreCase))
                {
                    float availableH = ButtonHeight - 4f - (subLines.Count * (effectiveSubSize * 1.25f));
                    titleStartY = (availableH - titleBlockHeight) / 2f + effectiveTitleSize * 0.88f + titleOffsetY;
                }
                else
                {
                    titleStartY = (ButtonHeight - titleBlockHeight) / 2f + effectiveTitleSize * 0.88f + titleOffsetY;
                }
            }

            SKTextAlign hAlign = titleAlignment switch
            {
                "Left" => SKTextAlign.Left,
                "Right" => SKTextAlign.Right,
                _ => SKTextAlign.Center
            };

            float baseDrawX = titleAlignment switch
            {
                "Left" => 6f,
                "Right" => ButtonWidth - 6f,
                _ => ButtonWidth / 2f
            };
            float drawX = (baseDrawX + titleOffsetX) * Scale;

            for (int i = 0; i < titleLines.Count; i++)
            {
                float lineY = (titleStartY + (i * titleLineSpacing)) * Scale;
                if (lineY > -effectiveTitleSize * Scale && lineY < (ButtonHeight + effectiveTitleSize) * Scale)
                {
                    bool isEmoji = IsEmojiOrSymbol(titleLines[i]);
                    var currentTf = ResolveTypeface(titleLines[i], chosenTitleFont, titleIsBold, titleIsItalic);
                    using var currentFont = new SKFont(currentTf, effectiveTitleSize * Scale);

                    canvas.DrawText(titleLines[i], drawX, lineY, hAlign, currentFont, textPaint);

                    if ((titleIsStrikeThrough || isStrikeThrough) && !isEmoji)
                    {
                        float textWidth = currentFont.MeasureText(titleLines[i]);
                        float strikeStartX = titleAlignment switch
                        {
                            "Left" => drawX,
                            "Right" => drawX - textWidth,
                            _ => drawX - (textWidth / 2f)
                        };
                        float strikeY = lineY - (effectiveTitleSize * Scale * 0.32f);
                        using var strikePaint = new SKPaint
                        {
                            Color = titleCol,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = Math.Clamp(effectiveTitleSize * Scale * 0.09f, 1f * Scale, 2f * Scale),
                            IsAntialias = true
                        };
                        canvas.DrawLine(strikeStartX, strikeY, strikeStartX + textWidth, strikeY, strikePaint);
                    }
                }
            }
        }

        // Rysowanie podtytułu
        if (subLines.Count > 0)
        {
            textPaint.Color = subCol;
            float subLineSpacing = effectiveSubSize * 1.25f;
            float subBlockHeight = subLines.Count * subLineSpacing;
            float subStartY;

            if (subtitleVerticalPosition.Equals("Top", StringComparison.OrdinalIgnoreCase))
            {
                subStartY = 4f + effectiveSubSize * 0.88f + subtitleOffsetY;
            }
            else if (subtitleVerticalPosition.Equals("Center", StringComparison.OrdinalIgnoreCase))
            {
                subStartY = (ButtonHeight - subBlockHeight) / 2f + effectiveSubSize * 0.88f + subtitleOffsetY;
            }
            else // Bottom
            {
                subStartY = ButtonHeight - 4f - ((subLines.Count - 1) * subLineSpacing) - (effectiveSubSize * 0.12f) + subtitleOffsetY;
            }

            SKTextAlign hAlign = subtitleAlignment switch
            {
                "Left" => SKTextAlign.Left,
                "Right" => SKTextAlign.Right,
                _ => SKTextAlign.Center
            };

            float baseSubDrawX = subtitleAlignment switch
            {
                "Left" => 6f,
                "Right" => ButtonWidth - 6f,
                _ => ButtonWidth / 2f
            };
            float drawX = (baseSubDrawX + subtitleOffsetX) * Scale;

            for (int i = 0; i < subLines.Count; i++)
            {
                float lineY = (subStartY + (i * subLineSpacing)) * Scale;
                if (lineY > -effectiveSubSize * Scale && lineY < (ButtonHeight + effectiveSubSize) * Scale)
                {
                    bool isEmoji = IsEmojiOrSymbol(subLines[i]);
                    var currentTf = ResolveTypeface(subLines[i], chosenSubFont, subtitleIsBold, subtitleIsItalic);
                    using var currentFont = new SKFont(currentTf, effectiveSubSize * Scale);

                    canvas.DrawText(subLines[i], drawX, lineY, hAlign, currentFont, textPaint);

                    if (subtitleIsStrikeThrough && !isEmoji)
                    {
                        float textWidth = currentFont.MeasureText(subLines[i]);
                        float strikeStartX = subtitleAlignment switch
                        {
                            "Left" => drawX,
                            "Right" => drawX - textWidth,
                            _ => drawX - (textWidth / 2f)
                        };
                        float strikeY = lineY - (effectiveSubSize * Scale * 0.32f);
                        using var strikePaint = new SKPaint
                        {
                            Color = subCol,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = Math.Clamp(effectiveSubSize * Scale * 0.09f, 1f * Scale, 2f * Scale),
                            IsAntialias = true
                        };
                        canvas.DrawLine(strikeStartX, strikeY, strikeStartX + textWidth, strikeY, strikePaint);
                    }
                }
            }
        }

        canvas.Restore();

        // 6. Ramka na krawędzi – tylko karta (kolor + tekst) albo włączony stan klawisza z obrazkiem bez własnej drugiej grafiki
        if (!overlay && frame.ShouldDrawOutline(isPressed, isActiveState, showsActivePicture))
        {
            float strokeWidth = (isPressed ? 3f : (isActiveState ? 2f : 1.5f)) * Scale;
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokeWidth,
                Color = isPressed ? new SKColor(0x00, 0xFF, 0xCC) : (isActiveState ? new SKColor(0x00, 0xFF, 0xCC) : new SKColor(0x22, 0x28, 0x3D)),
                IsAntialias = true
            };
            using var outline = frame.OutlineFor(strokeWidth);
            canvas.DrawRoundRect(outline, borderPaint);
        }

        // 7. Próbkowanie wstecz (Mitchell-Netravali) do natywnego 72x72
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
        if (overlay)
        {
            using var png = finalImage.Encode(SKEncodedImageFormat.Png, 100);   // keeps the transparency
            return png.ToArray();
        }

        // Jakość 100: absolutny brak artefaktów DCT/Gibbsa ("mosquito noise") oraz subsamplingu 4:2:0 na ekranie LCD
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(finalImage, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Loads the key's picture: the first frame of a GIF/video (already cut to the key), an SVG, the icon of a program or shortcut,
    /// or a plain picture. Returns null when the file is missing or cannot be read, so the key is drawn without a picture.
    /// </summary>
    private static SKBitmap? LoadKeyPicture(string? path, bool isMotionPicture, int renderW, int renderH, string fit, int scale, int offsetX, int offsetY)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            if (isMotionPicture) return MediaFrameExtractor.GetFirstFrameBitmap(path, fit, scale, offsetX, offsetY);
            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return SvgRasterizer.RasterizeSvg(path, renderW, renderH);
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                return WindowsAppIconExtractor.ExtractHighResAppIcon(path, 128);
            return SKBitmap.Decode(path);
        }
        catch (Exception ex)
        {
            // A broken file must never stop the key from being drawn; the reason goes to the log page instead of being lost
            MasterHub.Core.Diagnostics.AppLog.Write($"Key picture '{Path.GetFileName(path)}' could not be read: {ex.GetType().Name}");
            return null;
        }
    }

    [ThreadStatic] private static bool s_overlayMode;

    /// <summary>
    /// Runs <paramref name="render"/> (a call of <see cref="RenderCustomKeyJpeg"/>) in overlay mode: the result is a transparent PNG with only the
    /// key's text and icon, to be laid over the wallpaper. The mode is per thread and ends when the call returns.
    /// </summary>
    public static byte[] RenderOverlayPng(Func<byte[]> render)
    {
        bool before = s_overlayMode;
        s_overlayMode = true;
        try { return render(); }
        finally { s_overlayMode = before; }
    }

    public static byte[] CreateButtonJpeg(int buttonNumber, string title = "", bool isPressed = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        canvas.Clear(new SKColor(0, 0, 0));

        using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        SKColor bgColor = isPressed ? new SKColor(0x3B, 0x1A, 0x6E) : new SKColor(0x18, 0x18, 0x22);
        using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true, IsDither = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        using var fontBig = new SKFont(s_arialBold, 26);
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
        using var fontSmall = new SKFont(s_arialBold, 10);

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
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
        return data.ToArray();
    }
}
