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

    public string GetString(string key, string fallback = "") => GetStringFor(_currentLanguageCode, key, fallback);

    /// <summary>The text of a key in a given language, with the same fallback (English, then Polish, then the fallback).</summary>
    internal string GetStringFor(string languageCode, string key, string fallback = "")
    {
        if (_translations.TryGetValue(languageCode, out var dict) && dict.TryGetValue(key, out var val))
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

            // only the language files: the assembly also embeds other JSON (profile templates, ...) that is not a translation
            if (!resourceName.Contains(".Localization.Locales.", StringComparison.OrdinalIgnoreCase))
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

    /// <summary>The folder of the user's own language files (T-34 #11): <c>%APPDATA%\MasterHub\Languages</c>.</summary>
    public static string UserLanguagesFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub", "Languages");

    /// <summary>The name of the file "Make a language file" writes; it is never loaded as a language itself.</summary>
    public const string TemplateFileName = "template.json";

    /// <summary>Language files larger than this are skipped (a translation is a few hundred KB).</summary>
    internal const long MaxLanguageFileBytes = 4L * 1024 * 1024;

    private void LoadFileSystemTranslations()
    {
        try
        {
            LoadLanguageFolder(UserLanguagesFolder);
            LoadLanguageFolder(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Locales"));
        }
        catch { }
    }

    /// <summary>
    /// Loads every <c>&lt;code&gt;.json</c> of a folder as a language: a new code appears in the language picker (its name from
    /// <c>_LanguageName</c>), a known code gets its texts replaced key by key. A key the file lacks falls back to English.
    /// Returns the codes loaded.
    /// </summary>
    internal IReadOnlyList<string> LoadLanguageFolder(string folder)
    {
        var loaded = new List<string>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return loaded;

        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                if (Path.GetFileName(file).Equals(TemplateFileName, StringComparison.OrdinalIgnoreCase)) continue;
                string langCode = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                if (!IsValidLanguageCode(langCode)) continue;
                if (new FileInfo(file).Length > MaxLanguageFileBytes) continue;

                var dict = ParseLanguageFile(File.ReadAllText(file));
                if (dict == null || dict.Count == 0) continue;

                if (!_translations.TryGetValue(langCode, out var existing))
                {
                    existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _translations[langCode] = existing;
                }
                foreach (var kvp in dict) existing[kvp.Key] = kvp.Value;

                EnsureSupportedLanguage(langCode, dict);
                loaded.Add(langCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Localization] Error loading locale file {file}: {ex.Message}");
            }
        }
        return loaded;
    }

    /// <summary>A language code made of letters, digits and <c>-</c> (<c>it</c>, <c>pt-br</c>), at most 16 characters.</summary>
    internal static bool IsValidLanguageCode(string? code) =>
        !string.IsNullOrEmpty(code) && code.Length <= 16 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>The text entries of a language file; entries that are not text are ignored, broken JSON gives <c>null</c>.</summary>
    internal static Dictionary<string, string>? ParseLanguageFile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && property.Name.Length > 0)
                    dict[property.Name] = property.Value.GetString() ?? "";
            }
            return dict;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// "Make a language file" (T-34 #11): writes every English text into <c>template.json</c> (sorted by key, with a
    /// <c>_LanguageName</c> to fill in). Rename the copy to the language code (<c>it.json</c>), translate the values and
    /// restart the app: the language appears in the picker. Returns the path written.
    /// </summary>
    public string WriteLanguageTemplate(string? folder = null)
    {
        folder ??= UserLanguagesFolder;
        Directory.CreateDirectory(folder);

        var english = _translations.TryGetValue("en", out var en) ? en : new Dictionary<string, string>();
        string path = Path.Combine(folder, TemplateFileName);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Indented = true,
                   Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
               }))
        {
            writer.WriteStartObject();
            writer.WriteString("_LanguageName", "My language (XX)");   // first, so a translator sees it at once
            foreach (var kvp in english.Where(k => k.Key != "_LanguageName").OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                writer.WriteString(kvp.Key, kvp.Value);
            }
            writer.WriteEndObject();
        }
        return path;
    }

    /// <summary>The English text of a key, or <c>null</c> (for tests and the template).</summary>
    internal string? EnglishText(string key) =>
        _translations.TryGetValue("en", out var en) && en.TryGetValue(key, out var v) ? v : null;

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
