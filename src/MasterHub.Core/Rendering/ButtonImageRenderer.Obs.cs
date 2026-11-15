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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isStreaming
                ? (isPressed ? new SKColor(0x52, 0x18, 0x99) : new SKColor(0x3B, 0x12, 0x6E))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint
            {
                Color = isStreaming ? new SKColor(0x00, 0xFF, 0xCC) : new SKColor(0xCC, 0xD4, 0xEA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("📡", ButtonWidth / 2f, 36, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10.5f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isRecording
                ? (isPressed ? new SKColor(0x66, 0x0E, 0x15) : new SKColor(0x42, 0x09, 0x0E))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint
            {
                Color = isRecording ? new SKColor(0xFF, 0x22, 0x33) : new SKColor(0xAA, 0xB4, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🔴", ButtonWidth / 2f, 36, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10.5f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isVirtualCamActive
                ? (isPressed ? new SKColor(0x0A, 0x47, 0x44) : new SKColor(0x06, 0x2E, 0x2C))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint
            {
                Color = isVirtualCamActive ? new SKColor(0x00, 0xE5, 0xFF) : new SKColor(0xAA, 0xB4, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("📹", ButtonWidth / 2f, 36, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isReplayBufferActive
                ? (isPressed ? new SKColor(0x5E, 0x36, 0x05) : new SKColor(0x3E, 0x24, 0x03))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint
            {
                Color = isReplayBufferActive ? new SKColor(0xFF, 0xA0, 0x00) : new SKColor(0xAA, 0xB4, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("💾", ButtonWidth / 2f, 36, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isActiveScene
                ? (isPressed ? new SKColor(0x06, 0x4E, 0x3B) : new SKColor(0x02, 0x2C, 0x22))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 20);
            using var iconPaint = new SKPaint
            {
                Color = isActiveScene ? new SKColor(0x10, 0xB9, 0x81) : new SKColor(0xAA, 0xB4, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🎬", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            string displayLabel = label.Length > 8 ? label.Substring(0, 7) + "…" : label.ToUpperInvariant();
            using var fontLabel = new SKFont(s_segoeBold, 9.5f);
            using var labelPaint = new SKPaint
            {
                Color = isActiveScene ? new SKColor(0x34, 0xD3, 0x99) : new SKColor(0x9E, 0xAC, 0xCA),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText(displayLabel, ButtonWidth / 2f, 50, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isStudioMode
                ? (isPressed ? new SKColor(0x0C, 0x4A, 0x6E) : new SKColor(0x08, 0x2F, 0x49))
                : (isPressed ? new SKColor(0x22, 0x28, 0x3D) : new SKColor(0x0E, 0x10, 0x18));

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint
            {
                Color = isStudioMode ? new SKColor(0x38, 0xBD, 0xF8) : new SKColor(0x94, 0xA3, 0xB8),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🎭", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10f);
            using var labelPaint = new SKPaint
            {
                Color = isStudioMode ? new SKColor(0x7D, 0xD3, 0xFC) : new SKColor(0xE2, 0xE8, 0xF0),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("STUDIO", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8.5f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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

            using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 10, 10);
            canvas.Save();
            canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

            SKColor bgColor = isPressed ? new SKColor(0x4C, 0x1D, 0x95) : new SKColor(0x2E, 0x10, 0x65);

            using (var bgPaint = new SKPaint { Color = bgColor, Style = SKPaintStyle.Fill, IsAntialias = true })
            {
                canvas.DrawRoundRect(buttonRect, bgPaint);
            }

            using var fontIcon = new SKFont(s_segoeBold, 22);
            using var iconPaint = new SKPaint
            {
                Color = new SKColor(0xC0, 0x84, 0xFC),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("🔀", ButtonWidth / 2f, 32, SKTextAlign.Center, fontIcon, iconPaint);

            using var fontLabel = new SKFont(s_segoeBold, 10f);
            using var labelPaint = new SKPaint
            {
                Color = new SKColor(0xE9, 0xD5, 0xFF),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawText("PRZEJŚCIE", ButtonWidth / 2f, 49, SKTextAlign.Center, fontLabel, labelPaint);

            using var fontBadge = new SKFont(s_segoeBold, 8f);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
            return data.ToArray();
        });
    }
}
