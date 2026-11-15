using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MasterHub.Core.Profiles.Packages;

/// <summary>
/// Pictures a profile uses, kept under <c>%APPDATA%\MasterHub\media\&lt;sha256&gt;.&lt;ext&gt;</c> (T-09). A file the user picks is
/// <b>copied here at pick time</b>, so the profile keeps working when the original moves or is deleted, and a profile can be
/// exported with its pictures. Names come from the content, so the store is append-only (same picture = same file) and a
/// backup of the profiles never needs to copy it.
/// </summary>
public interface IMediaStore
{
    /// <summary>The folder of the store.</summary>
    string Directory { get; }

    /// <summary>
    /// Copies a picture into the store and returns the path of the copy; the path of a file that is already in the store
    /// comes back unchanged. Returns <c>null</c> when the file is missing, unreadable or larger than <see cref="MediaStore.MaxStoredBytes"/>
    /// (the caller then keeps the original path).
    /// </summary>
    string? Import(string sourcePath);

    /// <summary>Stores bytes (an image that came out of a package) and returns the path.</summary>
    string ImportBytes(byte[] data, string extension);

    bool IsStorePath(string? path);
}

/// <inheritdoc cref="IMediaStore"/>
public sealed class MediaStore : IMediaStore
{
    /// <summary>Pictures larger than this are not copied (a long video stays where it is).</summary>
    public const long MaxStoredBytes = 64L * 1024 * 1024;

    /// <summary>Formats that travel in a package. Anything else stays an external reference.</summary>
    public static readonly string[] PackableExtensions = { ".png", ".jpg", ".gif", ".webp", ".svg" };

    private readonly object _lock = new();

    public MediaStore(string? baseDirectory = null)
    {
        string baseDir = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub");
        Directory = Path.Combine(baseDir, "media");
    }

    public string Directory { get; }

    public static bool IsPackable(string? path) =>
        !string.IsNullOrEmpty(path) && PackableExtensions.Contains(NormalizeExtension(Path.GetExtension(path)));

    /// <summary>Lower-case extension with a leading dot; <c>.jpeg</c> is stored as <c>.jpg</c>.</summary>
    public static string NormalizeExtension(string? extension)
    {
        string e = (extension ?? "").Trim().ToLowerInvariant();
        if (e.Length > 0 && e[0] != '.') e = "." + e;
        return e == ".jpeg" ? ".jpg" : e;
    }

    public bool IsStorePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(Directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public string? Import(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;
        if (IsStorePath(sourcePath)) return sourcePath;

        try
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length > MaxStoredBytes) return null;

            string extension = NormalizeExtension(info.Extension);
            using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            string target = Path.Combine(Directory, hash + extension);

            lock (_lock)
            {
                System.IO.Directory.CreateDirectory(Directory);
                if (!File.Exists(target))
                {
                    string tmp = $"{target}.{Guid.NewGuid():N}.tmp";
                    try
                    {
                        stream.Position = 0;
                        using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
                        {
                            stream.CopyTo(output);
                        }
                        File.Move(tmp, target, overwrite: true);
                    }
                    finally
                    {
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    }
                }
            }
            return target;
        }
        catch
        {
            return null; // unreadable / locked: the key simply keeps the path the user picked
        }
    }

    public string ImportBytes(byte[] data, string extension)
    {
        string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        string target = Path.Combine(Directory, hash + NormalizeExtension(extension));

        lock (_lock)
        {
            System.IO.Directory.CreateDirectory(Directory);
            if (!File.Exists(target))
            {
                string tmp = $"{target}.{Guid.NewGuid():N}.tmp";
                try
                {
                    File.WriteAllBytes(tmp, data);
                    File.Move(tmp, target, overwrite: true);
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }
        }
        return target;
    }
}
