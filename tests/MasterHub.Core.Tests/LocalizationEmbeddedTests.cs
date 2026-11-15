using System.Reflection;
using MasterHub.Core.Localization;

namespace MasterHub.Core.Tests;

/// <summary>
/// The assembly embeds other JSON besides the language files (the profile templates). The language loader once tried to read those as
/// translations, which logged a JsonException for every template at every start.
/// </summary>
public class LocalizationEmbeddedTests
{
    [Fact]
    public void OnlyTheLanguageFiles_AreLoadedAsLanguages()
    {
        var field = typeof(LocalizationService).GetField("_translations", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var loaded = (System.Collections.IDictionary)field.GetValue(LocalizationService.Instance)!;

        var languages = loaded.Keys.Cast<string>().ToList();

        Assert.Contains("en", languages);
        Assert.Contains("pl", languages);
        Assert.DoesNotContain(languages, l => l.Contains('-'));   // "discord-meetings", "everyday-desk" … are templates, not languages
        Assert.DoesNotContain("game-action-bar", languages);
    }

    [Fact]
    public void EveryEmbeddedJson_IsEitherALanguageOrATemplate()
    {
        var names = typeof(LocalizationService).Assembly.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        Assert.All(names, n => Assert.True(
            n.Contains(".Localization.Locales.", StringComparison.Ordinal) || n.Contains(".Profiles.Templates.", StringComparison.Ordinal),
            $"an embedded JSON that is neither: {n} (the language loader would try to read it)"));
    }
}
