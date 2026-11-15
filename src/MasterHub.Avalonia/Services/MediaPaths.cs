using Microsoft.Extensions.DependencyInjection;
using MasterHub.Core.Profiles.Packages;

namespace MasterHub.Avalonia.Services;

/// <summary>Picked pictures are copied into the media store at pick time (T-09), so a profile keeps working when the original moves.</summary>
public static class MediaPaths
{
    /// <summary>The path of the store copy, or the original path when it cannot be copied (missing, too large, no store).</summary>
    public static string ToStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        try
        {
            return App.Services?.GetService<IMediaStore>()?.Import(path) ?? path;
        }
        catch
        {
            return path;
        }
    }
}
