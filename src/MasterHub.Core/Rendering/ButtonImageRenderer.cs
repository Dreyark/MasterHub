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
public static partial class ButtonImageRenderer
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

    public static void ClearCache() => s_cache.Clear();

    private static readonly SKTypeface s_segoeBold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
    private static readonly SKTypeface s_segoeRegular = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal) ?? SKTypeface.Default;
    private static readonly SKTypeface s_arialBold = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
    private static readonly SKTypeface s_emojiTypeface = SKTypeface.FromFamilyName("Segoe UI Emoji") ?? SKTypeface.Default;
    private static readonly SKTypeface s_symbolTypeface = SKTypeface.FromFamilyName("Segoe UI Symbol") ?? SKTypeface.Default;
    private static readonly ConcurrentDictionary<string, SKTypeface> s_typefaceCache = new(StringComparer.OrdinalIgnoreCase);

    public static SKTypeface GetOrCreateTypeface(string? familyName, SKFontStyle? style = null)
    {
        var s = style ?? SKFontStyle.Normal;
        string family = !string.IsNullOrWhiteSpace(familyName) ? familyName : "Segoe UI";
        string key = $"{family}|{s.Weight}|{s.Width}|{s.Slant}";
        return s_typefaceCache.GetOrAdd(key, _ =>
        {
            try
            {
                return SKTypeface.FromFamilyName(family, s)
                       ?? SKTypeface.FromFamilyName("Segoe UI", s)
                       ?? SKTypeface.Default;
            }
            catch
            {
                return SKTypeface.Default;
            }
        });
    }

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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 100);
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
            using var data = MasterHub.Core.Rendering.JpegEncoding.Encode(image, 95);
            return data.ToArray();
        }
        catch
        {
            return jpegBytes;
        }
    }
}
