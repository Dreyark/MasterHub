using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MasterHub.Cli;
using MasterHub.Cli.Mcp;
using MasterHub.Core.Actions;
using MasterHub.Core.Api;
using MasterHub.Core.Api.Client;
using MasterHub.Core.Profiles;
using MasterHub.Core.Security;

namespace MasterHub.Core.Tests;

public class AssistantGuardTests
{
    [Theory]
    [InlineData(@"C:\tools\run.bat", true)]
    [InlineData(@"C:\tools\run.PS1", true)]
    [InlineData("cmd", true)]
    [InlineData("cmd.exe /c del *", true)]
    [InlineData("powershell -enc AAAA", true)]
    [InlineData(@"C:\Windows\System32\wscript.exe", true)]
    [InlineData(@"C:\setup\installer.msi", true)]
    [InlineData(@"C:\Program Files\App\app.exe", false)]
    [InlineData(@"C:\Users\me\Documents\notes.txt", false)]
    [InlineData("notepad", false)]
    [InlineData("", false)]
    public void RiskyTargets_AreRecognised(string target, bool risky) => Assert.Equal(risky, AssistantGuard.IsRiskyTarget(target));

    [Theory]
    [InlineData("Alt+F4", true)]
    [InlineData("alt + f4", true)]
    [InlineData("Ctrl+W", true)]
    [InlineData("Control+Shift+Delete", true)]
    [InlineData("Shift+Ctrl+Delete", true)]
    [InlineData("Delete", true)]
    [InlineData("Ctrl+Shift+M", false)]
    [InlineData("Win+D", false)]
    [InlineData("Ctrl+S", false)]
    [InlineData("", false)]
    public void ClosingShortcuts_AreRecognised(string hotkey, bool destructive) => Assert.Equal(destructive, AssistantGuard.IsDestructiveHotkey(hotkey));

    private static ValidationResult Check(string control, string action, string? parameter = null, bool power = false) =>
        ProfileValidator.Validate(new[] { new ControlChange { Control = control, Action = action, Parameter = parameter } }, 1, new AssistantLimits(power));

    [Theory]
    [InlineData("SystemShutdown")]
    [InlineData("SystemRestart")]
    [InlineData("SystemSleep")]
    [InlineData("CloseApplication")]
    public void ClosingActions_AreRefusedOnAPress_AndSayWhatToUse(string action)
    {
        var result = Check("keys.3", action);

        Assert.False(result.IsValid);
        Assert.Contains("keys.3:hold", string.Join(' ', result.Errors));   // the reason and the way out, for the model
    }

    [Theory]
    [InlineData("SystemShutdown")]
    [InlineData("CloseApplication")]
    public void ClosingActions_AreAcceptedOnAHold_EvenWithMorePower(string action)
    {
        Assert.True(Check("keys.3:hold", action).IsValid);
        Assert.False(Check("keys.3:double", action).IsValid);
        Assert.False(Check("keys.3", action, power: true).IsValid);   // "more power" never lifts the hold rule
    }

    [Fact]
    public void AClosingHotkey_NeedsAHold_AnOrdinaryOneDoesNot()
    {
        Assert.False(Check("keys.0", "Hotkey", "Alt+F4").IsValid);
        Assert.True(Check("keys.0:hold", "Hotkey", "Alt+F4").IsValid);
        Assert.True(Check("keys.0", "Hotkey", "Ctrl+Shift+M").IsValid);
    }

    [Fact]
    public void ArbitraryServiceCalls_AreRefused_UnlessMorePowerIsOn()
    {
        var refused = Check("keys.0", "HomeAssistantCallService", "light.turn_on");
        Assert.False(refused.IsValid);
        Assert.Contains("HomeAssistantTurnOn", string.Join(' ', refused.Errors));

        Assert.True(Check("keys.0", "HomeAssistantCallService", "light.turn_on", power: true).IsValid);
        Assert.True(Check("keys.0", "HomeAssistantToggle", "light.desk").IsValid);
    }

    [Fact]
    public void ScriptsAndShells_AreNotOpened_UnlessMorePowerIsOn()
    {
        Assert.False(Check("keys.0", "OpenApplication", @"C:\x\clean.bat").IsValid);
        Assert.False(Check("keys.0", "OpenFile", "powershell").IsValid);
        Assert.True(Check("keys.0", "OpenApplication", @"C:\x\clean.bat", power: true).IsValid);
        Assert.True(Check("keys.0", "OpenApplication", @"C:\Program Files\App\app.exe").IsValid);
    }

