using System.IO.Compression;
using System.Text;
using MasterHub.Core.Actions;
using MasterHub.Core.Diagnostics;
using MasterHub.Core.Profiles;

namespace MasterHub.Core.Tests;

/// <summary>T-34 #20: an action of the official app without an equivalent must not vanish; it stays as a labelled empty key.</summary>
public class MhzNotSupportedTests
{
    private static ProfileModel Import(string keypadJson, string extraModules = "")
    {
        string json = "{\"keypad\":{\"a\":[[" + keypadJson + "]]}" + extraModules + "}";
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("profile.json");
            using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            w.Write(json);
        }
        ms.Position = 0;
        return CoolerMasterMhzImporter.Import(ms, "x_profile_Test.mhz").Single();
    }

    [Fact]
    public void An_unknown_action_becomes_an_empty_key_that_says_what_it_was()
    {
        var p = Import("{\"id\":0,\"btnFunc\":\"web-url\",\"url\":\"example.com\"},{\"id\":1,\"btnFunc\":\"gaming-macro-x\"}");

        var known = p.Pages[0].Keys[0];
        var lost = p.Pages[0].Keys[1];
        Assert.Equal(DeckActionType.OpenWebsite, known.Action);
        Assert.Equal(DeckActionType.None, lost.Action);
        Assert.Equal("Not supported: gaming-macro-x", lost.CustomTitle);
    }

    [Fact]
    public void A_key_with_its_own_title_keeps_it_and_gets_the_note_below()
    {
        var p = Import("{\"id\":0,\"btnFunc\":\"gaming-macro-x\",\"title\":\"Boost\"}");

        var key = p.Pages[0].Keys[0];
        Assert.Equal("Boost", key.CustomTitle);
        Assert.Equal("Not supported: gaming-macro-x", key.CustomSubtitle);
    }

    [Fact]
    public void An_empty_slot_is_not_labelled()
    {
        var p = Import("{\"id\":0,\"btnFunc\":\"\"},{\"id\":1},{\"id\":2,\"btnFunc\":\"none\"}");

        Assert.All(p.Pages[0].Keys.Take(3), k => Assert.True(string.IsNullOrEmpty(k.CustomTitle)));
    }

    [Fact]
    public void Lost_multi_action_steps_are_named_on_the_key()
    {
        var p = Import("{\"id\":0,\"btnFunc\":\"multi-tasking\",\"title\":\"Combo\",\"actionList\":[" +
                       "{\"btnFunc\":\"screen-capture\"},{\"btnFunc\":\"weird-step\"},{\"btnFunc\":\"other-step\"}]}");

        var key = p.Pages[0].Keys[0];
        Assert.Equal(DeckActionType.MultiAction, key.Action);
        Assert.Single(key.MultiActions!);
        Assert.Equal("Not supported (steps): weird-step, other-step", key.CustomSubtitle);
    }

    [Fact]
    public void An_unsupported_knob_function_is_written_to_the_log()
    {
        AppLog.Clear();
        Import("{\"id\":0,\"btnFunc\":\"\"}", ",\"knobsList\":{\"a\":{\"rotateB\":{\"btnFunc\":\"brush-size\"}}}");

        Assert.Contains(AppLog.Recent(), l => l.Text.Contains("knob 1") && l.Text.Contains("brush-size"));
    }
}
