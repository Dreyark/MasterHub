using System.Text.Json;
using System.Text.RegularExpressions;

namespace MasterHub.Core.Tests;

/// <summary>
/// Statyczne kontrole spójności repozytorium (lokalizacja, style AXAML, assety) - łapią błędy, które
/// inaczej wychodzą dopiero w runtime albo w konkretnym języku interfejsu.
/// </summary>
public class SourceIntegrityTests
{
    private static readonly string Root = FindRepoRoot();
    private static string LocalesDir => Path.Combine(Root, "src", "MasterHub.Core", "Localization", "Locales");
    private static string AvaloniaDir => Path.Combine(Root, "src", "MasterHub.Avalonia");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MasterHub.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Nie znaleziono korzenia repozytorium (MasterHub.slnx).");
    }

    private static Dictionary<string, string> LoadLocale(string lang)
    {
        string json = File.ReadAllText(Path.Combine(LocalesDir, lang + ".json"));
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
    }

    private static IEnumerable<string> SourceFiles(params string[] patterns) =>
        patterns.SelectMany(p => Directory.EnumerateFiles(AvaloniaDir, p, SearchOption.AllDirectories))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                         && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

    public static IEnumerable<object[]> Languages() =>
        new[] { "en", "pl", "de", "es", "fr", "ja" }.Select(l => new object[] { l });

    [Theory]
    [MemberData(nameof(Languages))]
    public void Locale_HasNoMojibake(string lang)
    {
        // typowe ślady UTF-8 odczytanego jako Windows-1252: "Ã¶", "â€¢", "ðŸ", "Ã¢â‚¬"
        var bad = LoadLocale(lang)
            .Where(kv => Regex.IsMatch(kv.Value, "Ã[\u0080-¿¡-¿€‚ƒ„…†‡ˆ‰Š‹ŒŽ‘’“”•–—˜™š›œžŸ]|â€|ðŸ|Ã¢|Â[ -¿]"))
            .Select(kv => kv.Key).ToList();
        Assert.True(bad.Count == 0, $"{lang}.json: zniekształcone kodowanie w kluczach: {string.Join(", ", bad.Take(10))}");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Locale_HasSameKeysAndPlaceholdersAsEnglish(string lang)
    {
        var en = LoadLocale("en");
        var other = LoadLocale(lang);

        Assert.Empty(en.Keys.Except(other.Keys));
        Assert.Empty(other.Keys.Except(en.Keys));

        var mismatched = en.Where(kv => Placeholders(kv.Value) != Placeholders(other[kv.Key])).Select(kv => kv.Key).ToList();
        Assert.True(mismatched.Count == 0, $"{lang}.json: różne placeholdery {{n}} w: {string.Join(", ", mismatched.Take(10))}");

        static string Placeholders(string s) =>
            string.Join(",", Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x));
    }

    [Fact]
    public void AllLocalizationKeysUsedInCodeAndXaml_ExistInEnglish()
    {
        var en = LoadLocale("en");
        var used = new Dictionary<string, string>();
        foreach (var f in SourceFiles("*.axaml", "*.cs"))
        {
            string t = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(t, @"\{loc:Loc\s+(?:Key=)?([A-Za-z0-9_\.]+)")) used[m.Groups[1].Value] = f;
            foreach (Match m in Regex.Matches(t, @"GetString\(\s*""([A-Za-z0-9_\.]+)""")) used[m.Groups[1].Value] = f;
        }
        var missing = used.Where(kv => !en.ContainsKey(kv.Key)).Select(kv => $"{kv.Key} ({Path.GetFileName(kv.Value)})").ToList();
        Assert.True(missing.Count == 0, "Brakujące klucze lokalizacji: " + string.Join(", ", missing.Take(15)));
    }

    [Fact]
    public void Axaml_UsesOnlyDefinedResourceKeys_AndStyleClasses()
    {
        var files = SourceFiles("*.axaml").ToList();
        var defined = new HashSet<string>();
        var definedClasses = new HashSet<string>();
        foreach (var f in files)
        {
            string t = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(t, @"x:Key=""([^""]+)""")) defined.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(t, @"Selector=""([^""]+)"""))
                foreach (Match c in Regex.Matches(m.Groups[1].Value, @"\.([A-Za-z][A-Za-z0-9_\-]*)")) definedClasses.Add(c.Groups[1].Value);
        }

        var missingKeys = new List<string>();
        var missingClasses = new List<string>();
        foreach (var f in files)
        {
            string t = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(t, @"\{(?:StaticResource|DynamicResource)\s+([A-Za-z0-9_\.]+)\}"))
                if (!defined.Contains(m.Groups[1].Value)) missingKeys.Add($"{m.Groups[1].Value} ({Path.GetFileName(f)})");
            foreach (Match m in Regex.Matches(t, @"\sClasses=""([^""{}]+)"""))
                foreach (var c in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (!definedClasses.Contains(c)) missingClasses.Add($"{c} ({Path.GetFileName(f)})");
        }
        Assert.True(missingKeys.Count == 0, "Brakujące zasoby: " + string.Join(", ", missingKeys.Distinct().Take(15)));
        Assert.True(missingClasses.Count == 0, "Klasy bez zdefiniowanego stylu (render domyślnym stylem Fluent): " + string.Join(", ", missingClasses.Distinct().Take(15)));
    }

    [Fact]
    public void Views_HaveNoUnstyledButtons()
    {
        // Przycisk bez klasy renderuje się domyślnym (szarym, systemowym) stylem Fluent i odstaje od motywu.
        var bare = new List<string>();
        foreach (var f in SourceFiles("*.axaml").Where(f => f.Contains(Path.DirectorySeparatorChar + "Views" + Path.DirectorySeparatorChar)
                                                            || f.EndsWith("MainWindow.axaml")))
        {
            string t = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(t, @"<(Button|ToggleButton)\b(?!\.)([^>]*)>"))
                if (!m.Groups[2].Value.Contains("Classes=")) bare.Add($"{Path.GetFileName(f)}: <{m.Groups[1].Value} {m.Groups[2].Value.Trim().Split('\n')[0].Trim()}");
        }
        Assert.True(bare.Count == 0, "Przyciski bez klasy stylu: " + string.Join(" | ", bare.Take(8)));
    }

    [Fact]
    public void Axaml_ReferencesOnlyExistingAssets()
    {
        var missing = new List<string>();
        foreach (var f in SourceFiles("*.axaml"))
        {
            string t = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(t, @"(?:Icon|Source)=""(/Assets/[^""]+)"""))
                if (!File.Exists(Path.Combine(AvaloniaDir, m.Groups[1].Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))))
                    missing.Add($"{m.Groups[1].Value} ({Path.GetFileName(f)})");
            foreach (Match m in Regex.Matches(t, @"avares://MasterHub\.Avalonia/([^""]+)"""))
                if (!File.Exists(Path.Combine(AvaloniaDir, m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar))))
                    missing.Add($"{m.Groups[1].Value} ({Path.GetFileName(f)})");
        }
        Assert.True(missing.Count == 0, "Odwołania do nieistniejących assetów: " + string.Join(", ", missing));
    }

    [Fact]
    public void ViewFiles_RespectOneHundredFiftyLineLimit()
    {
        var tooLong = SourceFiles("*.axaml", "*.axaml.cs")
            .Where(f => File.ReadLines(f).Count() > 150)
            .Select(f => Path.GetRelativePath(AvaloniaDir, f)).ToList();
        Assert.True(tooLong.Count == 0, "Pliki widoków powyżej 150 linii (reguła AI_CONTEXT): " + string.Join(", ", tooLong));
    }
}