    [Fact]
    public void WithoutLimits_NothingIsRefused_ItsTheUsersOwnScript()
    {
        var result = ProfileValidator.Validate(new[] { new ControlChange { Control = "keys.3", Action = "SystemShutdown" } }, 1);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ALookOnlyChange_IsNeverBlocked()
    {
        var result = ProfileValidator.Validate(new[] { new ControlChange { Control = "keys.3", Title = "Hi" } }, 1, new AssistantLimits());

        Assert.True(result.IsValid);
    }
}

/// <summary>T-26: the MCP server, over the real local API.</summary>
public sealed class McpTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_mcp_" + Guid.NewGuid().ToString("N"));
    private readonly FakeProtection _protection = new();
    private readonly ProfileService _profiles;
    private readonly LocalApiServer _server;
    private readonly FakeHost _host = new();
    private int _connects;

    private sealed class FakeProtection : ISecretProtectionService
    {
        public bool IsProtected(string? value) => value?.StartsWith("enc:v1:") == true;
        public string? Protect(string? plainText) => string.IsNullOrEmpty(plainText) || IsProtected(plainText) ? plainText : "enc:v1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        public string? Unprotect(string? cipherText) => IsProtected(cipherText) ? Encoding.UTF8.GetString(Convert.FromBase64String(cipherText!["enc:v1:".Length..])) : cipherText;
    }

    private sealed class FakeHost : ILocalApiHost
    {
        public List<string> Activated { get; } = new();
        public List<(LightsMode Mode, string? Color)> Lights { get; } = new();
        public bool ActivateProfile(string profileId) { Activated.Add(profileId); return true; }
        public void FlushActiveProfile() { }
        public void ProfileChanged(string profileId) { }
        public bool SetLights(LightsMode mode, string? colorHex) { Lights.Add((mode, colorHex)); return true; }
    }

    public McpTests()
    {
        _profiles = new ProfileService(_dir, _protection);
        _server = new LocalApiServer(_profiles, _protection, options: new LocalApiOptions { PreferredPort = 0, BaseDirectory = _dir }) { Host = _host };
        _server.Start();
    }

    public void Dispose()
    {
        _server.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private McpServer Server(bool power = false) => new(() =>
    {
        _connects++;
        return new LocalApiClient(ApiConnection.Read(_dir, _protection), assistant: power ? "assistant-power" : "assistant");
    });

    private static async Task<JsonObject> Ask(McpServer server, string method, object? args = null, int id = 1)
    {
        string request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = args == null ? null : JsonNode.Parse(JsonSerializer.Serialize(args)) }.ToJsonString();
        return (JsonObject)JsonNode.Parse((await server.HandleAsync(request))!)!;
    }

    private static async Task<(bool IsError, string Text)> Call(McpServer server, string tool, object? arguments = null)
    {
        var answer = await Ask(server, "tools/call", new { name = tool, arguments = arguments ?? new { } });
        var result = answer["result"]!.AsObject();
        return (result["isError"]!.GetValue<bool>(), result["content"]![0]!["text"]!.GetValue<string>());
    }

    private string ActiveId => _profiles.GetActiveProfile().Id;

