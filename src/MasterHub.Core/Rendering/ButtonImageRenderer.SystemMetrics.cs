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

    public static byte[] RenderMetricGaugeKeyJpeg(string header, string valueText, float percent, SKColor accentColor, bool isPressed = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        canvas.Clear(new SKColor(0, 0, 0));
        using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        // Tło
        SKColor bg = isPressed ? new SKColor(0x13, 0x24, 0x38) : new SKColor(0x0C, 0x10, 0x19);
        using (var bgPaint = new SKPaint { Color = bg, Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        // 1. Nagłówek (np. CPU, RAM, GPU)
        using (var fontHeader = new SKFont(s_segoeBold, 9.5f))
        using (var headerPaint = new SKPaint { Color = new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(header, ButtonWidth / 2f, 18, SKTextAlign.Center, fontHeader, headerPaint);
        }

        // 2. Wartość główna (np. 42%, 8.4G)
        float fontSize = valueText.Length <= 4 ? 18f : (valueText.Length <= 6 ? 15f : 12f);
        using (var fontValue = new SKFont(s_segoeBold, fontSize))
        using (var valPaint = new SKPaint { Color = isPressed ? new SKColor(0xFF, 0xFF, 0xFF) : accentColor, Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(valueText, ButtonWidth / 2f, 43, SKTextAlign.Center, fontValue, valPaint);
        }

        // 3. Pasek postępu (Gauge / Progress meter)
        float barX = 8f;
        float barY = 56f;
        float barW = ButtonWidth - 16f;
        float barH = 3.5f;
        using var barTrack = new SKRoundRect(new SKRect(barX, barY, barX + barW, barY + barH), 2, 2);
        using (var trackPaint = new SKPaint { Color = new SKColor(0x1A, 0x22, 0x34), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(barTrack, trackPaint);
        }

        float fillW = Math.Clamp(barW * (percent / 100f), 0f, barW);
        if (fillW > 0.5f)
        {
            using var barFill = new SKRoundRect(new SKRect(barX, barY, barX + fillW, barY + barH), 2, 2);
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
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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
        using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        using (var bgPaint = new SKPaint { Color = isPressed ? new SKColor(0x13, 0x24, 0x38) : new SKColor(0x0C, 0x10, 0x19), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        using (var fontHeader = new SKFont(s_segoeBold, 9.5f))
        using (var headerPaint = new SKPaint { Color = new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText("SIEĆ", ButtonWidth / 2f, 18, SKTextAlign.Center, fontHeader, headerPaint);
        }

        using (var fontVal = new SKFont(s_segoeBold, 13f))
        using (var valPaint = new SKPaint { Color = new SKColor(0x38, 0xBD, 0xF8), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            string downText = downMbps < 100
                ? $"↓ {downMbps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"
                : $"↓ {downMbps:0}";
            canvas.DrawText(downText, ButtonWidth / 2f, 38, SKTextAlign.Center, fontVal, valPaint);
        }

        using (var fontSub = new SKFont(s_segoeBold, 9f))
        using (var subPaint = new SKPaint { Color = new SKColor(0x00, 0xF0, 0xB5), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText("Mb/s", ButtonWidth / 2f, 54, SKTextAlign.Center, fontSub, subPaint);
        }

        canvas.Restore();

        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(0x1E, 0x29, 0x3B), IsAntialias = true };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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
        using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        using (var bgPaint = new SKPaint { Color = isPressed ? new SKColor(0x13, 0x24, 0x38) : new SKColor(0x0C, 0x10, 0x19), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        string dayName = time.ToString("ddd", System.Globalization.CultureInfo.CurrentCulture).ToUpperInvariant();
        using (var fontHeader = new SKFont(s_segoeBold, 9f))
        using (var headerPaint = new SKPaint { Color = new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(dayName, ButtonWidth / 2f, 17, SKTextAlign.Center, fontHeader, headerPaint);
        }

        using (var fontTime = new SKFont(s_segoeBold, 18f))
        using (var timePaint = new SKPaint { Color = new SKColor(0x00, 0xF0, 0xB5), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(time.ToString("HH:mm"), ButtonWidth / 2f, 41, SKTextAlign.Center, fontTime, timePaint);
        }

        using (var fontSec = new SKFont(s_segoeBold, 9f))
        using (var secPaint = new SKPaint { Color = new SKColor(0x38, 0xBD, 0xF8), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText($":{time.Second:D2}", ButtonWidth / 2f, 56, SKTextAlign.Center, fontSec, secPaint);
        }

        canvas.Restore();

        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(0x1E, 0x29, 0x3B), IsAntialias = true };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
        return data.ToArray();
    }

    public static byte[] RenderDateJpeg(DateTime date, bool isPressed = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0, 0, 0));
        using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        using (var bgPaint = new SKPaint { Color = isPressed ? new SKColor(0x13, 0x24, 0x38) : new SKColor(0x0C, 0x10, 0x19), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        string monthName = date.ToString("MMM", System.Globalization.CultureInfo.CurrentCulture).ToUpperInvariant();
        using (var fontHeader = new SKFont(s_segoeBold, 9.5f))
        using (var headerPaint = new SKPaint { Color = new SKColor(0xEF, 0x44, 0x44), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(monthName, ButtonWidth / 2f, 17, SKTextAlign.Center, fontHeader, headerPaint);
        }

        using (var fontDay = new SKFont(s_segoeBold, 22f))
        using (var dayPaint = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(date.Day.ToString(), ButtonWidth / 2f, 43, SKTextAlign.Center, fontDay, dayPaint);
        }

        string dayOfWeek = date.ToString("ddd", System.Globalization.CultureInfo.CurrentCulture).ToUpperInvariant();
        using (var fontSub = new SKFont(s_segoeBold, 8.5f))
        using (var subPaint = new SKPaint { Color = new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(dayOfWeek, ButtonWidth / 2f, 56, SKTextAlign.Center, fontSub, subPaint);
        }

        canvas.Restore();

        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = new SKColor(0x1E, 0x29, 0x3B), IsAntialias = true };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
        return data.ToArray();
    }

    public static byte[] RenderFpsJpeg(float fps, bool isPressed = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        canvas.Clear(new SKColor(0, 0, 0));
        using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
        canvas.Save();
        canvas.ClipRoundRect(buttonRect, SKClipOperation.Intersect, true);

        // Tło
        SKColor bg = isPressed ? new SKColor(0x13, 0x24, 0x38) : new SKColor(0x0C, 0x10, 0x19);
        using (var bgPaint = new SKPaint { Color = bg, Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawRoundRect(buttonRect, bgPaint);
        }

        // 1. Nagłówek "FPS"
        using (var fontHeader = new SKFont(s_segoeBold, 10f))
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
        using (var fontValue = new SKFont(s_segoeBold, fontSize))
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
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
        return data.ToArray();
    }

    public static byte[] RenderNowPlayingJpeg(string artist, string title, bool isPlaying, bool isPressed = false, byte[]? artworkJpeg = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0, 0, 0));
        using var buttonRect = new SKRoundRect(new SKRect(2, 2, ButtonWidth - 2, ButtonHeight - 2), 7, 7);
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

        using (var fontIcon = new SKFont(s_segoeBold, 14f))
        using (var iconPaint = new SKPaint { Color = isPlaying ? new SKColor(0x10, 0xB9, 0x81) : new SKColor(0x64, 0x74, 0x8B), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(isPlaying ? "▶ 🎵" : "⏸ 🎵", ButtonWidth / 2f, 20, SKTextAlign.Center, fontIcon, iconPaint);
        }

        string cleanTitle = !string.IsNullOrWhiteSpace(title) ? (title.Length > 9 ? title.Substring(0, 8) + ".." : title) : "MUZYKA";
        using (var fontTitle = new SKFont(s_segoeBold, 10f))
        using (var titlePaint = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(cleanTitle, ButtonWidth / 2f, 38, SKTextAlign.Center, fontTitle, titlePaint);
        }

        string cleanArtist = !string.IsNullOrWhiteSpace(artist) ? (artist.Length > 10 ? artist.Substring(0, 9) + ".." : artist) : (isPlaying ? "GRA" : "PAUZA");
        using (var fontArtist = new SKFont(s_segoeBold, 8.5f))
        using (var artistPaint = new SKPaint { Color = new SKColor(0x38, 0xBD, 0xF8), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.DrawText(cleanArtist, ButtonWidth / 2f, 54, SKTextAlign.Center, fontArtist, artistPaint);
        }

        canvas.Restore();

        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = isPlaying ? new SKColor(0x10, 0xB9, 0x81) : new SKColor(0x1E, 0x29, 0x3B), IsAntialias = true };
        canvas.DrawRoundRect(buttonRect, borderPaint);

        using var image = surface.Snapshot();
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Renderuje bitmapę SkiaSharp (obrazek, klatkę GIF lub wideo) do 72x72 JPEG na całej powierzchni klawisza LCD –
    /// bez czarnego marginesu, zaokrągleń i ramki (<see cref="KeyFrame.FullBleed"/>). Wciśnięcie to przyciemnienie całości.
    /// </summary>
    public static byte[] RenderBitmapJpeg(SKBitmap bmp, bool isPressed = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ButtonWidth, ButtonHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        using var frame = KeyFrame.FullBleed(ButtonWidth, ButtonHeight);
        using (var imgPaint = new SKPaint { IsAntialias = true, IsDither = true })
        {
            canvas.DrawBitmap(bmp, frame.ContentRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), imgPaint);
        }

        if (isPressed)
        {
            using var tintPaint = new SKPaint { Color = new SKColor(0x00, 0xFF, 0xCC, 0x55) };
            canvas.DrawRect(frame.ContentRect, tintPaint);
        }

        using var image = surface.Snapshot();
        using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
        return data.ToArray();
    }
}
