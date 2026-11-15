using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MasterHub.Core.Apps;

/// <summary>One place that is searched for installed apps (Start menu, Store, Steam, Epic, GOG).</summary>
public interface IInstalledAppSource
{
    string Name { get; }
    IEnumerable<InstalledApp> Scan(CancellationToken cancellationToken);
}

/// <summary>
/// What is installed on this PC, for the "Open app" picker, "Link app" (Smart Profiles) and game presets.
/// Scanned on demand on a background thread and cached in <c>%APPDATA%\MasterHub\installed.json</c>.
/// Game covers come from the launcher's local cache for display only and are never persisted.
/// </summary>
public interface IInstalledAppsService
{
    /// <summary>Result of the last scan (or the cache loaded at start), sorted by name.</summary>
    IReadOnlyList<InstalledApp> Apps { get; }

    bool IsScanning { get; }

    /// <summary>UTC time of the last completed scan, or <c>null</c> when only the cache (or nothing) is available.</summary>
    DateTime? LastScanUtc { get; }

    /// <summary>Scans all sources again (the picker's Refresh button).</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised (on a background thread) after a scan finished and <see cref="Apps"/> changed.</summary>
    event Action? Changed;
}

public sealed class InstalledAppsService : IInstalledAppsService
{
    private readonly string _cachePath;
    private readonly IReadOnlyList<IInstalledAppSource> _sources;
    private readonly object _lock = new();
    private IReadOnlyList<InstalledApp> _apps = Array.Empty<InstalledApp>();
    private int _scanning;

    public InstalledAppsService(string? cachePath = null, IEnumerable<IInstalledAppSource>? sources = null)
    {
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub", "installed.json");
        _sources = (sources ?? DefaultSources()).ToList();
        LoadCache();
    }

    public event Action? Changed;

    public IReadOnlyList<InstalledApp> Apps
    {
        get { lock (_lock) { return _apps; } }
    }

    public bool IsScanning => Volatile.Read(ref _scanning) == 1;

    public DateTime? LastScanUtc { get; private set; }

    public static IEnumerable<IInstalledAppSource> DefaultSources() => new IInstalledAppSource[]
    {
        new StartMenuSource(),
        new StoreAppsSource(),
        new SteamSource(),
        new EpicSource(),
        new GogSource()
    };

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _scanning, 1, 0) != 0) return;
        try
        {
            var result = await Task.Run(() => ScanAll(cancellationToken), cancellationToken).ConfigureAwait(false);
            lock (_lock) { _apps = result; }
            LastScanUtc = DateTime.UtcNow;
            SaveCache(result);
            Changed?.Invoke();
        }
        finally
        {
            Volatile.Write(ref _scanning, 0);
        }
    }

    private List<InstalledApp> ScanAll(CancellationToken ct)
    {
        var all = new List<InstalledApp>();
        foreach (var source in _sources)
        {
            ct.ThrowIfCancellationRequested();
            try { all.AddRange(source.Scan(ct)); }
            catch (OperationCanceledException) { throw; }
            catch { /* one broken launcher must not hide the others */ }
        }

        // The same id can appear twice (e.g. two Start-menu shortcuts of one app): keep the first, sort by name.
        return all
            .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return;
            var cached = JsonSerializer.Deserialize<List<InstalledApp>>(File.ReadAllText(_cachePath));
            if (cached != null) _apps = cached;
        }
        catch
        {
            // unreadable cache: it is only a cache, the next scan rebuilds it
        }
    }

    private void SaveCache(IReadOnlyList<InstalledApp> apps)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = $"{_cachePath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(apps, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _cachePath, overwrite: true);
        }
        catch
        {
            // a cache that cannot be written only costs a rescan next time
        }
    }
}

/// <summary>Start-menu shortcuts of the current user and of all users.</summary>
public sealed class StartMenuSource : IInstalledAppSource
{
    private readonly IReadOnlyList<string> _roots;
    private readonly Func<string, string?> _resolveShortcut;

