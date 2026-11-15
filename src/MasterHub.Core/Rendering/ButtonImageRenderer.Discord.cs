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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isMuted
                ? (isPressed ? new SKColor(0x6E, 0x14, 0x1C) : new SKColor(0x45, 0x0A, 0x10))
                : (isPressed ? new SKColor(0x31, 0x38, 0x8C) : new SKColor(0x18, 0x1C, 0x42));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 21);
            using var iconPaint = new SKPaint
            {
                Color = isMuted ? new SKColor(0xED, 0x42, 0x45) : new SKColor(0x58, 0x65, 0xF2),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(isMuted ? "🔇" : "🎙️", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isMuted ? new SKColor(0xFE, 0xCA, 0xCA) : new SKColor(0xE0, 0xE7, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("DISCORD", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isDeafened
                ? (isPressed ? new SKColor(0x6E, 0x14, 0x1C) : new SKColor(0x45, 0x0A, 0x10))
                : (isPressed ? new SKColor(0x31, 0x38, 0x8C) : new SKColor(0x18, 0x1C, 0x42));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 21);
            using var iconPaint = new SKPaint
            {
                Color = isDeafened ? new SKColor(0xED, 0x42, 0x45) : new SKColor(0x58, 0x65, 0xF2),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(isDeafened ? "🔇" : "🎧", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isDeafened ? new SKColor(0xFE, 0xCA, 0xCA) : new SKColor(0xE0, 0xE7, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("DISCORD", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isCameraEnabled
                ? (isPressed ? new SKColor(0x16, 0x65, 0x34) : new SKColor(0x0A, 0x3F, 0x22))
                : (isPressed ? new SKColor(0x31, 0x38, 0x8C) : new SKColor(0x18, 0x1C, 0x42));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 21);
            using var iconPaint = new SKPaint
            {
                Color = isCameraEnabled ? new SKColor(0x57, 0xF2, 0x87) : new SKColor(0x58, 0x65, 0xF2),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("📷", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isCameraEnabled ? new SKColor(0xDC, 0xFC, 0xE7) : new SKColor(0xE0, 0xE7, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("DISCORD", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isScreenSharing
                ? (isPressed ? new SKColor(0x6E, 0x14, 0x1C) : new SKColor(0x45, 0x0A, 0x10))
                : (isPressed ? new SKColor(0x31, 0x38, 0x8C) : new SKColor(0x18, 0x1C, 0x42));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 21);
            using var iconPaint = new SKPaint
            {
                Color = isScreenSharing ? new SKColor(0xED, 0x42, 0x45) : new SKColor(0x58, 0x65, 0xF2),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🖥️", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isScreenSharing ? new SKColor(0xFE, 0xCA, 0xCA) : new SKColor(0xE0, 0xE7, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("DISCORD", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
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

            using var fontIcon = new SKFont(s_segoeBold, 21);
            using var iconPaint = new SKPaint
            {
                Color = transmitting ? new SKColor(0x57, 0xF2, 0x87) : new SKColor(0x58, 0x65, 0xF2),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(transmitting ? "🗣️" : "🎙️", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = transmitting ? new SKColor(0xDC, 0xFC, 0xE7) : new SKColor(0xE0, 0xE7, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("DISCORD", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isConnectedToChannel
                ? (isPressed ? new SKColor(0x16, 0x65, 0x34) : new SKColor(0x0A, 0x3F, 0x22))
                : (isPressed ? new SKColor(0x31, 0x38, 0x8C) : new SKColor(0x18, 0x1C, 0x42));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 21);
            using var iconPaint = new SKPaint
            {
                Color = isConnectedToChannel ? new SKColor(0x57, 0xF2, 0x87) : new SKColor(0x58, 0x65, 0xF2),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🔊", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isConnectedToChannel ? new SKColor(0xDC, 0xFC, 0xE7) : new SKColor(0xE0, 0xE7, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("DISCORD", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            string badgeText = isConnectedToChannel ? "● POŁĄCZONO" : (safeName.Length > 8 ? safeName.Substring(0, 8) : safeName);
            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
            return data.ToArray();
        });
    }
}
