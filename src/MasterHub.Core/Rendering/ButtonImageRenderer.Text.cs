using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MasterHub.Core.Actions;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

public static partial class ButtonImageRenderer
{

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

    public static List<string> WrapTextLines(string text, SKFont font, float maxWidth)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var rawLines = text.Replace("\r\n", "\n").Split('\n');
        foreach (var rawLine in rawLines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;

            string currentLine = "";
            foreach (var word in words)
            {
                string testLine = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
                if (font.MeasureText(testLine) <= maxWidth)
                {
                    currentLine = testLine;
                }
                else
                {
                    if (!string.IsNullOrEmpty(currentLine))
                    {
                        result.Add(currentLine);
                        currentLine = "";
                    }

                    if (font.MeasureText(word) > maxWidth)
                    {
                        string wordChunk = "";
                        foreach (char ch in word)
                        {
                            string testChunk = wordChunk + ch;
                            if (font.MeasureText(testChunk) <= maxWidth)
                            {
                                wordChunk = testChunk;
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(wordChunk))
                                {
                                    result.Add(wordChunk);
                                }
                                wordChunk = ch.ToString();
                            }
                        }
                        currentLine = wordChunk;
                    }
                    else
                    {
                        currentLine = word;
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentLine))
            {
                result.Add(currentLine);
            }
        }

        return result;
    }

    private static SKTypeface ResolveTypeface(string text, string? requestedFont, bool isBold = true, bool isItalic = false)
    {
        if (IsEmojiOrSymbol(text))
        {
            if (s_emojiTypeface != null && s_emojiTypeface != SKTypeface.Default) return s_emojiTypeface;
            if (s_symbolTypeface != null && s_symbolTypeface != SKTypeface.Default) return s_symbolTypeface;
        }

        string chosenFont = !string.IsNullOrWhiteSpace(requestedFont) ? requestedFont : "Segoe UI";
        var weight = isBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = isItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        using var style = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);
        var primary = GetOrCreateTypeface(chosenFont, style);
        return WithGlyphFallback(primary, text, style);
    }

    private static readonly ConcurrentDictionary<(int CodePoint, int Weight, int Slant), SKTypeface?> s_fallbackCache = new();

    /// <summary>
    /// The chosen font when it has every character of <paramref name="text"/>; otherwise a system font that has the first missing one
    /// (Japanese, Chinese, Korean, Arabic …) instead of empty boxes. Such a font also contains Latin letters, so the whole line uses it.
    /// The answer for a character is remembered, so the font manager is asked only once per character.
    /// </summary>
    internal static SKTypeface WithGlyphFallback(SKTypeface primary, string? text, SKFontStyle style)
    {
        if (string.IsNullOrEmpty(text)) return primary;

        int missing = FirstMissingCodePoint(primary, text);
        if (missing < 0) return primary;

        var key = (missing, (int)style.Weight, (int)style.Slant);
        var fallback = s_fallbackCache.GetOrAdd(key, k =>
        {
            try { return SKFontManager.Default.MatchCharacter(null, style, null, k.CodePoint); }
            catch (Exception) { return null; }   // no font manager answer: keep the primary font
        });
        return fallback ?? primary;
    }

    private static int FirstMissingCodePoint(SKTypeface typeface, string text)
    {
        foreach (var rune in text.EnumerateRunes())   // a broken surrogate pair becomes U+FFFD instead of throwing
        {
            if (Rune.IsControl(rune) || Rune.IsWhiteSpace(rune)) continue;
            if (!HasGlyph(typeface, rune.Value)) return rune.Value;
        }
        return -1;
    }

    /// <summary>Whether <paramref name="typeface"/> has a glyph for the code point (glyph 0 is the "missing" box).</summary>
    internal static bool HasGlyph(SKTypeface typeface, int codePoint)
    {
        using var font = new SKFont(typeface);
        return font.GetGlyph(codePoint) != 0;
    }

    private static byte[] CreateActionJpeg(string icon, string subText, bool isPressed)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        // Czysto czarna ramka bazowa – zapobiega jakimkolwiek wyciekom koloru poza obramówkę
        canvas.Clear(new SKColor(0, 0, 0));

        using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        SKColor bgColor = isPressed ? new SKColor(0x3B, 0x1A, 0x6E) : new SKColor(0x0E, 0x10, 0x18);
        using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true, IsDither = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        if (!string.IsNullOrEmpty(icon))
        {
            using var fontBig = new SKFont(s_segoeBold, 22);

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
            using var fontSmall = new SKFont(s_segoeBold, 9.5f);

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
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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
}