    public StartMenuSource(IEnumerable<string>? roots = null, Func<string, string?>? resolveShortcut = null)
    {
        _roots = (roots ?? new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
        }).ToList();
        _resolveShortcut = resolveShortcut ?? (path => new Rendering.AppIconExtractorService().ResolveShortcutTarget(path));
    }

    public string Name => "Start menu";

    public IEnumerable<InstalledApp> Scan(CancellationToken cancellationToken)
    {
        foreach (string root in _roots.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToList(); }
            catch { continue; }

            foreach (string lnk in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = Path.GetFileNameWithoutExtension(lnk);
                if (name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Documentation", StringComparison.OrdinalIgnoreCase)) continue;

                string? target = null;
                try { target = _resolveShortcut(lnk); } catch { }
                if (string.IsNullOrWhiteSpace(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                yield return new InstalledApp($"startmenu:{name.ToLowerInvariant()}", name, InstalledAppKind.StartMenu, lnk, target);
            }
        }
    }
}

/// <summary>Microsoft Store apps (AUMIDs), via <c>Get-StartApps</c>.</summary>
public sealed class StoreAppsSource : IInstalledAppSource
{
    private readonly Func<string> _runStartApps;

    public StoreAppsSource(Func<string>? runStartApps = null) => _runStartApps = runStartApps ?? RunPowerShell;

    public string Name => "Microsoft Store";

    public IEnumerable<InstalledApp> Scan(CancellationToken cancellationToken) =>
        InstalledAppParsers.ParseStartAppsJson(_runStartApps());

    private static string RunPowerShell()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | ConvertTo-Json -Compress\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return "";
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(20_000))
        {
            try { process.Kill(true); } catch { }
            return "";
        }
        return output.GetAwaiter().GetResult();
    }
}

/// <summary>Steam games: <c>libraryfolders.vdf</c> + <c>appmanifest_*.acf</c>.</summary>
public sealed class SteamSource : IInstalledAppSource
{
    private readonly string? _steamRoot;

    public SteamSource(string? steamRoot = null) => _steamRoot = steamRoot ?? FindSteamRoot();

    public string Name => "Steam";

    public IEnumerable<InstalledApp> Scan(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_steamRoot) || !Directory.Exists(_steamRoot)) yield break;

        var libraries = new List<string> { _steamRoot };
        string vdf = Path.Combine(_steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            try { libraries.AddRange(InstalledAppParsers.ParseSteamLibraryFolders(File.ReadAllText(vdf))); } catch { }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string library in libraries.Where(p => seen.Add(Path.GetFullPath(p))))
        {
            string apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;

            foreach (string manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                InstalledApp? app = null;
                try
                {
                    string text = File.ReadAllText(manifest);
                    string id = Path.GetFileNameWithoutExtension(manifest)["appmanifest_".Length..];
                    app = InstalledAppParsers.ParseSteamManifest(text, FindCover(id));
                }
                catch { }
                if (app != null) yield return app;
            }
        }
    }

    // Covers: only from Steam's own local cache, only for display on this PC.
    private string? FindCover(string appId)
    {
        string cache = Path.Combine(_steamRoot!, "appcache", "librarycache");
        foreach (string name in new[] { $"{appId}_library_600x900.jpg", $"{appId}_header.jpg" })
        {
            string path = Path.Combine(cache, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static string? FindSteamRoot()
    {
        try
        {
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string p && Directory.Exists(p))
                return p.Replace('/', Path.DirectorySeparatorChar);
        }
        catch { }

        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }
}

/// <summary>Epic Games Launcher: <c>%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item</c>.</summary>
public sealed class EpicSource : IInstalledAppSource
{
    private readonly string _manifestsDir;

    public EpicSource(string? manifestsDir = null)
    {
        _manifestsDir = manifestsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
    }

    public string Name => "Epic Games";

    public IEnumerable<InstalledApp> Scan(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_manifestsDir)) yield break;

        foreach (string file in Directory.EnumerateFiles(_manifestsDir, "*.item"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstalledApp? app = null;
            try { app = InstalledAppParsers.ParseEpicManifest(File.ReadAllText(file)); } catch { }
            if (app != null) yield return app;
        }
    }
}

/// <summary>GOG Galaxy: installed games under <c>HKLM\SOFTWARE\WOW6432Node\GOG.com\Games</c>.</summary>
public sealed class GogSource : IInstalledAppSource
{
    private const string GamesKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";

    public string Name => "GOG Galaxy";

    public IEnumerable<InstalledApp> Scan(CancellationToken cancellationToken)
    {
        using var games = Registry.LocalMachine.OpenSubKey(GamesKey);
        if (games == null) yield break;

        foreach (string id in games.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstalledApp? app = null;
            try
            {
                using var key = games.OpenSubKey(id);
                if (key != null) app = InstalledAppParsers.ParseGogGame(v => key.GetValue(v)?.ToString());
            }
            catch { }
            if (app != null) yield return app;
        }
    }
}
