using System;
using System.IO;
using System.Linq;
using System.Text;
using MasterHub.Core.Profiles.Packages;
using MasterHub.Core.Rendering.Icons;
using SkiaSharp;
using Xunit;

namespace MasterHub.Core.Tests;

public class IconLibraryTests
{
    private static readonly IconLibrary Library = IconLibrary.Shared;

    [Fact]
    public void TheBundledLibrary_Loads_TheLucideIcons()
    {
        Assert.True(Library.All.Count > 2000, "count: " + Library.All.Count);
        Assert.NotNull(Library.Find("house"));
        Assert.NotNull(Library.Find("mic"));
        Assert.NotNull(Library.Find("PLAY"));   // case does not matter
    }

    [Fact]
    public void Icons_AreSortedByName_AndUnique()
    {
        var names = Library.All.Select(e => e.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Search_PutsTheExactName_First_ThenNamesThatStartWithIt()
    {
        var results = Library.Search("play");

        Assert.Equal("play", results[0].Name);
        Assert.All(results.Take(3), r => Assert.Contains("play", r.Name));
    }

    [Fact]
    public void Search_FindsIcons_ByTheirTags()
    {
        var results = Library.Search("home", 200);
        Assert.Contains(results, r => r.Name == "house");
    }

    [Fact]
    public void Search_WithSeveralWords_NeedsEveryWord()
    {
        var results = Library.Search("arrow up", 200);
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.True(
            (r.Name.Contains("arrow") || r.Tags.Any(t => t.Contains("arrow"))) &&
            (r.Name.Contains("up") || r.Tags.Any(t => t.Contains("up")))));
    }

    [Fact]
    public void Search_RespectsTheLimit_AndAnEmptyQueryListsTheStart()
    {
        Assert.Equal(10, Library.Search("", 10).Count);
        Assert.True(Library.Search("a", 5).Count <= 5);
        Assert.Empty(Library.Search("zzzzqqq-nothing"));
        Assert.Empty(Library.Search("play", 0));
    }

    [Fact]
    public void BuildSvg_PaintsTheIconInTheChosenColour()
    {
        string svg = Library.BuildSvg("house", "#FF0000")!;

        Assert.StartsWith("<svg", svg);
        Assert.Contains("stroke=\"#ff0000\"", svg);
        Assert.DoesNotContain("currentColor", svg);
        Assert.DoesNotContain("<script", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSvg_ForAnUnknownIcon_IsNull()
    {
        Assert.Null(Library.BuildSvg("no-such-icon-at-all", "#fff"));
        Assert.Null(Library.Render("no-such-icon-at-all", "#fff", 32));
    }

    [Fact]
    public void BuildSvg_AddsAirAroundTheIcon()
    {
        Assert.Contains("viewBox=\"0 0 24 24\"", Library.BuildSvg("house", "#fff", 0)!);
        Assert.Contains("viewBox=\"-4.8 -4.8 33.6 33.6\"", Library.BuildSvg("house", "#fff", 20)!);
    }

    [Fact]
    public void Render_DrawsTheIcon_InItsColour_OnATransparentBackground()
    {
        using SKBitmap bmp = Library.Render("square", "#00ff00", 64, paddingPercent: 0)!;

        Assert.Equal(64, bmp.Width);
        Assert.Equal(SKColors.Transparent.Alpha, bmp.GetPixel(32, 32).Alpha);   // the middle of an outline square is empty
        bool green = false;
        for (int x = 0; x < 64 && !green; x++)
        for (int y = 0; y < 64 && !green; y++)
        {
            var p = bmp.GetPixel(x, y);
            if (p.Alpha > 200 && p.Green > 200 && p.Red < 60 && p.Blue < 60) green = true;
        }
        Assert.True(green, "no green stroke drawn");
    }

    [Theory]
    [InlineData("#ABC", "#aabbcc")]
    [InlineData("FF8800", "#ff8800")]
    [InlineData("#80FF8800", "#ff8800")]
    [InlineData("red", "#ffffff")]
    [InlineData(null, "#ffffff")]
    [InlineData("#12345\"onload=", "#ffffff")]
    public void NormalizeHex_AcceptsOnlyColours(string? input, string expected)
    {
        Assert.Equal(expected, IconLibrary.NormalizeHex(input));
    }

    [Fact]
    public void IconRef_ParsesTheThreeKinds()
    {
        Assert.Equal(IconRef.Lucide("house"), IconRef.Parse("lucide:House"));
        Assert.Equal(IconRef.Emoji("🎮"), IconRef.Parse("emoji:🎮"));
        Assert.Equal(IconRef.File(@"C:\pics\a.png"), IconRef.Parse(@"C:\pics\a.png"));
        Assert.Null(IconRef.Parse("lucide:../../evil"));
        Assert.Null(IconRef.Parse("emoji:"));
        Assert.Null(IconRef.Parse("  "));
        Assert.Equal("lucide:house", IconRef.Lucide("house").ToString());
    }

    [Fact]
    public void ALibraryText_WithScriptsOrLinks_IsSkipped()
    {
        var lib = new IconLibrary("good|a,b|<path d=\"M0 0h24\" />\nbad|x|<script>alert(1)</script>\nlink|x|<use href=\"http://x\" />\nBad Name|x|<path />");

        Assert.Equal(new[] { "good" }, lib.All.Select(e => e.Name));
    }

    [Fact]
    public void IconFiles_StoresTheTintedSvg_InTheMediaStore_OncePerContent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mh_icons_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MediaStore(dir);
            string a = IconFiles.Save(Library, store, "house", "#ff0000")!;
            string b = IconFiles.Save(Library, store, "house", "#ff0000")!;
            string c = IconFiles.Save(Library, store, "house", "#00ff00")!;

            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
            Assert.True(store.IsStorePath(a));
            Assert.EndsWith(".svg", a);
            Assert.Contains("#ff0000", File.ReadAllText(a, Encoding.UTF8));
            Assert.Null(IconFiles.Save(Library, store, "nope-nope", "#fff"));
            Assert.True(MediaStore.IsPackable(a));   // travels in a .mhdeck package
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
