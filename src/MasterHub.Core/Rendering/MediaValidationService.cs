using System;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace MasterHub.Core.Rendering;

public enum MediaCategory
{
    None,
    Image,
    Animation,
    Video
}

public sealed record MediaValidationResult(
    bool IsValid,
    string? ErrorKey,
    object[]? ErrorArgs,
    MediaCategory Category,
    int Width,
    int Height,
    long FileSize)
{
    public static MediaValidationResult Success(MediaCategory cat, int w, int h, long size) =>
        new(true, null, null, cat, w, h, size);

    public static MediaValidationResult Failure(string errorKey, object[]? args, MediaCategory cat = MediaCategory.None, int w = 0, int h = 0, long size = 0) =>
        new(false, errorKey, args, cat, w, h, size);
}

public interface IMediaValidationService
{
    MediaValidationResult Validate(string filePath);
    bool IsAnimatedOrVideo(string? filePath);
    bool IsVideo(string? filePath);
    bool IsSupportedMedia(string? filePath);
}

public sealed class MediaValidationService : IMediaValidationService
{
    public const int MinPixelDimension = 16;
    public const int MaxImageWidth = 2560;
    public const int MaxImageHeight = 1440;
    public const int MaxVideoWidth = 1920;
    public const int MaxVideoHeight = 1080;

    public const long MaxImageFileSize = 25 * 1024 * 1024;       // 25 MB
    public const long MaxAnimationFileSize = 35 * 1024 * 1024;   // 35 MB
    public const long MaxVideoFileSize = 60 * 1024 * 1024;       // 60 MB

    public static readonly string[] VideoExtensions = [".mp4", ".avi", ".mov", ".wmv", ".webm", ".mkv"];
    public static readonly string[] AnimationExtensions = [".gif"];
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp", ".ico"];

    public static readonly MediaValidationService Instance = new();

    public static bool IsVideoFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext = Path.GetExtension(path);
        return Array.Exists(VideoExtensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsAnimatedImageFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext = Path.GetExtension(path);
        if (Array.Exists(AnimationExtensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var codec = SKCodec.Create(stream);
                return codec != null && codec.FrameCount > 1;
            }
            catch { }
        }
        return false;
    }

    public static bool IsAnimatedOrVideo(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        return IsVideoFile(filePath) || IsAnimatedImageFile(filePath);
    }

    public bool IsVideo(string? filePath) => IsVideoFile(filePath);

    bool IMediaValidationService.IsAnimatedOrVideo(string? filePath) => IsAnimatedOrVideo(filePath);

    public bool IsSupportedMedia(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        string ext = Path.GetExtension(filePath);
        return Array.Exists(VideoExtensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) ||
               Array.Exists(AnimationExtensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)) ||
               Array.Exists(ImageExtensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    public MediaValidationResult Validate(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return MediaValidationResult.Failure("Media_Error_CorruptOrUnsupported", null);
        }

        var fileInfo = new FileInfo(filePath);
        long fileSize = fileInfo.Length;

        if (IsVideoFile(filePath))
        {
            if (fileSize > MaxVideoFileSize)
            {
                return MediaValidationResult.Failure(
                    "Media_Error_FileSize",
                    [fileSize / (1024.0 * 1024.0), MaxVideoFileSize / (1024 * 1024)],
                    MediaCategory.Video, 0, 0, fileSize);
            }

            var (vw, vh) = GetVideoDimensions(filePath);
            if (vw <= 0 || vh <= 0)
            {
                return MediaValidationResult.Failure("Media_Error_CorruptOrUnsupported", null, MediaCategory.Video, 0, 0, fileSize);
            }

            if (vw < MinPixelDimension || vh < MinPixelDimension)
            {
                return MediaValidationResult.Failure(
                    "Media_Error_TooSmall",
                    [MinPixelDimension, MinPixelDimension],
                    MediaCategory.Video, vw, vh, fileSize);
            }

            if (vw > MaxVideoWidth || vh > MaxVideoHeight)
            {
                return MediaValidationResult.Failure(
                    "Media_Error_VideoTooLarge",
                    [MaxVideoWidth, MaxVideoHeight],
                    MediaCategory.Video, vw, vh, fileSize);
            }

            return MediaValidationResult.Success(MediaCategory.Video, vw, vh, fileSize);
        }

        // Animated or Static image validation via SKCodec
        try
        {
            using var stream = File.OpenRead(filePath);
            using var codec = SKCodec.Create(stream);

            if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
            {
                // Fallback to SKBitmap.Decode
                using var bmp = SKBitmap.Decode(filePath);
                if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0)
                {
                    return MediaValidationResult.Failure("Media_Error_CorruptOrUnsupported", null, MediaCategory.None, 0, 0, fileSize);
                }

                return ValidateDimensions(bmp.Width, bmp.Height, fileSize, isAnimation: false);
            }

            bool isAnimation = codec.FrameCount > 1;
            return ValidateDimensions(codec.Info.Width, codec.Info.Height, fileSize, isAnimation);
        }
        catch
        {
            return MediaValidationResult.Failure("Media_Error_CorruptOrUnsupported", null, MediaCategory.None, 0, 0, fileSize);
        }
    }

    private static MediaValidationResult ValidateDimensions(int w, int h, long fileSize, bool isAnimation)
    {
        MediaCategory cat = isAnimation ? MediaCategory.Animation : MediaCategory.Image;
        long maxFileSize = isAnimation ? MaxAnimationFileSize : MaxImageFileSize;

        if (fileSize > maxFileSize)
        {
            return MediaValidationResult.Failure(
                "Media_Error_FileSize",
                [fileSize / (1024.0 * 1024.0), maxFileSize / (1024 * 1024)],
                cat, w, h, fileSize);
        }

        if (w < MinPixelDimension || h < MinPixelDimension)
        {
            return MediaValidationResult.Failure(
                "Media_Error_TooSmall",
                [MinPixelDimension, MinPixelDimension],
                cat, w, h, fileSize);
        }

        int maxW = isAnimation ? MaxVideoWidth : MaxImageWidth;
        int maxH = isAnimation ? MaxVideoHeight : MaxImageHeight;

        if (w > maxW || h > maxH)
        {
            string err = isAnimation ? "Media_Error_VideoTooLarge" : "Media_Error_ImageTooLarge";
            return MediaValidationResult.Failure(err, [maxW, maxH], cat, w, h, fileSize);
        }

        return MediaValidationResult.Success(cat, w, h, fileSize);
    }

    private static (int width, int height) GetVideoDimensions(string videoPath)
    {
        try
        {
            return MediaFoundationVideoDecoder.GetVideoDimensions(videoPath);
        }
        catch
        {
            return (0, 0);
        }
    }
}
