using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// Zunifikowany ekstraktor klatek dla obrazów, animacji GIF/WebP oraz wideo (MP4/AVI/MOV/WMV/WEBM/MKV).
/// Skaluje i kadruje zawartość do natywnej rozdzielczości 72x72 klawisza LCD z buforowaniem w pamięci RAM.
/// </summary>
public static class MediaFrameExtractor
{
    private const int MaxFramesCacheEntries = 8;
    private const int MaxFirstFrameCacheEntries = 32;

    private static readonly ConcurrentDictionary<string, IReadOnlyList<byte[]>> s_framesCache = new();
    private static readonly ConcurrentQueue<string> s_framesKeys = new();

    private static readonly ConcurrentDictionary<string, byte[]> s_firstFrameJpegCache = new();
    private static readonly ConcurrentQueue<string> s_firstFrameKeys = new();

    private static void CacheFrames(string key, IReadOnlyList<byte[]> frames)
    {
        s_framesCache[key] = frames;
        s_framesKeys.Enqueue(key);
        while (s_framesKeys.Count > MaxFramesCacheEntries && s_framesKeys.TryDequeue(out var oldKey))
        {
            s_framesCache.TryRemove(oldKey, out _);
        }
    }

    private static void CacheFirstFrame(string key, byte[] frame)
    {
        s_firstFrameJpegCache[key] = frame;
        s_firstFrameKeys.Enqueue(key);
        while (s_firstFrameKeys.Count > MaxFirstFrameCacheEntries && s_firstFrameKeys.TryDequeue(out var oldKey))
        {
            s_firstFrameJpegCache.TryRemove(oldKey, out _);
        }
    }

    public static IReadOnlyList<byte[]> ExtractFrames(
        string filePath,
        int maxFrames = 120,
        string fitMode = "Cover",
        int scale = 100,
        int offsetX = 0,
        int offsetY = 0)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return Array.Empty<byte[]>();

        string cacheKey = $"{filePath}|{File.GetLastWriteTimeUtc(filePath).Ticks}|{maxFrames}|{fitMode}|{scale}|{offsetX}|{offsetY}";
        if (s_framesCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var frames = new List<byte[]>();

        if (MediaValidationService.IsVideoFile(filePath))
        {
            frames = MediaFoundationVideoDecoder.ExtractVideoFrames(filePath, maxFrames, fitMode, scale, offsetX, offsetY);
        }
        else
        {
            // GIF / Animated WebP / Static Image
            try
            {
                using var stream = File.OpenRead(filePath);
                using var codec = SKCodec.Create(stream);

                if (codec != null && codec.FrameCount > 0)
                {
                    int framesToRead = Math.Min(codec.FrameCount, maxFrames);
                    var srcInfo = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                    const int targetSize = ButtonImageRenderer.ButtonWidth; // 72 px
                    var destBounds = new SKRect(0, 0, targetSize, targetSize);

                    for (int i = 0; i < framesToRead; i++)
                    {
                        using var frameBmp = new SKBitmap(srcInfo);
                        var options = new SKCodecOptions(i);
                        var result = codec.GetPixels(srcInfo, frameBmp.GetPixels(), options);

                        if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput)
                        {
                            using var scaledBmp = new SKBitmap(targetSize, targetSize, SKColorType.Rgba8888, SKAlphaType.Premul);
                            using (var canvas = new SKCanvas(scaledBmp))
                            {
                                canvas.Clear(SKColors.Black);
                                ButtonImageRenderer.DrawTransformedBitmap(canvas, frameBmp, destBounds, fitMode, scale, offsetX, offsetY);
                            }

                            byte[] jpeg = ButtonImageRenderer.RenderBitmapJpeg(scaledBmp);
                            frames.Add(jpeg);
                        }
                    }
                }
            }
            catch { }

            // Fallback: pojedyncza klatka z SKBitmap.Decode
            if (frames.Count == 0)
            {
                try
                {
                    using var singleBmp = SKBitmap.Decode(filePath);
                    if (singleBmp != null)
                    {
                        const int targetSize = ButtonImageRenderer.ButtonWidth;
                        var destBounds = new SKRect(0, 0, targetSize, targetSize);

                        using var scaledBmp = new SKBitmap(targetSize, targetSize, SKColorType.Rgba8888, SKAlphaType.Premul);
                        using (var canvas = new SKCanvas(scaledBmp))
                        {
                            canvas.Clear(SKColors.Black);
                            ButtonImageRenderer.DrawTransformedBitmap(canvas, singleBmp, destBounds, fitMode, scale, offsetX, offsetY);
                        }

                        frames.Add(ButtonImageRenderer.RenderBitmapJpeg(scaledBmp));
                    }
                }
                catch { }
            }
        }

        CacheFrames(cacheKey, frames);
        if (frames.Count > 0)
        {
            string firstKey = $"{filePath}|{fitMode}|{scale}|{offsetX}|{offsetY}";
            CacheFirstFrame(firstKey, frames[0]);
        }

        return frames;
    }

    public static byte[]? GetFirstFrameJpeg(
        string filePath,
        string fitMode = "Cover",
        int scale = 100,
        int offsetX = 0,
        int offsetY = 0)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        string firstKey = $"{filePath}|{fitMode}|{scale}|{offsetX}|{offsetY}";
        if (s_firstFrameJpegCache.TryGetValue(firstKey, out var cached))
            return cached;

        var frames = ExtractFrames(filePath, maxFrames: 1, fitMode, scale, offsetX, offsetY);
        return frames.Count > 0 ? frames[0] : null;
    }

    public static SKBitmap? GetFirstFrameBitmap(
        string filePath,
        string fitMode = "Cover",
        int scale = 100,
        int offsetX = 0,
        int offsetY = 0)
    {
        byte[]? jpeg = GetFirstFrameJpeg(filePath, fitMode, scale, offsetX, offsetY);
        return jpeg != null ? SKBitmap.Decode(jpeg) : null;
    }

    public static void ClearCache()
    {
        s_framesCache.Clear();
        while (s_framesKeys.TryDequeue(out _)) { }
        s_firstFrameJpegCache.Clear();
        while (s_firstFrameKeys.TryDequeue(out _)) { }
    }
}
