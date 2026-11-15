using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MasterHub.Core.Localization;
using Xunit;

namespace MasterHub.Core.Tests;

/// <summary>T-34 #11: own language files next to the built-in ones, and "Make a language file".</summary>
public sealed class LanguageFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_lang_" + Guid.NewGuid().ToString("N"));

    public LanguageFilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void TheTemplate_HasEveryEnglishText_AndTheLanguageNameFirst()
    {
        var service = new LocalizationService();
        string path = service.WriteLanguageTemplate(_dir);

        Assert.Equal(Path.Combine(_dir, LocalizationService.TemplateFileName), path);
        string json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal("_LanguageName", names[0]);
        Assert.True(names.Count > 1000, "keys: " + names.Count);
        Assert.Equal(service.EnglishText("IconLib_Open"), doc.RootElement.GetProperty("IconLib_Open").GetString());
        Assert.Contains("…", json);   // written as UTF-8 text, not escapes
        Assert.DoesNotContain("\\u0027", json);
    }

    [Fact]
    public void AnOwnLanguageFile_AppearsInThePicker_AndMissingTextsStayEnglish()
    {
        File.WriteAllText(Path.Combine(_dir, "it.json"), "{ \"_LanguageName\": \"Italiano (IT)\", \"IconLib_Open\": \"Icone\" }");
        var service = new LocalizationService();

        var loaded = service.LoadLanguageFolder(_dir);

        Assert.Equal(new[] { "it" }, loaded);
        Assert.Contains(service.SupportedLanguages, l => l.Code == "it" && l.DisplayName == "Italiano (IT)");
        // (SetLanguage is not used: it would write the choice into the real settings.json)
        Assert.Equal("Icone", service.GetStringFor("it", "IconLib_Open"));
        Assert.Equal(service.EnglishText("Check_Close"), service.GetStringFor("it", "Check_Close"));   // not translated: English
    }

    [Fact]
    public void TheTemplate_BadNames_HugeFiles_AndNonTextEntries_AreNotLoaded()
    {
        File.WriteAllText(Path.Combine(_dir, LocalizationService.TemplateFileName), "{ \"IconLib_Open\": \"X\" }");
        File.WriteAllText(Path.Combine(_dir, "bad name!.json"), "{ \"IconLib_Open\": \"X\" }");
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");
        File.WriteAllText(Path.Combine(_dir, "big.json"), "{ \"A\": \"" + new string('x', (int)LocalizationService.MaxLanguageFileBytes) + "\" }");
        File.WriteAllText(Path.Combine(_dir, "pt-br.json"), "{ \"_LanguageName\": \"Português\", \"Check_Close\": \"Fechar\", \"Num\": 5, \"Obj\": {} }");

        var service = new LocalizationService();
        var loaded = service.LoadLanguageFolder(_dir);

        Assert.Equal(new[] { "pt-br" }, loaded);
    }

    [Theory]
    [InlineData("it", true)]
    [InlineData("pt-br", true)]
    [InlineData("template", true)]   // a valid code; the template file is skipped by its file name
    [InlineData("../x", false)]
    [InlineData("", false)]
    [InlineData("averyveryverylongcode", false)]
    public void LanguageCodes_AreLettersDigitsAndDashes(string code, bool valid)
    {
        Assert.Equal(valid, LocalizationService.IsValidLanguageCode(code));
    }

    [Fact]
    public void ParseLanguageFile_KeepsOnlyTexts_AndAcceptsCommentsAndTrailingCommas()
    {
        var dict = LocalizationService.ParseLanguageFile("{ // comment\n \"A\": \"a\", \"B\": 1, \"C\": null, }");
        Assert.Equal(new[] { "A" }, dict!.Keys);
        Assert.Null(LocalizationService.ParseLanguageFile("[1,2]"));
        Assert.Null(LocalizationService.ParseLanguageFile("nope"));
    }
}
