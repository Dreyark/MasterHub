using MasterHub.Core.Actions;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Templates;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;

namespace MasterHub.Core.Tests;

/// <summary>T-10: the ready-made profiles are valid data, instantiate into real profiles, and "For games" sends scan codes held for a short time.</summary>
public sealed class TemplatesAndHotkeysTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_tpl_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class Plain : ISecretProtectionService
    {
        public bool IsProtected(string? value) => false;
        public string? Protect(string? plainText) => plainText;
        public string? Unprotect(string? cipherText) => cipherText;
    }

    // ── templates ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryEmbeddedTemplate_IsValid_AndTheSetIsComplete()
    {
        var all = ProfileTemplateService.LoadEmbedded();

        Assert.True(all.Count >= 6, $"only {all.Count} templates found");
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());

        foreach (var t in all)
        {
            var problems = ProfileTemplateService.Validate(t);
            Assert.True(problems.Count == 0, $"{t.Id}: {string.Join("; ", problems)}");
            Assert.Equal(15, t.Keys.Count);                              // every template fills the whole key panel
            Assert.False(string.IsNullOrWhiteSpace(t.NameFor("pl")));
            Assert.False(string.IsNullOrWhiteSpace(t.DescriptionFor("de")));   // falls back to English
        }
    }

    [Fact]
    public void Validator_CatchesBadTemplates()
    {
        var bad = new ProfileTemplate
        {
            Id = "x",
            Name = { ["en"] = "X" },
            Description = { ["en"] = "d" },
            Exes = { "bad;exe" },
            Keys =
            {
                new TemplateKey { Index = 0, Action = "NoSuchAction" },
                new TemplateKey { Index = 0, Action = "MediaNext" },                         // duplicate index
                new TemplateKey { Index = 20, Action = "MediaNext" },                        // out of range
                new TemplateKey { Index = 1, Action = "Hotkey", Parameter = "Ctrl+" },        // not a hotkey
                new TemplateKey { Index = 2, Action = "MediaNext", SendMode = "ForGames" },   // send mode on a non-hotkey
                new TemplateKey { Index = 3, Action = "MediaNext", Background = "red" },
                new TemplateKey { Index = 4, Action = "StreamDeckPluginAction" },
                new TemplateKey { Index = 5, Action = "MediaNext", Title = "A VERY LONG TITLE INDEED" }
            }
        };

        var problems = ProfileTemplateService.Validate(bad);

        Assert.True(problems.Count >= 8, string.Join("; ", problems));
        Assert.Contains(problems, p => p.Contains("unknown action"));
        Assert.Contains(problems, p => p.Contains("duplicate"));
        Assert.Contains(problems, p => p.Contains("not a hotkey"));
        Assert.Contains(problems, p => p.Contains("exe"));
    }

    [Fact]
    public void Instantiate_CreatesARealProfile_WithoutLinkingAppsUnlessAsked()
    {
        var profiles = new ProfileService(_dir, new Plain());
        var service = new ProfileTemplateService(profiles);
        var obs = service.GetAll().Single(t => t.Id == "obs-streaming");

        var plain = service.Instantiate(obs);
        Assert.Empty(plain.SmartRules);                                         // nothing auto-linked
        Assert.Equal("OBS streaming", plain.Name);

        var linked = service.Instantiate(obs, "My stream", linkApps: true);
        Assert.Equal("My stream", linked.Name);
        Assert.Equal("obs64", linked.SmartRules.Single().ProcessName);

        var saved = profiles.GetAllProfiles().Single(p => p.Id == linked.Id);
        Assert.Equal(DeckActionType.ObsToggleStream, saved.Keys.Single(k => k.Index == 0).Action);
        Assert.Equal("Scene 2", saved.Keys.Single(k => k.Index == 7).ActionParameter);
        Assert.Equal(DeckActionType.ObsToggleStream, saved.Pages[0].Keys.Single(k => k.Index == 0).Action);   // page 1 mirrors the keys
        Assert.Equal("#38BDF8", saved.LedColorHex);
        Assert.NotEqual(plain.Id, linked.Id);
    }

    [Fact]
    public void GameActionBar_UsesScanCodes()
    {
        var profiles = new ProfileService(_dir, new Plain());
        var bar = new ProfileTemplateService(profiles).GetAll().Single(t => t.Id == "game-action-bar");

        var created = new ProfileTemplateService(profiles).Instantiate(bar);

        var hotkeys = created.Keys.Where(k => k.Action == DeckActionType.Hotkey).ToList();
        Assert.Equal(12, hotkeys.Count);
        Assert.All(hotkeys, k => Assert.Equal(HotkeySendMode.ForGames, k.HotkeySendMode));
    }

    [Fact]
    public void InvalidTemplate_IsNeverInstantiated()
    {
        var profiles = new ProfileService(_dir, new Plain());
        var service = new ProfileTemplateService(profiles);
        var broken = new ProfileTemplate { Id = "b", Name = { ["en"] = "B" }, Description = { ["en"] = "d" }, Keys = { new TemplateKey { Index = 0, Action = "Nope" } } };

        Assert.Throws<InvalidOperationException>(() => service.Instantiate(broken));
        Assert.Single(profiles.GetAllProfiles());                                // only the default profile exists
    }

    // ── hotkeys ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Ctrl+Shift+S", 2, 'S')]
    [InlineData("alt f4", 1, 0x73)]
    [InlineData("F13", 0, 0x7C)]
    [InlineData("F24", 0, 0x87)]
    [InlineData("Win+D", 1, 'D')]
    [InlineData("Tab", 0, 0x09)]
    [InlineData("7", 0, '7')]
    public void Parser_ReadsModifiersAndTheMainKey(string text, int modifiers, int main)
    {
        var combo = HotkeyParser.Parse(text)!;
        Assert.Equal(modifiers, combo.Modifiers.Count);
        Assert.Equal(main, combo.MainKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense-key")]
    public void Parser_RejectsText_ThatIsNotAHotkey(string text) => Assert.Null(HotkeyParser.Parse(text));

    private sealed class FakeKeyboard : IKeyboardSender
    {
        public List<string> Events { get; } = new();
        public bool ThrowOnMain { get; set; }
        public void Down(byte vk, bool extended, bool scanCode)
        {
            if (ThrowOnMain && vk == (byte)'A') throw new InvalidOperationException("boom");
            Events.Add($"down:{vk:X2}:{(scanCode ? "scan" : "vk")}{(extended ? ":ext" : "")}");
        }
        public void Up(byte vk, bool extended, bool scanCode) => Events.Add($"up:{vk:X2}:{(scanCode ? "scan" : "vk")}{(extended ? ":ext" : "")}");
    }

    [Fact]
    public void ForGames_SendsScanCodes_HoldsTheMainKey_AndReleasesInReverse()
    {
        var keyboard = new FakeKeyboard();
        var sleeps = new List<int>();
        var sender = new HotkeySender(keyboard, sleeps.Add);

        Assert.True(sender.Send("Ctrl+Shift+A", HotkeySendMode.ForGames, holdMs: 70));

        Assert.Equal(new[]
        {
            "down:11:scan", "down:10:scan", "down:41:scan", "up:41:scan", "up:10:scan", "up:11:scan"
        }, keyboard.Events);
        Assert.Contains(70, sleeps);                                  // the main key is held for the configured time
    }

    [Fact]
    public void ForGames_DefaultHold_IsAbout30ms_AndIsClamped()
    {
        var sleeps = new List<int>();
        new HotkeySender(new FakeKeyboard(), sleeps.Add).Send("A", HotkeySendMode.ForGames);
        Assert.Contains(HotkeySender.DefaultHoldMs, sleeps);

        sleeps.Clear();
        new HotkeySender(new FakeKeyboard(), sleeps.Add).Send("A", HotkeySendMode.ForGames, holdMs: 5000);
        Assert.Contains(HotkeySender.MaxHoldMs, sleeps);
    }

    [Fact]
    public void ExtendedKeys_GetTheExtendedFlag_AndNormalModeUsesVirtualKeys()
    {
        var keyboard = new FakeKeyboard();
        new HotkeySender(keyboard, _ => { }).Send("Right", HotkeySendMode.ForGames);
        Assert.Equal(new[] { "down:27:scan:ext", "up:27:scan:ext" }, keyboard.Events);

        var normal = new FakeKeyboard();
        new HotkeySender(normal, _ => { }).Send("A", HotkeySendMode.Normal);
        Assert.All(normal.Events, e => Assert.EndsWith(":vk", e));
    }

    [Fact]
    public void ModifiersAreAlwaysReleased_EvenWhenSendingTheMainKeyFails()
    {
        var keyboard = new FakeKeyboard { ThrowOnMain = true };

        Assert.Throws<InvalidOperationException>(() => new HotkeySender(keyboard, _ => { }).Send("Ctrl+A", HotkeySendMode.ForGames));

        Assert.Equal(new[] { "down:11:scan", "up:11:scan" }, keyboard.Events);   // no stuck Ctrl
    }

    [Fact]
    public void NotAHotkey_SendsNothing()
    {
        var keyboard = new FakeKeyboard();
        Assert.False(new HotkeySender(keyboard, _ => { }).Send("???", HotkeySendMode.ForGames));
        Assert.Empty(keyboard.Events);
    }
}
