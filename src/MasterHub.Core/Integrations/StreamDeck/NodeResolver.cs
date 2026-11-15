using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MasterHub.Core.Integrations.StreamDeck;

/// <summary>The file system as the Node resolver sees it (a fake in tests).</summary>
public interface INodeFileSystem
{
    bool FileExists(string path);
    IEnumerable<string> Directories(string path);
    string? Environment(string name);
    /// <summary>The product version of an executable (<c>20.11.1</c>), or <c>null</c>.</summary>
    Version? FileVersion(string path);
}

public enum NodeProblem { None, Missing, TooOld }

/// <summary>The Node.js chosen for a plugin, or why there is none.</summary>
public sealed record NodeResolution(string? Path, Version? Version, NodeProblem Problem, int RequiredMajor);

/// <summary>
/// Finds <c>node.exe</c> for Node.js plugins (T-31 b). Candidates, in order: a runtime MasterHub downloaded with the user's
/// consent (<c>%APPDATA%\MasterHub\runtime\node-v&lt;version&gt;</c>, newest first), <c>NVM_SYMLINK</c>, the usual install folders
/// and <c>PATH</c>. The plugin's <c>Nodejs.Version</c> (a major version, default 20) must be met; a Node that is too old is
/// reported as such instead of starting a plugin that would crash.
/// </summary>
public static class NodeResolver
{
    public const int DefaultRequiredMajor = 20;

    public static string ManagedRuntimeRoot =>
        System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "MasterHub", "runtime");

    /// <summary>The major version a manifest asks for (<c>"20"</c>, <c>"20.11"</c>, <c>"v18"</c>); the default when it says nothing usable.</summary>
    public static int RequiredMajor(string? manifestVersion)
    {
        string v = (manifestVersion ?? "").Trim().TrimStart('v', 'V');
        int dot = v.IndexOf('.');
        if (dot >= 0) v = v[..dot];
        return int.TryParse(v, out int major) && major is > 0 and < 1000 ? major : DefaultRequiredMajor;
    }

    public static NodeResolution Resolve(INodeFileSystem fs, int requiredMajor, string? managedRoot = null)
    {
        Version? bestFound = null;
        string? bestPath = null;
        foreach (string candidate in Candidates(fs, managedRoot ?? ManagedRuntimeRoot))
        {
            if (!fs.FileExists(candidate)) continue;
            var version = VersionOf(fs, candidate);
            if (version == null || version.Major >= requiredMajor)
            {
                // an unknown version is given the benefit of the doubt (an old node.exe without file version is rare)
                return new NodeResolution(candidate, version, NodeProblem.None, requiredMajor);
            }
            if (bestFound == null || version > bestFound)
            {
                bestFound = version;
                bestPath = candidate;
            }
        }
        return bestPath != null
            ? new NodeResolution(bestPath, bestFound, NodeProblem.TooOld, requiredMajor)
            : new NodeResolution(null, null, NodeProblem.Missing, requiredMajor);
    }

    private static Version? VersionOf(INodeFileSystem fs, string path)
    {
        var fromFile = fs.FileVersion(path);
        if (fromFile != null && fromFile.Major > 0) return fromFile;
        // a managed runtime folder carries its version in its name: runtime\node-v20.11.1\node.exe
        string? folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
        return folder != null && folder.StartsWith("node-v", StringComparison.OrdinalIgnoreCase) && Version.TryParse(folder[6..], out var v) ? v : null;
    }

    internal static IEnumerable<string> Candidates(INodeFileSystem fs, string managedRoot)
    {
        var managed = new List<(Version v, string path)>();
        foreach (string dir in SafeDirectories(fs, managedRoot))
        {
            string name = System.IO.Path.GetFileName(dir);
            if (name.StartsWith("node-v", StringComparison.OrdinalIgnoreCase) && Version.TryParse(name[6..], out var v))
                managed.Add((v, System.IO.Path.Combine(dir, "node.exe")));
        }
        foreach (var m in managed.OrderByDescending(m => m.v)) yield return m.path;

        string? nvm = fs.Environment("NVM_SYMLINK");
        if (!string.IsNullOrWhiteSpace(nvm)) yield return System.IO.Path.Combine(nvm, "node.exe");

        yield return @"C:\nvm4w\nodejs\node.exe";
        string? programFiles = fs.Environment("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles)) yield return System.IO.Path.Combine(programFiles, "nodejs", "node.exe");
        string? programFilesX86 = fs.Environment("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(programFilesX86)) yield return System.IO.Path.Combine(programFilesX86, "nodejs", "node.exe");
        string? localAppData = fs.Environment("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(localAppData)) yield return System.IO.Path.Combine(localAppData, "Programs", "node", "node.exe");

        foreach (string part in (fs.Environment("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return System.IO.Path.Combine(part, "node.exe");
        }
    }

    private static IEnumerable<string> SafeDirectories(INodeFileSystem fs, string path)
    {
        try
        {
            return fs.Directories(path).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}

/// <summary>The real file system.</summary>
public sealed class RealNodeFileSystem : INodeFileSystem
{
    public static readonly RealNodeFileSystem Instance = new();

    public bool FileExists(string path)
    {
        try { return File.Exists(path); } catch { return false; }
    }

    public IEnumerable<string> Directories(string path) => Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>();

    public string? Environment(string name) => System.Environment.GetEnvironmentVariable(name);

    public Version? FileVersion(string path)
    {
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            return info.ProductMajorPart > 0 ? new Version(info.ProductMajorPart, info.ProductMinorPart, Math.Max(0, info.ProductBuildPart)) : null;
        }
        catch
        {
            return null;
        }
    }
}
