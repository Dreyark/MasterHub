using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MasterHub.Core.SystemControl;
using SkiaSharp;
using Svg.Skia;

namespace MasterHub.Core.Integrations.StreamDeck;

/// <summary>
/// Zarządca wtyczek Elgato Stream Deck (.streamDeckPlugin) w MasterHub.
/// Obsługuje instalację, rekonstrukcję szyfrowanych manifestów Elgato DRM, cykl życia procesów, WebSocket oraz konwersję klatek do 72x72 JPEG.
/// </summary>
public sealed class StreamDeckPluginHost : IStreamDeckPluginHost
{
    private readonly StreamDeckWebSocketServer _server;
    private readonly string _pluginsDirectory;
    private readonly string _elgatoPluginsDirectory;
    private readonly ConcurrentDictionary<string, StreamDeckInstalledPlugin> _plugins = new();
    private readonly ConcurrentDictionary<string, Process> _runningProcesses = new();
    private readonly ConcurrentDictionary<string, int> _contextToKeyMap = new();
    private readonly ConcurrentDictionary<int, string> _keyToContextMap = new();
    private readonly ConcurrentDictionary<int, (string PluginUuid, string ActionUuid, string? SettingsJson)> _activeKeyAssignments = new();
    private bool _isDisposed;

    public int Port => _server.Port;
    public bool IsRunning => _server.IsRunning;
    public IReadOnlyList<StreamDeckInstalledPlugin> InstalledPlugins => _plugins.Values.ToList();

    public event Action<int, byte[]>? KeyImageUpdated;
    public event Action<int, string>? KeyTitleUpdated;
    public event Action<int, int>? KeyStateUpdated;
    public event Action<int>? KeyAlertRequested;
    public event Action<int>? KeyOkRequested;
    public event Action<string>? PluginInstalled;
    public event Action<string, string>? PluginLogReceived;
    public event Action<int, string>? KeySettingsUpdated;
    public event Action<string, string>? GlobalSettingsUpdated;
    public event Action<string>? SwitchToProfileRequested;

    public StreamDeckPluginHost(int preferredPort = 28123, string? pluginsDirectory = null, string? elgatoPluginsDirectory = null)
    {
        _server = new StreamDeckWebSocketServer(preferredPort);
        _server.PluginFolderResolver = uuid => _plugins.TryGetValue(uuid, out var p) ? p.FolderPath : null;

        _pluginsDirectory = pluginsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MasterHub", "plugins");

        _elgatoPluginsDirectory = elgatoPluginsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Elgato", "StreamDeck", "Plugins");

        EnsureDirectoryExists(_pluginsDirectory);

        // Usuń pozostałości po testowym Sample Plugin jeśli istnieją
        try
        {
            string sampleDir = Path.Combine(_pluginsDirectory, "com.test.sample.sdPlugin");
            if (Directory.Exists(sampleDir)) Directory.Delete(sampleDir, true);
        }
        catch { }

        _server.PluginRegistered += OnPluginRegistered;
        _server.SetImageRequested += OnSetImageRequested;
        _server.SetTitleRequested += OnSetTitleRequested;
        _server.SetStateRequested += OnSetStateRequested;
        _server.ShowAlertRequested += OnShowAlertRequested;
        _server.ShowOkRequested += OnShowOkRequested;
        _server.OpenUrlRequested += OnOpenUrlRequested;
        _server.LogMessageReceived += (uuid, msg) => PluginLogReceived?.Invoke(uuid, msg);
        _server.SettingsUpdated += OnServerSettingsUpdated;
        _server.GlobalSettingsUpdated += (uuid, json) => GlobalSettingsUpdated?.Invoke(uuid, json);
        _server.SwitchToProfileRequested += prof => SwitchToProfileRequested?.Invoke(prof);
    }

    private void OnServerSettingsUpdated(string context, string settingsJson)
    {
        if (_contextToKeyMap.TryGetValue(context, out int keyIndex))
        {
            if (_activeKeyAssignments.TryGetValue(keyIndex, out var cur))
            {
                _activeKeyAssignments[keyIndex] = (cur.PluginUuid, cur.ActionUuid, settingsJson);
            }
            KeySettingsUpdated?.Invoke(keyIndex, settingsJson);
        }
    }

    private void OnPluginRegistered(string pluginUuid, string registerEvent)
    {
        foreach (var kvp in _activeKeyAssignments)
        {
            if (string.Equals(kvp.Value.PluginUuid, pluginUuid, StringComparison.OrdinalIgnoreCase))
            {
                _ = SendWillAppearAsync(kvp.Key, kvp.Value.PluginUuid, kvp.Value.ActionUuid, kvp.Value.SettingsJson);
            }
        }
    }

    private static void EnsureDirectoryExists(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
        catch { }
    }

    public void RefreshPlugins()
    {
        _plugins.Clear();

        // 1. Wtyczki MasterHub (%APPDATA%\MasterHub\plugins)
        ScanPluginsInDirectory(_pluginsDirectory);

        // 2. Oficjalne wtyczki Elgato Stream Deck (%APPDATA%\Elgato\StreamDeck\Plugins)
        if (Directory.Exists(_elgatoPluginsDirectory))
        {
            ScanPluginsInDirectory(_elgatoPluginsDirectory);
        }
    }

