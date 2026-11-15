using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MasterHub.Core.Apps;

/// <summary>Minimal Valve KeyValues (VDF/ACF) reader: nested <c>"key" "value"</c> / <c>"key" { … }</c> blocks.</summary>
public static class VdfParser
{
    /// <summary>Parses text into nested dictionaries (values are <c>string</c> or <c>Dictionary&lt;string, object&gt;</c>), keys case-insensitive.</summary>
    public static Dictionary<string, object> Parse(string text)
    {
        var tokens = Tokenize(text ?? "");
        int pos = 0;
        return ReadBlock(tokens, ref pos, topLevel: true);
    }

    private static Dictionary<string, object> ReadBlock(List<string> tokens, ref int pos, bool topLevel)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (pos < tokens.Count)
        {
            string key = tokens[pos++];
            if (key == "}")
            {
                if (topLevel) continue; // stray brace: ignore
                return result;
            }
            if (key == "{") continue;
            if (pos >= tokens.Count) break;

            if (tokens[pos] == "{")
            {
                pos++;
                result[key] = ReadBlock(tokens, ref pos, topLevel: false);
            }
            else
            {
                string raw = tokens[pos++];
                result[key] = raw.StartsWith('\u0001') ? raw[1..] : raw;
            }
        }
        return result;
    }

    // Quoted strings are returned as-is; braces become the bare tokens "{" and "}" (a quoted "{" is prefixed so it cannot collide).
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c is '{' or '}') { tokens.Add(c.ToString()); i++; continue; }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        char n = text[i + 1];
                        sb.Append(n switch { 'n' => '\n', 't' => '\t', _ => n });
                        i += 2;
                    }
                    else sb.Append(text[i++]);
                }
                i++; // closing quote
                string value = sb.ToString();
                tokens.Add(value is "{" or "}" ? "\u0001" + value : value);
                continue;
            }
            // unquoted token
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"')) i++;
            tokens.Add(text[start..i]);
        }
        return tokens;
    }
}

/// <summary>Parsers of the launchers' own metadata files (all pure: text in, apps out, so they are testable with fixtures).</summary>
public static class InstalledAppParsers
{
    /// <summary>Library folders from Steam's <c>libraryfolders.vdf</c> (new and old layout).</summary>
    public static IReadOnlyList<string> ParseSteamLibraryFolders(string vdf)
    {
        var root = VdfParser.Parse(vdf);
        var paths = new List<string>();
        if (!root.TryGetValue("libraryfolders", out var node) || node is not Dictionary<string, object> folders) return paths;

        foreach (var (key, value) in folders)
        {
            if (!key.All(char.IsDigit)) continue;
            if (value is string oldStyle) paths.Add(oldStyle);
            else if (value is Dictionary<string, object> block && block.TryGetValue("path", out var p) && p is string path) paths.Add(path);
        }
        return paths;
    }

    /// <summary>One game from <c>appmanifest_*.acf</c>; <c>null</c> for tools/redistributables or unfinished installs.</summary>
    public static InstalledApp? ParseSteamManifest(string acf, string? coverPath = null)
    {
        var root = VdfParser.Parse(acf);
        if (!root.TryGetValue("AppState", out var node) || node is not Dictionary<string, object> state) return null;
        if (!state.TryGetValue("appid", out var idObj) || idObj is not string appId || appId.Length == 0 || !appId.All(char.IsDigit)) return null;
        if (!state.TryGetValue("name", out var nameObj) || nameObj is not string name || string.IsNullOrWhiteSpace(name)) return null;

        // StateFlags bit 4 = fully installed. Missing field: assume installed.
        if (state.TryGetValue("StateFlags", out var flagsObj) && flagsObj is string flagsText
            && int.TryParse(flagsText, out int flags) && (flags & 4) == 0) return null;

        if (name.StartsWith("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Proton ", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase)) return null;

        return new InstalledApp($"steam:{appId}", name.Trim(), InstalledAppKind.Steam, $"steam://rungameid/{appId}")
        {
            CoverPath = coverPath
        };
    }

    /// <summary>One game from an Epic <c>*.item</c> manifest; <c>null</c> for DLC / incomplete installs.</summary>
    public static InstalledApp? ParseEpicManifest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) return null;

            string? name = Str("DisplayName");
            string? appName = Str("AppName");
            string? ns = Str("CatalogNamespace");
            string? item = Str("CatalogItemId");
            string? location = Str("InstallLocation");
            string? exe = Str("LaunchExecutable");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(exe)) return null;

            string link = string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(item)
                ? $"com.epicgames.launcher://apps/{Uri.EscapeDataString(appName)}?action=launch&silent=true"
                : $"com.epicgames.launcher://apps/{Uri.EscapeDataString(ns)}%3A{Uri.EscapeDataString(item)}%3A{Uri.EscapeDataString(appName)}?action=launch&silent=true";

            string? exePath = string.IsNullOrWhiteSpace(location) ? null : Path.Combine(location, exe.Replace('/', Path.DirectorySeparatorChar));
            return new InstalledApp($"epic:{appName}", name.Trim(), InstalledAppKind.Epic, link, exePath);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>One game from the values under <c>HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\&lt;id&gt;</c>.</summary>
    public static InstalledApp? ParseGogGame(Func<string, string?> value)
    {
        string? id = value("gameID");
        string? name = value("gameName");
        string? exe = value("exe");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(exe)) return null;

        return new InstalledApp($"gog:{id}", name.Trim(), InstalledAppKind.Gog, exe, exe);
    }

    /// <summary>Microsoft Store apps from <c>Get-StartApps | ConvertTo-Json</c> (entries whose AppID is <c>PackageFamily!App</c>).</summary>
    public static IReadOnlyList<InstalledApp> ParseStartAppsJson(string json)
    {
        var list = new List<InstalledApp>();
        if (string.IsNullOrWhiteSpace(json)) return list;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            IEnumerable<JsonElement> items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : new[] { root };

            foreach (var el in items)
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                string? name = el.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                string? appId = el.TryGetProperty("AppID", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(appId)) continue;
                // Store apps look like "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"; Win32 entries are paths / GUID paths.
                if (!appId.Contains('!') || appId.Contains('\\') || appId.Contains('/')) continue;

                list.Add(new InstalledApp($"store:{appId}", name.Trim(), InstalledAppKind.Store, $"shell:AppsFolder\\{appId}"));
            }
        }
        catch (JsonException)
        {
            // unreadable PowerShell output: no Store apps this round
        }

        return list;
    }
}
