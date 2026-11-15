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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isEmoteOnly
                ? (isPressed ? new SKColor(0x4C, 0x1D, 0x95) : new SKColor(0x2E, 0x10, 0x65))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint { Color = isEmoteOnly ? new SKColor(0xD8, 0xB4, 0xFE) : new SKColor(0x94, 0xA3, 0xB8), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("😀", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10f);
            using var labelPaint = new SKPaint { Color = isEmoteOnly ? new SKColor(0xF3, 0xE8, 0xFF) : new SKColor(0xE2, 0xE8, 0xF0), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("EMOTE", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8.5f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isSubOnly
                ? (isPressed ? new SKColor(0x58, 0x1C, 0x87) : new SKColor(0x3B, 0x07, 0x64))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint { Color = isSubOnly ? new SKColor(0xFA, 0xCC, 0x15) : new SKColor(0x94, 0xA3, 0xB8), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("⭐", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10f);
            using var labelPaint = new SKPaint { Color = isSubOnly ? new SKColor(0xFE, 0xF0, 0x8A) : new SKColor(0xE2, 0xE8, 0xF0), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("SUB-ONLY", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8.5f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isSlowMode
                ? (isPressed ? new SKColor(0x1E, 0x1B, 0x4B) : new SKColor(0x31, 0x2E, 0x81))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint { Color = isSlowMode ? new SKColor(0xA5, 0xB4, 0xFC) : new SKColor(0x94, 0xA3, 0xB8), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("🐢", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10f);
            using var labelPaint = new SKPaint { Color = isSlowMode ? new SKColor(0xE0, 0xE7, 0xFF) : new SKColor(0xE2, 0xE8, 0xF0), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("SLOW", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8.5f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x4C, 0x1D, 0x95) : new SKColor(0x2E, 0x10, 0x65);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xE9, 0xD5, 0xFF), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("🧹", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xF3, 0xE8, 0xFF), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("WYCZYŚĆ", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8.5f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x58, 0x1C, 0x87) : new SKColor(0x2E, 0x10, 0x65);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xC0, 0x84, 0xFC), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("💬", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("TWITCH", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x58, 0x1C, 0x87) : new SKColor(0x2E, 0x10, 0x65);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xF0, 0xAB, 0xFC), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("🎬", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("UTWÓRZ", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8.5f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x58, 0x1C, 0x87) : new SKColor(0x2E, 0x10, 0x65);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xF4, 0x72, 0xB6), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("📍", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("ZNACZNIK", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x58, 0x1C, 0x87) : new SKColor(0x2E, 0x10, 0x65);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xC0, 0x84, 0xFC), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("🚀", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("RAID", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8.5f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x7F, 0x1D, 0x1D) : new SKColor(0x45, 0x0A, 0x0A);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xEF, 0x44, 0x44), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("🛑", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("ANULUJ", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8.5f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x78, 0x35, 0x0F) : new SKColor(0x45, 0x1A, 0x03);
            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint { Color = new SKColor(0xFB, 0xBF, 0x24), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("📺", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint { Color = new SKColor(0xFA, 0xFA, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawText("REKLAMA", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8.5f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
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
            using var fontTop = new SKFont(s_segoeBold, 8.5f);
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
            using var fontMain = new SKFont(s_segoeBold, mainFontSize);
            using var mainPaint = new SKPaint
            {
                Color = isLive ? new SKColor(0xFF, 0xFF, 0xFF) : new SKColor(0x64, 0x74, 0x8B),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(mainText, ButtonWidth / 2f, 42, SKTextAlign.Center, fontMain, mainPaint);

            // Dolny podpis (nazwa kanału lub "WIDZÓW")
            using var fontBottom = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isActiveScene
                ? (isPressed ? new SKColor(0x0D, 0x5C, 0x52) : new SKColor(0x07, 0x3B, 0x36))
                : (isPressed ? new SKColor(0x13, 0x2F, 0x33) : new SKColor(0x0C, 0x19, 0x1E));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 21);
            using var iconPaint = new SKPaint
            {
                Color = isActiveScene ? new SKColor(0x00, 0xF5, 0xD4) : new SKColor(0x5E, 0xEA, 0xD4),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🎥", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            string displayLabel = label.Length > 8 ? label.Substring(0, 7) + "…" : label.ToUpperInvariant();
            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isActiveScene ? new SKColor(0xCC, 0xFB, 0xF1) : new SKColor(0xE2, 0xE8, 0xF0),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(displayLabel, ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
            return data.ToArray();
        });
    }
}