    private void ScanPluginsInDirectory(string baseDir)
    {
        try
        {
            var directories = Directory.GetDirectories(baseDir);
            foreach (var dir in directories)
            {
                string folderName = Path.GetFileName(dir);
                if (folderName.Contains("sample", StringComparison.OrdinalIgnoreCase) ||
                    folderName.Contains("com.test.sample", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string pluginUuid = folderName.EndsWith(".sdPlugin", StringComparison.OrdinalIgnoreCase)
                    ? folderName[..^9]
                    : folderName;

                if (pluginUuid.Equals("com.test.sample", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath) || IsEncryptedElgatoManifest(manifestPath))
                {
                    TryReconstructManifest(dir, pluginUuid);
                }

                if (!File.Exists(manifestPath)) continue;

                try
                {
                    string json = File.ReadAllText(manifestPath);
                    StreamDeckManifest? manifest = null;
                    try
                    {
                        manifest = JsonSerializer.Deserialize<StreamDeckManifest>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                    }
                    catch
                    {
                        manifest = TryReconstructManifest(dir, pluginUuid);
                    }

                    if (manifest == null) continue;

                    string? codePath = manifest.CodePathWin ?? manifest.CodePath;
                    if (string.IsNullOrWhiteSpace(codePath))
                    {
                        string[] jsCandidates = { Path.Combine("bin", "plugin.js"), "plugin.js", "index.js", Path.Combine("dist", "index.js") };
                        foreach (var js in jsCandidates)
                        {
                            if (File.Exists(Path.Combine(dir, js))) { codePath = js; break; }
                        }
                        if (string.IsNullOrWhiteSpace(codePath))
                        {
                            string[] htmlCandidates = { "code.html", "index.html", "plugin.html" };
                            foreach (var h in htmlCandidates)
                            {
                                if (File.Exists(Path.Combine(dir, h))) { codePath = h; break; }
                            }
                        }
                        if (string.IsNullOrWhiteSpace(codePath))
                        {
                            var exes = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories);
                            if (exes.Length > 0) codePath = Path.GetRelativePath(dir, exes[0]);
                        }
                    }

                    bool isHtml = !string.IsNullOrEmpty(codePath) && (codePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || codePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase));
                    bool isNode = !isHtml && (manifest.Nodejs != null || (codePath != null && codePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)));
                    string execPath = "";
                    string? htmlPath = null;

                    if (isHtml)
                    {
                        htmlPath = Path.Combine(dir, codePath!);
                    }
                    else if (!isNode && !string.IsNullOrEmpty(codePath))
                    {
                        execPath = Path.Combine(dir, codePath);
                    }

                    string? iconPath = null;
                    if (!string.IsNullOrEmpty(manifest.Icon))
                    {
                        string candidate = Path.Combine(dir, manifest.Icon);
                        if (File.Exists(candidate)) iconPath = candidate;
                        else if (File.Exists(candidate + ".png")) iconPath = candidate + ".png";
                        else if (File.Exists(candidate + "@2x.png")) iconPath = candidate + "@2x.png";
                        else if (File.Exists(candidate + ".svg")) iconPath = candidate + ".svg";
                    }

                    var installed = new StreamDeckInstalledPlugin
                    {
                        PluginUuid = pluginUuid,
                        Name = !string.IsNullOrEmpty(manifest.Name) ? manifest.Name : pluginUuid,
                        Author = manifest.Author,
                        Version = manifest.Version,
                        Description = manifest.Description,
                        FolderPath = dir,
                        ExecutablePath = execPath,
                        IconPath = iconPath,
                        IsNodeJs = isNode,
                        IsHtml = isHtml,
                        HtmlEntryPath = htmlPath,
                        IsRunning = _runningProcesses.ContainsKey(pluginUuid),
                        IsEnabled = true,
                        Manifest = manifest
                    };

                    _plugins[pluginUuid] = installed;
                }
                catch { }
            }
        }
        catch { }
    }

    public async Task<StreamDeckInstalledPlugin?> InstallPluginPackageAsync(string packageFilePath)
    {
        if (!File.Exists(packageFilePath)) return null;

        try
        {
            using var archive = ZipFile.OpenRead(packageFilePath);
            var manifestEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase));
            var enEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("en.json", StringComparison.OrdinalIgnoreCase));
            if (manifestEntry == null && enEntry == null) return null;

            var anchorEntry = manifestEntry ?? enEntry!;
            string entryFullName = anchorEntry.FullName.Replace('\\', '/');

            string targetDir;
            if (entryFullName.Contains('/'))
            {
                string topDirName = entryFullName[..entryFullName.IndexOf('/')];
                if (!topDirName.EndsWith(".sdPlugin", StringComparison.OrdinalIgnoreCase))
                {
                    topDirName += ".sdPlugin";
                }
                targetDir = Path.Combine(_pluginsDirectory, topDirName);
            }
            else
            {
                string safeName = Path.GetFileNameWithoutExtension(packageFilePath);
                if (!safeName.EndsWith(".sdPlugin", StringComparison.OrdinalIgnoreCase))
                {
                    safeName += ".sdPlugin";
                }
                targetDir = Path.Combine(_pluginsDirectory, safeName);
            }

            if (Directory.Exists(targetDir))
            {
                try { Directory.Delete(targetDir, true); } catch { }
            }
            Directory.CreateDirectory(targetDir);

            foreach (var entry in archive.Entries)
            {
                string entryRelPath = entry.FullName.Replace('\\', '/');
                if (entryFullName.Contains('/'))
                {
                    int slashIdx = entryRelPath.IndexOf('/');
                    if (slashIdx >= 0) entryRelPath = entryRelPath[(slashIdx + 1)..];
                }

                if (string.IsNullOrEmpty(entryRelPath) || entryRelPath.EndsWith('/')) continue;

                string fullDestPath = Path.Combine(targetDir, entryRelPath.Replace('/', Path.DirectorySeparatorChar));
                string? destDir = Path.GetDirectoryName(fullDestPath);
                if (destDir != null && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                entry.ExtractToFile(fullDestPath, overwrite: true);
            }

            string folderName = Path.GetFileName(targetDir);
            string pluginUuid = folderName.EndsWith(".sdPlugin", StringComparison.OrdinalIgnoreCase)
                ? folderName[..^9]
                : folderName;

            string manifestPath = Path.Combine(targetDir, "manifest.json");
            if (!File.Exists(manifestPath) || IsEncryptedElgatoManifest(manifestPath))
            {
                TryReconstructManifest(targetDir, pluginUuid);
            }

            RefreshPlugins();

            var installed = _plugins.Values.FirstOrDefault(p =>
                p.FolderPath.Equals(targetDir, StringComparison.OrdinalIgnoreCase) ||
                p.PluginUuid.Equals(pluginUuid, StringComparison.OrdinalIgnoreCase));

            if (installed != null)
            {
                PluginInstalled?.Invoke(installed.PluginUuid);
            }

            return await Task.FromResult(installed);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> UninstallPluginAsync(string pluginUuid)
    {
        if (_plugins.TryRemove(pluginUuid, out var plugin))
        {
            await StopPluginAsync(pluginUuid);
            try
            {
                if (Directory.Exists(plugin.FolderPath))
                {
                    Directory.Delete(plugin.FolderPath, true);
                }
                RefreshPlugins();
                return true;
            }
            catch { }
        }
        return false;
    }

    private static bool IsEncryptedElgatoManifest(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath)) return false;
            using var fs = File.OpenRead(manifestPath);
            if (fs.Length < 8) return false;
            byte[] header = new byte[8];
            int read = fs.Read(header, 0, 8);
            if (read >= 8 &&
                header[0] == 0x45 && header[1] == 0x4C && header[2] == 0x47 &&
                header[3] == 0x41 && header[4] == 0x54 && header[5] == 0x4F)
            {
                return true;
            }
        }
        catch { }
        return false;
    }

    private StreamDeckManifest? TryReconstructManifest(string pluginDir, string pluginUuid)
    {
        try
        {
            var manifest = new StreamDeckManifest
            {
                Name = "",
                Description = "",
                Category = "",
                Author = pluginUuid.StartsWith("com.elgato", StringComparison.OrdinalIgnoreCase) ? "Elgato" : "Stream Deck",
                Version = "1.0.0",
                SDKVersion = 2
            };

            // 1. Sprawdź pliki lokalizacyjne (en.json, de.json, etc.)
            string enPath = Path.Combine(pluginDir, "en.json");
            if (!File.Exists(enPath))
            {
                var candidateEn = Directory.GetFiles(pluginDir, "*.json")
                    .FirstOrDefault(f => !Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase) &&
                                         !Path.GetFileName(f).Equals("package.json", StringComparison.OrdinalIgnoreCase));
                if (candidateEn != null) enPath = candidateEn;
            }

            var actionsDict = new Dictionary<string, (string Name, string? Tooltip, string? Icon)>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(enPath))
            {
                try
                {
                    string json = File.ReadAllText(enPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("Name", out var nameProp)) manifest.Name = nameProp.GetString() ?? "";
                    if (root.TryGetProperty("Description", out var descProp)) manifest.Description = descProp.GetString() ?? "";
                    if (root.TryGetProperty("Category", out var catProp)) manifest.Category = catProp.GetString() ?? "";
                    if (root.TryGetProperty("Author", out var authProp)) manifest.Author = authProp.GetString() ?? manifest.Author;

                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Object && prop.Name.Contains('.'))
                        {
                            string actionUuid = prop.Name;
                            string? actName = prop.Value.TryGetProperty("Name", out var an) ? an.GetString() : null;
                            string? actTooltip = prop.Value.TryGetProperty("Tooltip", out var at) ? at.GetString() : null;

                            if (string.IsNullOrWhiteSpace(actName))
                            {
                                int lastDot = actionUuid.LastIndexOf('.');
                                actName = lastDot >= 0 ? FormatIdentifierToTitle(actionUuid[(lastDot + 1)..]) : actionUuid;
                            }

                            actionsDict[actionUuid] = (actName, actTooltip, null);
                        }
                    }
                }
                catch { }
            }

            // 2. Jeśli brakuje nazwy / kategorii, wyprowadź z pluginUuid
            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                int lastDot = pluginUuid.LastIndexOf('.');
                manifest.Name = lastDot >= 0 ? FormatIdentifierToTitle(pluginUuid[(lastDot + 1)..]) : pluginUuid;
            }
            if (string.IsNullOrWhiteSpace(manifest.Category))
            {
                manifest.Category = manifest.Name;
            }

            // 3. Sprawdź wersję z package.json
            string pkgPath = Path.Combine(pluginDir, "package.json");
            if (!File.Exists(pkgPath)) pkgPath = Path.Combine(pluginDir, "bin", "package.json");
            if (File.Exists(pkgPath))
            {
                try
                {
                    using var pkgDoc = JsonDocument.Parse(File.ReadAllText(pkgPath));
                    if (pkgDoc.RootElement.TryGetProperty("version", out var verProp))
                    {
                        manifest.Version = verProp.GetString() ?? manifest.Version;
                    }
                }
                catch { }
            }

            // 4. Wykryj punkt wejścia (CodePath, Nodejs, HTML, EXE)
            string[] jsCandidates = { Path.Combine("bin", "plugin.js"), "plugin.js", "index.js", Path.Combine("dist", "index.js") };
            foreach (var js in jsCandidates)
            {
                if (File.Exists(Path.Combine(pluginDir, js)))
                {
                    manifest.CodePath = js.Replace('\\', '/');
                    manifest.CodePathWin = js.Replace('\\', '/');
                    manifest.Nodejs = new StreamDeckNodejs { Version = "20" };
                    break;
                }
            }

            if (manifest.Nodejs == null)
            {
                string[] htmlCandidates = { "code.html", "index.html", "plugin.html" };
                foreach (var h in htmlCandidates)
                {
                    if (File.Exists(Path.Combine(pluginDir, h)))
                    {
                        manifest.CodePath = h;
                        manifest.CodePathWin = h;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(manifest.CodePath))
                {
                    var htmls = Directory.GetFiles(pluginDir, "*.html")
                        .Where(f => !Path.GetFileName(f).Contains("inspector", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).Contains("pi", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (htmls.Count > 0)
                    {
                        string relHtml = Path.GetRelativePath(pluginDir, htmls[0]).Replace('\\', '/');
                        manifest.CodePath = relHtml;
                        manifest.CodePathWin = relHtml;
                    }
                }
            }

            if (manifest.Nodejs == null && string.IsNullOrEmpty(manifest.CodePath))
            {
                var exes = Directory.GetFiles(pluginDir, "*.exe", SearchOption.AllDirectories);
                if (exes.Length > 0)
                {
                    string relExe = Path.GetRelativePath(pluginDir, exes[0]).Replace('\\', '/');
                    manifest.CodePath = relExe;
                    manifest.CodePathWin = relExe;
                }
            }

            // 5. Przeszukaj pliki js w poszukiwaniu dodatkowych UUID akcji (np. UUID:"com.elgato...")
            try
            {
                var jsFiles = Directory.GetFiles(pluginDir, "*.js", SearchOption.AllDirectories);
                foreach (var jsFile in jsFiles)
                {
                    string jsCode = File.ReadAllText(jsFile);
                    var matches = Regex.Matches(jsCode, @"UUID\s*:\s*[""'](?<uuid>[a-zA-Z0-9_\-\.]+)[""']");
                    foreach (Match m in matches)
                    {
                        string foundUuid = m.Groups["uuid"].Value;
                        if (foundUuid.Contains('.') && !actionsDict.ContainsKey(foundUuid))
                        {
                            int lastDot = foundUuid.LastIndexOf('.');
                            string actName = lastDot >= 0 ? FormatIdentifierToTitle(foundUuid[(lastDot + 1)..]) : foundUuid;
                            actionsDict[foundUuid] = (actName, null, null);
                        }
                    }
                }
            }
            catch { }

            // 6. Przeszukaj katalogi w imgs/actions/ oraz ea/
            string imgsActionsDir = Path.Combine(pluginDir, "imgs", "actions");
            if (Directory.Exists(imgsActionsDir))
            {
                foreach (var actionSubDir in Directory.GetDirectories(imgsActionsDir))
                {
                    string subDirName = Path.GetFileName(actionSubDir);
                    bool alreadyCovered = actionsDict.Keys.Any(k => k.EndsWith(subDirName, StringComparison.OrdinalIgnoreCase));
                    if (!alreadyCovered)
                    {
                        string candidateUuid = $"{pluginUuid}.{subDirName}";
                        actionsDict[candidateUuid] = (FormatIdentifierToTitle(subDirName), null, null);
                    }
                }
            }

            // 7. Znajdź ikonę główną wtyczki
            string[] pluginIconCandidates =
            {
                Path.Combine("imgs", "plugin", "marketplace.png"),
                Path.Combine("imgs", "plugin", "category-icon.png"),
                Path.Combine("imgs", "plugin", "category-icon.svg"),
                Path.Combine("pluginIcon.png"),
                Path.Combine("icon.png"),
                Path.Combine("imgs", "icon.png")
            };

            foreach (var cand in pluginIconCandidates)
            {
                if (File.Exists(Path.Combine(pluginDir, cand)))
                {
                    manifest.Icon = cand.Replace('\\', '/');
                    break;
                }
            }

            // 8. Znajdź ikony dla akcji
            foreach (var kv in actionsDict.ToList())
            {
                string actionUuid = kv.Key;
                string actName = kv.Value.Name;
                string? actTooltip = kv.Value.Tooltip;
                string? foundIcon = FindBestActionIcon(pluginDir, actionUuid, manifest.Icon);

                actionsDict[actionUuid] = (actName, actTooltip, foundIcon);
            }

            manifest.Actions = actionsDict.Select(kv => new StreamDeckActionManifest
            {
                UUID = kv.Key,
                Name = kv.Value.Name,
                Tooltip = kv.Value.Tooltip,
                Icon = kv.Value.Icon ?? manifest.Icon
            }).ToList();

            // Zapisz zrekonstruowany manifest.json na dysk
            string outManifestPath = Path.Combine(pluginDir, "manifest.json");
            string reconstructedJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(outManifestPath, reconstructedJson);

            return manifest;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatIdentifierToTitle(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return identifier;
        var parts = identifier.Split(new[] { '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i][1..].ToLowerInvariant();
            }
        }
        return string.Join(" ", parts);
    }

    private static string? FindBestActionIcon(string pluginDir, string actionUuid, string fallbackPluginIcon)
    {
        int lastDot = actionUuid.LastIndexOf('.');
        string shortName = lastDot >= 0 ? actionUuid[(lastDot + 1)..] : actionUuid;

        string[] iconCandidatePaths =
        {
            Path.Combine("imgs", "actions", shortName, "icon.svg"),
            Path.Combine("imgs", "actions", shortName, "icon.png"),
            Path.Combine("imgs", "actions", shortName, $"{shortName}.svg"),
            Path.Combine("imgs", "actions", shortName, $"{shortName}.png"),
            Path.Combine("imgs", "actions", $"{shortName}.svg"),
            Path.Combine("imgs", "actions", $"{shortName}.png"),
            Path.Combine("imgs", $"{shortName}.png")
        };

        foreach (var relCand in iconCandidatePaths)
        {
            if (File.Exists(Path.Combine(pluginDir, relCand)))
            {
                return relCand.Replace('\\', '/');
            }
        }

        var nameMappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["next"] = new[] { Path.Combine("imgs", "actions", "slides", "next-slide.svg"), Path.Combine("imgs", "actions", "slides", "next-slide.png") },
            ["prev"] = new[] { Path.Combine("imgs", "actions", "slides", "previous-slide.svg"), Path.Combine("imgs", "actions", "slides", "previous-slide.png") },
            ["first"] = new[] { Path.Combine("imgs", "actions", "slides", "first-slide.svg"), Path.Combine("imgs", "actions", "slides", "first-slide.png") },
            ["last"] = new[] { Path.Combine("imgs", "actions", "slides", "last-slide.svg"), Path.Combine("imgs", "actions", "slides", "last-slide.png") },
            ["slide-navigation"] = new[] { Path.Combine("imgs", "actions", "slides", "icon.svg"), Path.Combine("imgs", "actions", "slides", "icon.png") },
            ["start-stop-slideshow"] = new[] { Path.Combine("imgs", "actions", "slideshow", "start-stop-slideshow.svg"), Path.Combine("imgs", "actions", "slideshow", "icon.svg") },
            ["play"] = new[] { Path.Combine("imgs", "actions", "slideshow", "start-stop-slideshow.svg"), Path.Combine("imgs", "actions", "slideshow", "icon.svg") },
            ["load"] = new[] { Path.Combine("imgs", "actions", "slideshow", "icon.svg"), Path.Combine("imgs", "actions", "slideshow", "icon.png") },
            ["toggle-slide-navigate"] = new[] { Path.Combine("imgs", "actions", "presentation-overview", "icon.svg"), Path.Combine("imgs", "actions", "presentation-overview", "icon.png") }
        };

        if (nameMappings.TryGetValue(shortName, out var mappedCandidates))
        {
            foreach (var cand in mappedCandidates)
            {
                if (File.Exists(Path.Combine(pluginDir, cand)))
                {
                    return cand.Replace('\\', '/');
                }
            }
        }

        string imgsActionsDir = Path.Combine(pluginDir, "imgs", "actions");
        if (Directory.Exists(imgsActionsDir))
        {
            try
            {
                var files = Directory.GetFiles(imgsActionsDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var match = files.FirstOrDefault(f => Path.GetFileName(f).Contains(shortName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    return Path.GetRelativePath(pluginDir, match).Replace('\\', '/');
                }
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(fallbackPluginIcon) && File.Exists(Path.Combine(pluginDir, fallbackPluginIcon)))
        {
            return fallbackPluginIcon;
        }

        return null;
    }

    public Task StartAsync()
    {
        if (!_server.IsRunning)
        {
            _server.Start();
        }

        KillOrphanedPluginProcesses();

        RefreshPlugins();

        return Task.CompletedTask;
    }

    private void KillOrphanedPluginProcesses()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'node.exe'");
            using var objects = searcher.Get();
            foreach (ManagementObject obj in objects)
            {
                try
                {
                    string? cmdLine = obj["CommandLine"]?.ToString();
                    if (!string.IsNullOrEmpty(cmdLine))
                    {
                        bool isPluginProcess = cmdLine.Contains("MasterHub", StringComparison.OrdinalIgnoreCase) ||
                                               cmdLine.Contains(".sdPlugin", StringComparison.OrdinalIgnoreCase) ||
                                               cmdLine.Contains("-pluginUUID", StringComparison.OrdinalIgnoreCase);

                        if (isPluginProcess && uint.TryParse(obj["ProcessId"]?.ToString(), out uint pid))
                        {
                            try
                            {
                                var proc = Process.GetProcessById((int)pid);
                                if (!proc.HasExited)
                                {
                                    proc.Kill(true);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    public async Task StopAsync()
    {
        foreach (var uuid in _runningProcesses.Keys.ToList())
        {
            await StopPluginAsync(uuid);
        }
        _server.Dispose();
    }

    public async Task StartPluginAsync(string pluginUuid)
    {
        if (!_server.IsRunning)
        {
            _server.Start();
        }

        if (_server.IsPluginConnected(pluginUuid)) return;
        if (!_plugins.TryGetValue(pluginUuid, out var plugin)) return;

        if (!_runningProcesses.ContainsKey(pluginUuid))
        {
            string appInfoJson = JsonSerializer.Serialize(new
            {
                application = new
                {
                    font = "Segoe UI",
                    language = "en",
                    platform = "windows",
                    platformVersion = Environment.OSVersion.Version.ToString(),
                    version = "6.5.0"
                },
                plugin = new
                {
                    uuid = pluginUuid,
                    version = plugin.Version
                },
                devicePixelRatio = 2,
                devices = new[]
                {
                    new
                    {
                        id = "masterhub-keydeck-15",
                        name = "MasterHub Keydeck 15",
                        size = new { columns = 5, rows = 3 },
                        type = 0
                    },
                    new
                    {
                        id = "masterhub-keydeck-plus",
                        name = "MasterHub Keydeck Plus",
                        size = new { columns = 4, rows = 2 },
                        type = 7
                    }
                }
            });

            string escapedInfo = appInfoJson.Replace("\"", "\\\"");
            string cliArgs = $"-port {Port} -pluginUUID \"{pluginUuid}\" -registerEvent registerPlugin -info \"{escapedInfo}\"";

            ProcessStartInfo psi;

            if (plugin.IsNodeJs)
            {
                string? nodePath = FindNodeExecutable();
                if (nodePath == null)
                {
                    PluginLogReceived?.Invoke(pluginUuid, "Błąd: Node.js (node.exe) nie został znaleziony w systemie do uruchomienia tej wtyczki.");
                    return;
                }

                string? scriptPath = plugin.Manifest.CodePathWin ?? plugin.Manifest.CodePath;
                string fullScriptPath = Path.Combine(plugin.FolderPath, scriptPath ?? "index.js");

                psi = new ProcessStartInfo
                {
                    FileName = nodePath,
                    Arguments = $"\"{fullScriptPath}\" {cliArgs}",
                    WorkingDirectory = plugin.FolderPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
            }
            else if (plugin.IsHtml)
            {
                string? browserPath = FindBrowserExecutable();
                if (browserPath == null)
                {
                    PluginLogReceived?.Invoke(pluginUuid, "Błąd: Nie znaleziono przeglądarki Edge ani Chrome do uruchomienia wtyczki HTML.");
                    return;
                }

                string entryFile = !string.IsNullOrEmpty(plugin.HtmlEntryPath) ? Path.GetFileName(plugin.HtmlEntryPath) : "code.html";
                string runnerPath = Path.Combine(plugin.FolderPath, "_masterhub_runner.html");

                string runnerHtml = $@"<!DOCTYPE html>
<html>
<head><meta charset=""utf-8""></head>
<body>
<iframe id=""pluginFrame"" src=""{entryFile}"" style=""display:none;width:0;height:0;border:0;""></iframe>
<script>
var frame = document.getElementById('pluginFrame');
var connected = false;
function initPlugin() {{
    if (connected) return;
    try {{
        var win = frame.contentWindow;
        if (win && typeof win.connectElgatoStreamDeckSocket === 'function') {{
            connected = true;
            win.connectElgatoStreamDeckSocket({Port}, '{pluginUuid}', 'registerPlugin', {JsonSerializer.Serialize(appInfoJson)});
            return;
        }}
    }} catch (e) {{}}
    setTimeout(initPlugin, 100);
}}
frame.onload = initPlugin;
setTimeout(initPlugin, 250);
</script>
</body>
</html>";
                try
                {
                    File.WriteAllText(runnerPath, runnerHtml);
                }
                catch { }

                string tempUserData = Path.Combine(Path.GetTempPath(), "MasterHub", "edge_sd_plugins", pluginUuid);
                try { Directory.CreateDirectory(tempUserData); } catch { }

                string runnerUrl = $"http://127.0.0.1:{Port}/plugin/{pluginUuid}/_masterhub_runner.html";
                string args = $"--headless --disable-gpu --remote-debugging-port=0 --user-data-dir=\"{tempUserData}\" --no-first-run --no-default-browser-check \"{runnerUrl}\"";

                psi = new ProcessStartInfo
                {
                    FileName = browserPath,
                    Arguments = args,
                    WorkingDirectory = plugin.FolderPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
            }
            else
            {
                if (!File.Exists(plugin.ExecutablePath))
                {
                    return;
                }

                psi = new ProcessStartInfo
                {
                    FileName = plugin.ExecutablePath,
                    Arguments = cliArgs,
                    WorkingDirectory = plugin.FolderPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
            }

            try
            {
                var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) PluginLogReceived?.Invoke(pluginUuid, e.Data);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data)) PluginLogReceived?.Invoke(pluginUuid, $"[ERR] {e.Data}");
                };
                process.Exited += (_, _) =>
                {
                    _runningProcesses.TryRemove(pluginUuid, out _);
                    plugin.IsRunning = false;
                };

                if (process.Start())
                {
                    ChildProcessTracker.AddProcess(process);
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    _runningProcesses[pluginUuid] = process;
                    plugin.IsRunning = true;
                }
            }
            catch (Exception ex)
            {
                PluginLogReceived?.Invoke(pluginUuid, $"Wyjątek podczas uruchamiania wtyczki: {ex.Message}");
            }
        }

        // Poczekaj na połączenie i rejestrację wtyczki przez WebSocket (do 3 sekund)
        var sw = Stopwatch.StartNew();
        while (!_server.IsPluginConnected(pluginUuid) && sw.ElapsedMilliseconds < 3000)
        {
            await Task.Delay(50);
        }
    }

    public async Task StopPluginAsync(string pluginUuid)
    {
        try
        {
            await _server.ClosePluginSocketAsync(pluginUuid);
        }
        catch { }

        if (_runningProcesses.TryRemove(pluginUuid, out var proc))
        {
            try
            {
                if (!proc.HasExited)
                {
                    if (!proc.WaitForExit(150))
                    {
                        proc.Kill(true);
                    }
                }
                proc.Dispose();
            }
            catch { }
        }

        if (_plugins.TryGetValue(pluginUuid, out var plugin))
        {
            plugin.IsRunning = false;
            try
            {
                string runnerPath = Path.Combine(plugin.FolderPath, "_masterhub_runner.html");
                if (File.Exists(runnerPath)) File.Delete(runnerPath);
            }
            catch { }
        }
    }

    private static string? FindBrowserExecutable()
    {
        string[] paths =
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };

        return paths.FirstOrDefault(File.Exists);
    }

    private static string? FindNodeExecutable()
    {
        string[] commonPaths =
        {
            @"C:\nvm4w\nodejs\node.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "node", "node.exe")
        };

        foreach (var p in commonPaths)
        {
            if (File.Exists(p)) return p;
        }

        string? nvmSymlink = Environment.GetEnvironmentVariable("NVM_SYMLINK");
        if (!string.IsNullOrEmpty(nvmSymlink))
        {
            string cand = Path.Combine(nvmSymlink, "node.exe");
            if (File.Exists(cand)) return cand;
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var part in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = Path.Combine(part, "node.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    public async Task SendWillAppearAsync(int keyIndex, string pluginUuid, string actionUuid, string? settingsJson = null)
    {
        string context = $"key_{keyIndex}";
        _contextToKeyMap[context] = keyIndex;
        _keyToContextMap[keyIndex] = context;
        _activeKeyAssignments[keyIndex] = (pluginUuid, actionUuid, settingsJson);
        _server.AssociateContext(context, pluginUuid, actionUuid, settingsJson);

        bool showTitle = true;
        if (_plugins.TryGetValue(pluginUuid, out var plugin))
        {
            var actionManifest = plugin.Manifest.Actions.FirstOrDefault(a => a.UUID.Equals(actionUuid, StringComparison.OrdinalIgnoreCase));
            if (actionManifest != null && actionManifest.States.Count > 0)
            {
                showTitle = actionManifest.States[0].ShowTitle;
            }
        }

        var msg = new
        {
            @event = "willAppear",
            action = actionUuid,
            context = context,
            device = "masterhub-keydeck-15",
            payload = new
            {
                controller = "Keypad",
                settings = ParseJsonOrEmpty(settingsJson),
                coordinates = new { column = keyIndex % 5, row = keyIndex / 5 },
                state = 0,
                isInMultiAction = false,
                title = "",
                titleParameters = new
                {
                    fontFamily = "",
                    fontSize = 9,
                    fontStyle = "",
                    fontUnderline = false,
                    showTitle = showTitle,
                    titleAlignment = "bottom",
                    titleColor = "#ffffff"
                }
            }
        };

        await _server.SendEventAsync(pluginUuid, msg);
    }

    public async Task SendWillDisappearAsync(int keyIndex, string pluginUuid, string actionUuid)
    {
        _activeKeyAssignments.TryRemove(keyIndex, out _);
        string context = $"key_{keyIndex}";
        var msg = new
        {
            @event = "willDisappear",
            action = actionUuid,
            context = context,
            device = "masterhub-keydeck-15",
            payload = new
            {
                controller = "Keypad",
                coordinates = new { column = keyIndex % 5, row = keyIndex / 5 },
                state = 0,
                isInMultiAction = false
            }
        };

        await _server.SendEventAsync(pluginUuid, msg);
    }

    public async Task SendKeyDownAsync(int keyIndex, string pluginUuid, string actionUuid, string? settingsJson = null)
    {
        string context = $"key_{keyIndex}";
        var msg = new
        {
            @event = "keyDown",
            action = actionUuid,
            context = context,
            device = "masterhub-keydeck-15",
            payload = new
            {
                controller = "Keypad",
                settings = ParseJsonOrEmpty(settingsJson),
                coordinates = new { column = keyIndex % 5, row = keyIndex / 5 },
                state = 0,
                userDesiredState = 0,
                isInMultiAction = false
            }
        };

        await _server.SendEventAsync(pluginUuid, msg);
    }

    public async Task SendKeyUpAsync(int keyIndex, string pluginUuid, string actionUuid, string? settingsJson = null)
    {
        string context = $"key_{keyIndex}";
        var msg = new
        {
            @event = "keyUp",
            action = actionUuid,
            context = context,
            device = "masterhub-keydeck-15",
            payload = new
            {
                controller = "Keypad",
                settings = ParseJsonOrEmpty(settingsJson),
                coordinates = new { column = keyIndex % 5, row = keyIndex / 5 },
                state = 0,
                userDesiredState = 0,
                isInMultiAction = false
            }
        };

        await _server.SendEventAsync(pluginUuid, msg);
    }

    public async Task SendDialRotateAsync(int dialIndex, string pluginUuid, string actionUuid, int ticks, bool pressed, string? settingsJson = null)
    {
        string context = $"dial_{dialIndex}";
        await _server.SendDialRotateAsync(pluginUuid, context, actionUuid, ticks, pressed, ParseJsonOrEmpty(settingsJson));
    }

    public async Task SendDialPressAsync(int dialIndex, string pluginUuid, string actionUuid, bool isDown, string? settingsJson = null)
    {
        string context = $"dial_{dialIndex}";
        await _server.SendDialPressAsync(pluginUuid, context, actionUuid, isDown, ParseJsonOrEmpty(settingsJson));
    }

    public async Task SendTouchTapAsync(int dialIndex, string pluginUuid, string actionUuid, float x, float y, bool hold, string? settingsJson = null)
    {
        string context = $"dial_{dialIndex}";
        await _server.SendTouchTapAsync(pluginUuid, context, actionUuid, x, y, hold, ParseJsonOrEmpty(settingsJson));
    }

    public async Task SendTitleParametersDidChangeAsync(int keyIndex, string pluginUuid, string actionUuid, string title, string? font = null, int? fontSize = null, string? fontColor = null)
    {
        string context = $"key_{keyIndex}";
        await _server.SendTitleParametersDidChangeAsync(pluginUuid, context, actionUuid, title, font, fontSize, fontColor);
    }

    public async Task SendDeviceDidConnectAsync(string deviceId, string deviceName, int type, int cols, int rows)
    {
        foreach (var plugin in _plugins.Values)
        {
            await _server.SendDeviceDidConnectAsync(plugin.PluginUuid, deviceId, deviceName, type, cols, rows);
        }
    }

    public async Task SendDeviceDidDisconnectAsync(string deviceId)
    {
        foreach (var plugin in _plugins.Values)
        {
            await _server.SendDeviceDidDisconnectAsync(plugin.PluginUuid, deviceId);
        }
    }

    public async Task SendSystemDidWakeUpAsync()
    {
        foreach (var plugin in _plugins.Values)
        {
            await _server.SendSystemDidWakeUpAsync(plugin.PluginUuid);
        }
    }

    public async Task SendApplicationDidLaunchAsync(string application)
    {
        foreach (var plugin in _plugins.Values)
        {
            await _server.SendApplicationDidLaunchAsync(plugin.PluginUuid, application);
        }
    }

    public async Task SendApplicationDidTerminateAsync(string application)
    {
        foreach (var plugin in _plugins.Values)
        {
            await _server.SendApplicationDidTerminateAsync(plugin.PluginUuid, application);
        }
    }

    private static object ParseJsonOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new { };
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return new { };
        }
    }

    private int ResolveContextToKey(string context)
    {
        if (_contextToKeyMap.TryGetValue(context, out int mappedIdx))
        {
            return mappedIdx;
        }
        if (context.StartsWith("key_") && int.TryParse(context.AsSpan(4), out int parsedIdx))
        {
            return parsedIdx;
        }
        return -1;
    }

    private void OnSetImageRequested(string context, string? imageString, string? pluginUuid)
    {
        if (string.IsNullOrEmpty(imageString)) return;

        int keyIndex = ResolveContextToKey(context);
        if (keyIndex < 0 || keyIndex >= 15) return;

        try
        {
            byte[]? jpegBytes = ConvertToKeyJpeg(imageString, pluginUuid);
            if (jpegBytes != null && jpegBytes.Length > 0)
            {
                KeyImageUpdated?.Invoke(keyIndex, jpegBytes);
            }
        }
        catch { }
    }

    private byte[]? ConvertToKeyJpeg(string imageString, string? pluginUuid)
    {
        try
        {
            SKBitmap? sourceBmp = null;
            SKSvg? svg = null;

            string trimmed = imageString.Trim();

            if (trimmed.StartsWith("data:image/svg+xml", StringComparison.OrdinalIgnoreCase))
            {
                int commaIdx = trimmed.IndexOf(',');
                if (commaIdx >= 0)
                {
                    string header = trimmed[..commaIdx];
                    string rawData = trimmed[(commaIdx + 1)..];
                    string svgXml;
                    if (header.Contains(";base64", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            byte[] bytes = Convert.FromBase64String(rawData);
                            svgXml = Encoding.UTF8.GetString(bytes);
                        }
                        catch
                        {
                            svgXml = Uri.UnescapeDataString(rawData);
                        }
                    }
                    else
                    {
                        svgXml = Uri.UnescapeDataString(rawData);
                    }
                    svg = new SKSvg();
                    svg.FromSvg(svgXml);
                }
            }
            else if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                int commaIdx = trimmed.IndexOf(',');
                if (commaIdx >= 0)
                {
                    string base64Data = trimmed[(commaIdx + 1)..];
                    byte[] rawBytes = Convert.FromBase64String(base64Data);
                    sourceBmp = SKBitmap.Decode(rawBytes);
                }
            }
            else if (trimmed.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("<svg"))
            {
                svg = new SKSvg();
                svg.FromSvg(trimmed);
            }
            else if (File.Exists(trimmed))
            {
                if (trimmed.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    svg = new SKSvg();
                    svg.Load(trimmed);
                }
                else
                {
                    sourceBmp = SKBitmap.Decode(trimmed);
                }
            }
            else if (!string.IsNullOrEmpty(pluginUuid) && _plugins.TryGetValue(pluginUuid, out var plugin))
            {
                string[] candidates =
                {
                    Path.Combine(plugin.FolderPath, trimmed),
                    Path.Combine(plugin.FolderPath, trimmed + ".png"),
                    Path.Combine(plugin.FolderPath, trimmed + "@2x.png"),
                    Path.Combine(plugin.FolderPath, trimmed + ".svg"),
                    Path.Combine(plugin.FolderPath, trimmed + "@2x.svg")
                };

                foreach (var cand in candidates)
                {
                    if (File.Exists(cand))
                    {
                        if (cand.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                        {
                            svg = new SKSvg();
                            svg.Load(cand);
                        }
                        else
                        {
                            sourceBmp = SKBitmap.Decode(cand);
                        }
                        break;
                    }
                }
            }

            if (sourceBmp == null && svg?.Picture == null)
            {
                // Fallback: próba zdekodowania czystego base64
                try
                {
                    byte[] rawBytes = Convert.FromBase64String(trimmed);
                    sourceBmp = SKBitmap.Decode(rawBytes);
                    if (sourceBmp == null)
                    {
                        string asText = Encoding.UTF8.GetString(rawBytes);
                        if (asText.Contains("<svg", StringComparison.OrdinalIgnoreCase))
                        {
                            svg = new SKSvg();
                            svg.FromSvg(asText);
                        }
                    }
                }
                catch { }
            }

            if (sourceBmp == null && svg?.Picture == null) return null;

            using (sourceBmp)
            using (var surface = SKSurface.Create(new SKImageInfo(72, 72, SKColorType.Rgba8888, SKAlphaType.Premul)))
            {
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Black);

                if (sourceBmp != null)
                {
                    var destRect = new SKRect(0, 0, 72, 72);
                    using var paint = new SKPaint { IsAntialias = true };
                    canvas.DrawBitmap(sourceBmp, destRect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
                }
                else if (svg?.Picture != null)
                {
                    float cullW = svg.Picture.CullRect.Width;
                    float cullH = svg.Picture.CullRect.Height;
                    if (cullW > 0 && cullH > 0)
                    {
                        float scale = Math.Min(72f / cullW, 72f / cullH);
                        float dx = (72f - cullW * scale) / 2f;
                        float dy = (72f - cullH * scale) / 2f;
                        canvas.Translate(dx, dy);
                        canvas.Scale(scale);
                    }
                    canvas.DrawPicture(svg.Picture);
                }

                using var snapshot = surface.Snapshot();
                using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, 90);
                return encoded.ToArray();
            }
        }
        catch
        {
            return null;
        }
    }

    private void OnSetTitleRequested(string context, string? title)
    {
        int keyIndex = ResolveContextToKey(context);
        if (keyIndex >= 0 && keyIndex < 15)
        {
            KeyTitleUpdated?.Invoke(keyIndex, title ?? "");
        }
    }

    private void OnSetStateRequested(string context, int state)
    {
        int keyIndex = ResolveContextToKey(context);
        if (keyIndex >= 0 && keyIndex < 15)
        {
            KeyStateUpdated?.Invoke(keyIndex, state);
        }
    }

    private void OnShowAlertRequested(string context)
    {
        int keyIndex = ResolveContextToKey(context);
        if (keyIndex >= 0 && keyIndex < 15)
        {
            KeyAlertRequested?.Invoke(keyIndex);
        }
    }

    private void OnShowOkRequested(string context)
    {
        int keyIndex = ResolveContextToKey(context);
        if (keyIndex >= 0 && keyIndex < 15)
        {
            KeyOkRequested?.Invoke(keyIndex);
        }
    }

    private void OnOpenUrlRequested(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { }
        }
    }

    public StreamDeckActionManifest? GetActionManifest(string pluginUuid, string actionUuid)
    {
        if (_plugins.TryGetValue(pluginUuid, out var plugin))
        {
            return plugin.Manifest.Actions.FirstOrDefault(a => a.UUID.Equals(actionUuid, StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }

    public StreamDeckInstalledPlugin? GetPlugin(string pluginUuid)
    {
        _plugins.TryGetValue(pluginUuid, out var plugin);
        return plugin;
    }

    public string? GetPropertyInspectorPath(string pluginUuid, string? actionUuid = null)
    {
        if (!_plugins.TryGetValue(pluginUuid, out var plugin)) return null;

        if (!string.IsNullOrEmpty(actionUuid))
        {
            var action = plugin.Manifest?.Actions?.FirstOrDefault(a => string.Equals(a.UUID, actionUuid, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(action?.PropertyInspectorPath))
            {
                string cand = Path.Combine(plugin.FolderPath, action.PropertyInspectorPath);
                if (File.Exists(cand)) return action.PropertyInspectorPath.Replace('\\', '/');
            }
        }

        if (!string.IsNullOrEmpty(plugin.Manifest?.PropertyInspectorPath))
        {
            string cand = Path.Combine(plugin.FolderPath, plugin.Manifest.PropertyInspectorPath);
            if (File.Exists(cand)) return plugin.Manifest.PropertyInspectorPath.Replace('\\', '/');
        }

        string[] commonCandidates = { "pi/pi.html", "pi.html", "propertyInspector.html", "propertyinspector.html", "index.html" };
        foreach (var c in commonCandidates)
        {
            if (File.Exists(Path.Combine(plugin.FolderPath, c))) return c;
        }

        return null;
    }

    public bool OpenPropertyInspector(int keyIndex, string pluginUuid, string actionUuid, string? currentSettingsJson = null)
    {
        string? piRel = GetPropertyInspectorPath(pluginUuid, actionUuid);
        if (string.IsNullOrEmpty(piRel)) return false;

        if (!_server.IsRunning)
        {
            _server.Start();
        }

        _ = StartPluginAsync(pluginUuid);

        string context = keyIndex >= 0 ? $"key_{keyIndex}" : $"preview_{Guid.NewGuid():N}";
        _server.AssociateContext(context, pluginUuid, actionUuid, currentSettingsJson);

        if (keyIndex >= 0)
        {
            _contextToKeyMap[context] = keyIndex;
            _keyToContextMap[keyIndex] = context;
            if (_activeKeyAssignments.TryGetValue(keyIndex, out var cur))
            {
                _activeKeyAssignments[keyIndex] = (cur.PluginUuid, cur.ActionUuid, currentSettingsJson ?? cur.SettingsJson);
            }
            else
            {
                _activeKeyAssignments[keyIndex] = (pluginUuid, actionUuid, currentSettingsJson);
            }
        }

        string url = $"http://127.0.0.1:{Port}/plugin/{pluginUuid}/{piRel}?context={context}&action={Uri.EscapeDataString(actionUuid)}";

        return LaunchPropertyInspectorWindow(url);
    }

    public bool OpenGlobalPropertyInspector(string pluginUuid)
    {
        if (!_plugins.TryGetValue(pluginUuid, out var plugin)) return false;
        string? defaultAction = plugin.Manifest?.Actions?.FirstOrDefault()?.UUID ?? pluginUuid;
        return OpenPropertyInspector(-1, pluginUuid, defaultAction, null);
    }

    public void OpenPluginFolder(string pluginUuid)
    {
        if (_plugins.TryGetValue(pluginUuid, out var plugin) && Directory.Exists(plugin.FolderPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = plugin.FolderPath, UseShellExecute = true });
            }
            catch { }
        }
    }

    private static bool LaunchPropertyInspectorWindow(string url)
    {
        try
        {
            string? browser = FindBrowserExecutable();
            if (!string.IsNullOrEmpty(browser))
            {
                string tempProfile = Path.Combine(Path.GetTempPath(), "MasterHub", "sd_inspector_profile");
                try { Directory.CreateDirectory(tempProfile); } catch { }

                var psi = new ProcessStartInfo
                {
                    FileName = browser,
                    Arguments = $"--app=\"{url}\" --window-size=520,680 --user-data-dir=\"{tempProfile}\" --no-first-run --no-default-browser-check",
                    UseShellExecute = false
                };
                var proc = Process.Start(psi);
                if (proc != null) ChildProcessTracker.AddProcess(proc);
                return true;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task UpdateKeySettingsAsync(int keyIndex, string pluginUuid, string actionUuid, string settingsJson)
    {
        string context = _keyToContextMap.TryGetValue(keyIndex, out var ctx) ? ctx : $"key_{keyIndex}";
        _contextToKeyMap[context] = keyIndex;
        _keyToContextMap[keyIndex] = context;

        if (_activeKeyAssignments.TryGetValue(keyIndex, out var cur))
        {
            _activeKeyAssignments[keyIndex] = (cur.PluginUuid, cur.ActionUuid, settingsJson);
        }
        else
        {
            _activeKeyAssignments[keyIndex] = (pluginUuid, actionUuid, settingsJson);
        }

        await _server.UpdateKeySettingsAsync(context, pluginUuid, actionUuid, settingsJson);
    }

    public async Task UpdateGlobalSettingsAsync(string pluginUuid, string globalSettingsJson)
    {
        await _server.UpdateGlobalSettingsAsync(pluginUuid, globalSettingsJson);
    }

    public string? GetGlobalSettings(string pluginUuid) => _server.GetGlobalSettings(pluginUuid);

    public string? GetKeySettings(int keyIndex)
    {
        string context = _keyToContextMap.TryGetValue(keyIndex, out var ctx) ? ctx : $"key_{keyIndex}";
        return _server.GetKeySettings(context);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        foreach (var p in _runningProcesses.Values)
        {
            try
            {
                if (!p.HasExited) p.Kill(true);
                p.Dispose();
            }
            catch { }
        }
        _runningProcesses.Clear();
        _server.Dispose();
    }
}
