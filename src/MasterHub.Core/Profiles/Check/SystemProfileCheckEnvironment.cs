using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MasterHub.Core.Audio;

namespace MasterHub.Core.Profiles.Check;

/// <summary>
/// The real lookups of the profile check: the file system, PATH / PATHEXT, the Windows "App Paths" registry and the names of
/// installed apps. What integrations know (audio devices, OBS scenes, Home Assistant entities, plugins) is filled in by the
/// caller from the services, and left <c>null</c> when the service cannot see it now.
/// </summary>
public sealed class SystemProfileCheckEnvironment : IProfileCheckEnvironment
{
    private readonly Func<string, bool> _appPathsLookup;
    private readonly HashSet<string> _installedNames;

    /// <param name="installedAppNames">Names of installed apps (T-36 catalog), compared without <c>.exe</c> and case.</param>
    /// <param name="appPathsLookup">For tests: replaces the registry lookup.</param>
    public SystemProfileCheckEnvironment(IEnumerable<string>? installedAppNames = null, Func<string, bool>? appPathsLookup = null)
    {
        _installedNames = new HashSet<string>(
            (installedAppNames ?? Enumerable.Empty<string>()).Select(StripExe).Where(n => n.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        _appPathsLookup = appPathsLookup ?? InAppPaths;
    }

    public IReadOnlyList<AudioDeviceInfo>? AudioDevices { get; init; }
    public IReadOnlyCollection<string>? ObsScenes { get; init; }
    public IReadOnlyCollection<string>? HomeAssistantEntities { get; init; }
    public IReadOnlyCollection<string>? InstalledPlugins { get; init; }

    public bool FileExists(string path)
    {
        try { return File.Exists(path); } catch { return false; }
    }

    public bool DirectoryExists(string path)
    {
        try { return Directory.Exists(path); } catch { return false; }
    }

    public bool BareNameResolves(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string bare = name.Trim();
        if (_installedNames.Contains(StripExe(bare))) return true;
        if (OnPath(bare)) return true;
        string exe = bare.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? bare : bare + ".exe";
        return _appPathsLookup(exe);
    }

    private static string StripExe(string name)
    {
        string n = (name ?? "").Trim();
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }

    private bool OnPath(string name)
    {
        string[] extensions = Path.HasExtension(name)
            ? new[] { "" }
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries).Prepend("").ToArray();
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        foreach (string folder in folders)
        {
            foreach (string ext in extensions)
            {
                try
                {
                    if (File.Exists(Path.Combine(folder, name + ext))) return true;
                }
                catch
                {
                    // a broken PATH entry is skipped
                }
            }
        }
        return false;
    }

    private static bool InAppPaths(string exe)
    {
        if (!OperatingSystem.IsWindows()) return false;
        const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
        try
        {
            using var user = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key + exe);
            if (user != null) return true;
            using var machine = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key + exe);
            return machine != null;
        }
        catch
        {
            return false;
        }
    }
}
