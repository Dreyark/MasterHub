using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace MasterHub.Core.Localization;

public class LocalizationService : ILocalizationService
{
    private static readonly Lazy<LocalizationService> _lazyInstance = new(() => new LocalizationService());
    public static LocalizationService Instance => _lazyInstance.Value;

    private string _currentLanguageCode = "pl";
    private readonly Dictionary<string, Dictionary<string, string>> _translations = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LanguageOption> _supportedLanguages = new()
    {
        new("pl", "🇵🇱 Polski (PL)"),
        new("en", "🇬🇧 English (EN)"),
        new("de", "🇩🇪 Deutsch (DE)"),
        new("fr", "🇫🇷 Français (FR)"),
        new("es", "🇪🇸 Español (ES)"),
        new("ja", "🇯🇵 日本語 (JA)")
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? LanguageChanged;

    public string CurrentLanguageCode => _currentLanguageCode;
    public IReadOnlyList<LanguageOption> SupportedLanguages => _supportedLanguages;

    public string this[string key] => GetString(key);

    public LocalizationService()
    {
        InitializeTranslations();
        LoadSavedLanguage();
    }

    public string GetString(string key, string fallback = "")
    {
        if (_translations.TryGetValue(_currentLanguageCode, out var dict) && dict.TryGetValue(key, out var val))
        {
            return val;
        }

        // Fallback do angielskiego, a następnie polskiego
        if (_translations.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var enVal))
        {
            return enVal;
        }

        if (_translations.TryGetValue("pl", out var plDict) && plDict.TryGetValue(key, out var plVal))
        {
            return plVal;
        }

        return !string.IsNullOrEmpty(fallback) ? fallback : key;
    }

    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return;

        string code = NormalizeLanguageCode(languageCode);
        if (_currentLanguageCode == code) return;

        _currentLanguageCode = code;
        SaveLanguage(code);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguageCode)));
        LanguageChanged?.Invoke(_currentLanguageCode);
    }

    private static string NormalizeLanguageCode(string rawCode)
    {
        string lower = rawCode.Trim().ToLowerInvariant();
        if (lower.StartsWith("pl")) return "pl";
        if (lower.StartsWith("en")) return "en";
        if (lower.StartsWith("de")) return "de";
        if (lower.StartsWith("fr")) return "fr";
        if (lower.StartsWith("es")) return "es";
        if (lower.StartsWith("ja")) return "ja";
        if (lower.Contains('-')) return lower.Split('-')[0];
        return lower;
    }

    private void SaveLanguage(string code)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "MasterHub");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "settings.json");

            System.Text.Json.Nodes.JsonObject rootObj = new();
            if (File.Exists(path))
            {
                try
                {
                    string existing = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(existing) &&
                        System.Text.Json.Nodes.JsonNode.Parse(existing) is System.Text.Json.Nodes.JsonObject parsedObj)
                    {
                        rootObj = parsedObj;
                    }
                }
                catch { }
            }

            rootObj["Language"] = code;
            File.WriteAllText(path, rootObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void LoadSavedLanguage()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string path = Path.Combine(appData, "MasterHub", "settings.json");
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("Language", out var langProp) &&
                        langProp.ValueKind == JsonValueKind.String)
                    {
                        string? lang = langProp.GetString();
                        if (!string.IsNullOrWhiteSpace(lang))
                        {
                            _currentLanguageCode = NormalizeLanguageCode(lang);
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void InitializeTranslations()
    {
        // 1. Wczytaj wbudowane pliki JSON (EmbeddedResource)
        LoadEmbeddedTranslations();

        // 2. Wczytaj opcjonalne pliki JSON użytkownika (%APPDATA%\MasterHub\Languages\ oraz ./Locales/)
        LoadFileSystemTranslations();
    }

    private void LoadEmbeddedTranslations()
    {
        var assembly = typeof(LocalizationService).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        foreach (var resourceName in resourceNames)
        {
            if (!resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = resourceName.Split('.');
            if (parts.Length < 2) continue;
            string langCode = parts[^2].ToLowerInvariant(); // np. "pl" lub "en"

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    _translations[langCode] = new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
                    EnsureSupportedLanguage(langCode, dict);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Localization] Error loading embedded locale {resourceName}: {ex.Message}");
            }
        }
    }

    private void LoadFileSystemTranslations()
    {
        try
        {
            var searchPaths = new List<string>();

            string appDataLanguages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub", "Languages");
            if (Directory.Exists(appDataLanguages))
            {
                searchPaths.AddRange(Directory.GetFiles(appDataLanguages, "*.json"));
            }

            string baseDirLanguages = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Locales");
            if (Directory.Exists(baseDirLanguages))
            {
                searchPaths.AddRange(Directory.GetFiles(baseDirLanguages, "*.json"));
            }

            foreach (var file in searchPaths)
            {
                try
                {
                    string langCode = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    string json = File.ReadAllText(file);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        if (!_translations.TryGetValue(langCode, out var existing))
                        {
                            existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            _translations[langCode] = existing;
                        }

                        foreach (var kvp in dict)
                        {
                            existing[kvp.Key] = kvp.Value;
                        }

                        EnsureSupportedLanguage(langCode, dict);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Localization] Error loading locale file {file}: {ex.Message}");
                }
            }
        }
        catch { }
    }

    private void EnsureSupportedLanguage(string langCode, Dictionary<string, string> dict)
    {
        if (_supportedLanguages.Any(l => l.Code.Equals(langCode, StringComparison.OrdinalIgnoreCase)))
            return;

        string displayName;
        if (dict.TryGetValue("_LanguageName", out var customName) && !string.IsNullOrWhiteSpace(customName))
        {
            displayName = customName;
        }
        else
        {
            displayName = langCode.ToUpperInvariant();
        }

        _supportedLanguages.Add(new LanguageOption(langCode, displayName));
    }
}