    // ── protocol ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2025-06-18")]
    [InlineData("2025-03-26")]
    [InlineData("2024-11-05")]
    public async Task Initialize_EchoesAVersionItSpeaks(string version)
    {
        var answer = await Ask(Server(), "initialize", new { protocolVersion = version, capabilities = new { }, clientInfo = new { name = "t", version = "1" } });

        Assert.Equal(version, answer["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("masterhub", answer["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.NotNull(answer["result"]!["capabilities"]!["tools"]);
    }

    [Fact]
    public async Task Initialize_WithAnUnknownVersion_AnswersWithItsNewest()
    {
        var answer = await Ask(Server(), "initialize", new { protocolVersion = "1999-01-01" });

        Assert.Equal(McpServer.SupportedVersions[0], answer["result"]!["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task ANotification_GetsNoAnswer()
    {
        Assert.Null(await Server().HandleAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
    }

    [Fact]
    public async Task Ping_AndUnknownMethods_AndBrokenInput_AreAnsweredProperly()
    {
        var server = Server();

        Assert.NotNull((await Ask(server, "ping"))["result"]);
        Assert.Equal(-32601, (await Ask(server, "nope"))["error"]!["code"]!.GetValue<int>());

        var broken = JsonNode.Parse((await server.HandleAsync("{not json"))!)!;
        Assert.Equal(-32700, broken["error"]!["code"]!.GetValue<int>());
        Assert.Null(broken["id"]);
    }

    [Fact]
    public async Task TheAnswerCarriesTheRequestsId()
    {
        var answer = await Ask(Server(), "ping", id: 42);

        Assert.Equal(42, answer["id"]!.GetValue<int>());
        Assert.Equal("2.0", answer["jsonrpc"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsList_HasTheDesignTools_AndNoRawHidTool()
    {
        var answer = await Ask(Server(), "tools/list");
        var names = answer["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToList();

        Assert.Equal(new[] { "design_guide", "list_modules", "list_actions", "list_profiles", "get_profile", "activate_profile", "set_control", "clear_control", "set_lights" }, names);
        Assert.DoesNotContain(names, n => n.Contains("hid", StringComparison.OrdinalIgnoreCase) || n.Contains("raw", StringComparison.OrdinalIgnoreCase));
        Assert.All(answer["result"]!["tools"]!.AsArray(), t => Assert.Equal("object", t!["inputSchema"]!["type"]!.GetValue<string>()));
    }

    [Fact]
    public async Task AnUnknownTool_IsAProtocolError()
    {
        var answer = await Ask(Server(), "tools/call", new { name = "format_disk" });

        Assert.Equal(-32602, answer["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task TheDesignGuide_IsAResourceAndATool_AndStatesTheHardwareFacts()
    {
        var server = Server();

        var resource = await Ask(server, "resources/read", new { uri = McpServer.DesignGuideUri });
        string text = resource["result"]!["contents"]![0]!["text"]!.GetValue<string>();
        var (isError, tool) = await Call(server, "design_guide");

        Assert.False(isError);
        Assert.Equal(text, tool);
        Assert.Contains("72×72", text);
        Assert.Contains("no lights", text);
        Assert.Contains(":hold", text);
        Assert.Equal(-32002, (await Ask(server, "resources/read", new { uri = "masterhub://other" }))["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task TheLoop_AnswersEachRequestOnItsOwnLine_AndSkipsNotifications()
    {
        var input = new StringReader(
            """{"jsonrpc":"2.0","id":1,"method":"ping"}""" + "\n" +
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""" + "\n\n" +
            """{"jsonrpc":"2.0","id":2,"method":"ping"}""" + "\n");
        var output = new StringWriter();

        await Server().RunAsync(input, output);

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal(new[] { 1, 2 }, lines.Select(l => JsonNode.Parse(l)!["id"]!.GetValue<int>()));
    }

    // ── tools over the real API ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Profiles_AreListedAndOneIsRead()
    {
        _profiles.CreateProfile("Streaming", setAsActive: false);
        var server = Server();

        var (_, list) = await Call(server, "list_profiles");
        var (_, one) = await Call(server, "get_profile", new { profile = "stream" });

        using var doc = JsonDocument.Parse(list);
        Assert.Equal(ActiveId, doc.RootElement.GetProperty("active").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("profiles").GetArrayLength());
        Assert.Contains("Streaming", one);
    }

    [Fact]
    public async Task APartOfAnAmbiguousName_IsRefusedWithTheCandidates()
    {
        _profiles.CreateProfile("Focus work", setAsActive: false);
        _profiles.CreateProfile("Focus music", setAsActive: false);

        var (isError, text) = await Call(Server(), "activate_profile", new { profile = "focus" });

        Assert.True(isError);
        Assert.Contains("Focus music", text);
        Assert.Empty(_host.Activated);
    }

    [Fact]
    public async Task ActivateProfile_PutsItOnTheDeck()
    {
        var other = _profiles.CreateProfile("Streaming", setAsActive: false);

        var (isError, _) = await Call(Server(), "activate_profile", new { profile = "Streaming" });

        Assert.False(isError);
        Assert.Equal(new[] { other.Id }, _host.Activated);
    }

    [Fact]
    public async Task SetControl_ChangesAKey_AndOmittedProfileMeansTheActiveOne()
    {
        var (isError, text) = await Call(Server(), "set_control", new { control = "keys.2", action = "MediaNext", title = "Next", background = "#112233" });

        Assert.False(isError, text);
        var key = _profiles.GetActiveProfile().Keys.Single(k => k.Index == 2);
        Assert.Equal(DeckActionType.MediaNext, key.Action);
        Assert.Equal("Next", key.CustomTitle);
        Assert.Equal("#112233", key.BackgroundColorHex);
    }

    [Fact]
    public async Task ARefusedShutdownOnAPress_ChangesNothing_AndTheModelIsToldWhy()
    {
        var (isError, text) = await Call(Server(), "set_control", new { control = "keys.4", action = "SystemShutdown" });

        Assert.True(isError);
        Assert.Contains("only accepted on a hold", text);
        Assert.Contains("keys.4:hold", text);
        Assert.DoesNotContain(_profiles.GetActiveProfile().Keys, k => k.Index == 4 && k.Action == DeckActionType.SystemShutdown);

        var (ok, _) = await Call(Server(), "set_control", new { control = "keys.4:hold", action = "SystemShutdown" });
        Assert.False(ok);
        Assert.Equal(DeckActionType.SystemShutdown, _profiles.GetActiveProfile().Keys.Single(k => k.Index == 4).Gestures!.Hold!.Action);
    }

    [Fact]
    public async Task AServiceCall_IsRefusedForTheAssistant_AndAllowedWithMorePower()
    {
        var args = new { control = "keys.5", action = "HomeAssistantCallService", parameter = "light.turn_on" };

        var (refused, text) = await Call(Server(), "set_control", args);
        Assert.True(refused);
        Assert.Contains("HomeAssistantTurnOn", text);

        var (error, _) = await Call(Server(power: true), "set_control", args);
        Assert.False(error);
    }

    [Fact]
    public async Task ClearControl_EmptiesAKey()
    {
        var server = Server();
        await Call(server, "set_control", new { control = "keys.1", action = "MediaNext" });

        var (isError, _) = await Call(server, "clear_control", new { control = "keys.1" });

        Assert.False(isError);
        Assert.Equal(DeckActionType.None, _profiles.GetActiveProfile().Keys.Single(k => k.Index == 1).Action);
    }

    [Fact]
    public async Task SetLights_ReachesTheApp_AndABadModeIsExplained()
    {
        var server = Server();

        var (ok, _) = await Call(server, "set_lights", new { mode = "color", color = "#ff8800" });
        var (bad, text) = await Call(server, "set_lights", new { mode = "disco" });

        Assert.False(ok);
        Assert.Equal((LightsMode.Color, "#ff8800"), Assert.Single(_host.Lights));
        Assert.True(bad);
        Assert.Contains("not known", text);
    }

    [Fact]
    public async Task ListActions_ShowsTheLimits_AndCanBeSearched()
    {
        var (_, all) = await Call(Server(), "list_actions");
        var (_, some) = await Call(Server(), "list_actions", new { search = "shutdown" });

        Assert.Contains("HomeAssistantCallService", all);
        Assert.Contains("only on a hold", some);
        Assert.DoesNotContain("MediaNext", some);
        Assert.DoesNotContain("StreamDeckPluginAction", all);
    }

    // ── connection ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryToolCall_ReadsThePortAndTokenAgain()
    {
        var server = Server();

        await Call(server, "list_modules");
        await Call(server, "list_modules");
        await Call(server, "list_profiles");

        Assert.Equal(3, _connects);
    }

    [Fact]
    public async Task WhenTheAppIsNotRunning_TheToolSaysSo_InsteadOfFailing()
    {
        var server = new McpServer(() => throw new MasterHubNotRunningException("MasterHub is not running (no port file)."));

        var (isError, text) = await Call(server, "list_profiles");

        Assert.True(isError);
        Assert.Contains("not running", text);
    }

    [Fact]
    public async Task TheCliRunsTheServer_OnItsInputAndOutput()
    {
        var input = new StringReader("""{"jsonrpc":"2.0","id":7,"method":"ping"}""" + "\n");
        var output = new StringWriter();
        var runner = new CliRunner(input: input, connectAssistant: () => new LocalApiClient(ApiConnection.Read(_dir, _protection), assistant: "assistant"));

        int code = await runner.RunAsync(new[] { "--mcp" }, output, new StringWriter());

        Assert.Equal(0, code);
        Assert.Equal(7, JsonNode.Parse(output.ToString().Trim())!["id"]!.GetValue<int>());
    }
}
