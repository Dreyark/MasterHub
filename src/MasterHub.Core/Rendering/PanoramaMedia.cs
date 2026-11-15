using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

/// <summary>
/// The pictures of the whole-deck wallpaper: one 480 × 272 JPEG for a still picture, many for a GIF, an animated WebP or a video.
/// The same fit / zoom / move rules apply as for the picture of a single key, so the wallpaper can be placed as one piece.
/// </summary>
public static class PanoramaMedia
{
    /// <summary>The most frames kept (a video is cut here, like the animation of a single key).</summary>
    public const int MaxFrames = 120;

    private const int CacheSize = 3;
    private static readonly ConcurrentDictionary<string, IReadOnlyList<byte[]>> s_cache = new();
    private static readonly ConcurrentQueue<string> s_order = new();

    public static bool IsMoving(string? path) => !string.IsNullOrWhiteSpace(path) && MediaValidationService.IsAnimatedOrVideo(path);

    /// <returns>The frames, or one blank frame when the file cannot be read. Never empty, never throws.</returns>
    public static IReadOnlyList<byte[]> ExtractFrames(string? path, string fitMode = "Cover", int scale = 100, int offsetX = 0, int offsetY = 0, int maxFrames = MaxFrames)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new[] { ButtonImageRenderer.GetBlankPanoramicJpeg() };

        string key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|{fitMode}|{scale}|{offsetX}|{offsetY}|{maxFrames}";
        if (s_cache.TryGetValue(key, out var cached)) return cached;

        var frames = new List<byte[]>();
        try
        {
            if (MediaValidationService.IsVideoFile(path))
                frames = MediaFoundationVideoDecoder.ExtractVideoFrames(path, maxFrames, fitMode, scale, offsetX, offsetY,
                    ButtonImageRenderer.PanelWidth, ButtonImageRenderer.PanelHeight);
            else if (MediaValidationService.IsAnimatedOrVideo(path))
                frames = ExtractAnimatedFrames(path, fitMode, scale, offsetX, offsetY, maxFrames);
        }
        catch
        {
            frames.Clear();
        }

        // a still picture, or an animation we could not read: the first picture it has
        if (frames.Count == 0) frames.Add(ButtonImageRenderer.RenderPanoramicImage(path, fitMode, scale, offsetX, offsetY));

        Remember(key, frames);
        return frames;
    }

    private static List<byte[]> ExtractAnimatedFrames(string path, string fitMode, int scale, int offsetX, int offsetY, int maxFrames)
    {
        var frames = new List<byte[]>();
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        if (codec == null || codec.FrameCount <= 0) return frames;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        int count = Math.Min(codec.FrameCount, maxFrames);
        for (int i = 0; i < count; i++)
        {
            using var frame = new SKBitmap(info);
            var result = codec.GetPixels(info, frame.GetPixels(), new SKCodecOptions(i));
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) continue;

            frames.Add(ButtonImageRenderer.RenderPanoramicFrame(frame, fitMode, scale, offsetX, offsetY));
        }

        return frames;
    }

    private static void Remember(string key, IReadOnlyList<byte[]> frames)
    {
        s_cache[key] = frames;
        s_order.Enqueue(key);
        while (s_order.Count > CacheSize && s_order.TryDequeue(out var old)) s_cache.TryRemove(old, out _);
    }

    public static void ClearCache()
    {
        s_cache.Clear();
        while (s_order.TryDequeue(out _)) { }
    }
}
