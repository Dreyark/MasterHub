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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isMuted
                ? (isPressed ? new SKColor(0x45, 0x1A, 0x1A) : new SKColor(0x2A, 0x12, 0x12))
                : (isPressed ? new SKColor(0x2B, 0x24, 0x5C) : new SKColor(0x19, 0x18, 0x36));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 21);
            using var iconPaint = new SKPaint
            {
                Color = isMuted ? new SKColor(0xEF, 0x44, 0x44) : new SKColor(0x22, 0xC5, 0x5E),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            string iconStr = isMuted ? "🔇" : "🎙️";
            canvas.DrawText(iconStr, ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = new SKColor(0xC7, 0xD2, 0xFE),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("MS TEAMS", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            string badgeText = isMuted ? "● WYCISZ" : "● MIKRO ON";
            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isCameraOn
                ? (isPressed ? new SKColor(0x16, 0x4E, 0x2E) : new SKColor(0x0E, 0x33, 0x1E))
                : (isPressed ? new SKColor(0x2B, 0x24, 0x5C) : new SKColor(0x19, 0x18, 0x36));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 21);
            using var iconPaint = new SKPaint
            {
                Color = isCameraOn ? new SKColor(0x22, 0xC5, 0x5E) : new SKColor(0x9E, 0xA2, 0xEC),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("📷", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = new SKColor(0xC7, 0xD2, 0xFE),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("MS TEAMS", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            string badgeText = isCameraOn ? "● WIDEO ON" : "● WIDEO WYŁ";
            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isHandRaised
                ? (isPressed ? new SKColor(0x4D, 0x33, 0x05) : new SKColor(0x33, 0x22, 0x03))
                : (isPressed ? new SKColor(0x2B, 0x24, 0x5C) : new SKColor(0x19, 0x18, 0x36));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 21);
            using var iconPaint = new SKPaint
            {
                Color = isHandRaised ? new SKColor(0xFB, 0xBF, 0x24) : new SKColor(0x9E, 0xA2, 0xEC),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("✋", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = new SKColor(0xC7, 0xD2, 0xFE),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("MS TEAMS", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            string badgeText = isHandRaised ? "● W GÓRZE" : "● RĘKA W DÓŁ";
            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x57, 0x16, 0x16) : new SKColor(0x38, 0x0F, 0x0F);

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 21);
            using var iconPaint = new SKPaint
            {
                Color = new SKColor(0xEF, 0x44, 0x44),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("📞", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = new SKColor(0xFE, 0xCA, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("MS TEAMS", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            string badgeText = isPressed ? "● OPUSZCZANIE" : "● OPUŚĆ";
            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
            return data.ToArray();
        });
    }
}
