using System.Net;
using System.Text;
using System.Text.Json;
using MasterHub.Cli;
using MasterHub.Core.Actions;
using MasterHub.Core.Api;
using MasterHub.Core.Api.Client;
using MasterHub.Core.Profiles;
using MasterHub.Core.Security;

namespace MasterHub.Core.Tests;

/// <summary>T-24: the arguments, the profile lookup and the checks that do not need an app.</summary>
public sealed class CliPartsTests
{
    private static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase) { "label", "page" };
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "clear", "json" };

    private static CliArguments Parse(params string[] args)
    {
        var parsed = CliArguments.Parse(args, Values, Flags, out string? problem);
        Assert.Null(problem);
        return parsed!;
    }

    [Fact]
    public void Arguments_AreSplitIntoWords_Options_AndFlags()
    {
        var a = Parse("3", "--label", "Build OK", "--clear", "extra", "--page=2");

        Assert.Equal(new[] { "3", "extra" }, a.Positionals);
        Assert.Equal("Build OK", a.Value("label"));
        Assert.Equal("2", a.Value("page"));
        Assert.True(a.Has("clear"));
        Assert.False(a.Has("json"));
    }

    [Fact]
    public void AValue_MayStartWithADash()
    {
        var a = Parse("--label", "-5", "--page", "--weird");

        Assert.Equal("-5", a.Value("label"));
        Assert.Equal("--weird", a.Value("page"));
    }

    [Theory]
    [InlineData("--nope", "Unknown option --nope")]
    [InlineData("--label", "--label needs a value")]
    [InlineData("--clear=1", "--clear does not take a value")]
    public void ABadCommandLine_SaysWhatIsWrong(string arg, string expected)
    {
        var parsed = CliArguments.Parse(new[] { arg }, Values, Flags, out string? problem);

        Assert.Null(parsed);
        Assert.Contains(expected, problem);
    }

    private static readonly List<ApiProfile> Profiles = new()
    {
        new("aaa111", "Default", true),
        new("bbb222", "Focus work", false),
        new("ccc333", "Focus music", false),
        new("ddd444", "Streaming", false)
    };

    [Theory]
    [InlineData("bbb222", "Focus work")]      // an id
    [InlineData("streaming", "Streaming")]    // a name, any case
    [InlineData("Stream", "Streaming")]       // the only name that starts with it
    [InlineData("music", "Focus music")]      // the only name that contains it
    public void AProfile_IsFoundByIdOrName(string query, string expected)
    {
        var profile = ProfileResolver.Resolve(Profiles, query, out string? problem);

        Assert.Null(problem);
        Assert.Equal(expected, profile!.Name);
    }

    [Fact]
    public void AnAmbiguousName_IsRefused_ListingTheCandidates()
    {
        var profile = ProfileResolver.Resolve(Profiles, "focus", out string? problem);

        Assert.Null(profile);
        Assert.Contains("Focus work", problem);
        Assert.Contains("Focus music", problem);
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("")]
    [InlineData("  ")]
    public void AnUnknownOrEmptyName_IsRefused(string query)
    {
        Assert.Null(ProfileResolver.Resolve(Profiles, query, out string? problem));
        Assert.False(string.IsNullOrEmpty(problem));
    }

    // ── what the validator learned for the CLI ──────────────────────────────────────────────

    [Fact]
    public void AChangeWithoutAnAction_ThatOnlyChangesTheLook_IsValid_AndKeepsTheAction()
    {
        var look = new ControlChange { Control = "keys.2", Title = "Build OK", BackgroundColorHex = "#00aa00" };
        Assert.True(ProfileValidator.Validate(new[] { look }).IsValid);

        var profile = ProfileModel.CreateEmpty("p");
        ProfileValidator.Apply(profile, new[] { new ControlChange { Control = "keys.2", Action = "MediaNext", Title = "Next" } });
        ProfileValidator.Apply(profile, new[] { look });

        var key = profile.Keys.Single(k => k.Index == 2);
        Assert.Equal(DeckActionType.MediaNext, key.Action);   // what it does is untouched
        Assert.Equal("Build OK", key.CustomTitle);
        Assert.Equal("#00aa00", key.BackgroundColorHex);
    }

    [Fact]
    public void AChangeWithNothingToChange_StillNeedsAnAction()
    {
        var result = ProfileValidator.Validate(new[] { new ControlChange { Control = "keys.2" } });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("action is required"));
    }

    [Fact]
    public void ALookOnlyChange_IsStillChecked()
    {
        var result = ProfileValidator.Validate(new[] { new ControlChange { Control = "keys.2", BackgroundColorHex = "red" } });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("#rrggbb"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void APageThatDoesNotExist_IsRefused(int page)
    {
        var result = ProfileValidator.Validate(new[] { new ControlChange { Control = "keys.0", Title = "x", Page = page } }, pageCount: 2);

        Assert.Contains(result.Errors, e => e.Contains("page") && e.Contains("does not exist"));
    }

    [Fact]
    public void AChangeOnPageTwo_TouchesOnlyPageTwo()
    {
        var profile = ProfileModel.CreateEmpty("p");
        profile.Pages.Add(new KeyPageModel { PageIndex = 1, Name = "Two" });

        ProfileValidator.Apply(profile, new[] { new ControlChange { Control = "keys.4", Action = "MediaStop", Title = "Stop", Page = 2 } });

        Assert.Equal("Stop", profile.Pages[1].Keys.Single(k => k.Index == 4).CustomTitle);
        Assert.DoesNotContain(profile.Keys, k => k.Index == 4 && k.Action == DeckActionType.MediaStop);
        Assert.DoesNotContain(profile.Pages[0].Keys, k => k.Index == 4 && k.Action == DeckActionType.MediaStop);
    }

    [Theory]
    [InlineData("rainbow", null, LightsMode.Rainbow)]
    [InlineData("OFF", null, LightsMode.Off)]
    [InlineData("profile", null, LightsMode.Profile)]
    [InlineData("color", "#ff8800", LightsMode.Color)]
    public void Lights_AreValidated(string mode, string? color, LightsMode expected)
    {
        var (parsed, parsedColor, result) = ProfileValidator.ValidateLights(new LightsRequest { Mode = mode, Color = color });

        Assert.True(result.IsValid);
        Assert.Equal(expected, parsed);
        Assert.Equal(color, parsedColor);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("disco", null)]
    [InlineData("7", null)]                 // a number is not a mode name
    [InlineData("color", null)]             // a colour is needed
    [InlineData("color", "orange")]         // and it must be #rrggbb
    [InlineData("rainbow", "#ff0000")]      // a colour only belongs to "color"
    public void BadLights_AreRefused(string mode, string? color)
    {
        Assert.False(ProfileValidator.ValidateLights(new LightsRequest { Mode = mode, Color = color }).Result.IsValid);
        Assert.False(ProfileValidator.ValidateLights(null).Result.IsValid);
    }
}

/// <summary>T-24: the CLI against the real local API (a real server on an ephemeral port, a fake app host). Never the GUI exe.</summary>
public sealed class CliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_cli_" + Guid.NewGuid().ToString("N"));
    private readonly ProfileService _profiles;
    private readonly LocalApiServer _server;
    private readonly FakeHost _host = new();
    private readonly FakeProtection _protection = new();

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
        public bool LightsWork { get; set; } = true;
        public bool ActivateProfile(string profileId) { Activated.Add(profileId); return true; }
        public void FlushActiveProfile() { }
        public void ProfileChanged(string profileId) { }
        public bool SetLights(LightsMode mode, string? colorHex) { Lights.Add((mode, colorHex)); return LightsWork; }
    }

    public CliTests()
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

    private CliRunner Runner() => new(() => new LocalApiClient(ApiConnection.Read(_dir, _protection)));

    private async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int code = await Runner().RunAsync(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private ProfileModel Home => _profiles.GetAllProfiles().Single(p => p.IsDefault);

    private ProfileModel AddProfile(string name)
    {
        var p = _profiles.CreateProfile(name, setAsActive: false);
        return p;
    }

    // ── status, profiles, profile ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_SaysWhoIsRunningAndWhichProfileIsOnTheDeck()
    {
        var (code, output, error) = await Run("status");

        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Contains("MasterHub", output);
        Assert.Contains("API 1", output);
        Assert.Contains(_profiles.GetActiveProfile().Name, output);
        Assert.Contains("Deck:", output);
        Assert.Contains("no module connected", output);   // there is no hardware in a test
    }

    [Fact]
    public async Task Status_AsJson_IsOneJsonDocument()
    {
        var (code, output, _) = await Run("status", "--json");

        Assert.Equal(0, code);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetProperty("api").GetInt32());
        Assert.Equal(_profiles.GetActiveProfile().Id, doc.RootElement.GetProperty("state").GetProperty("activeProfileId").GetString());
        Assert.True(doc.RootElement.TryGetProperty("modules", out _));
    }

    [Fact]
    public async Task Profiles_MarksTheActiveOneAndHome()
    {
        AddProfile("Streaming");

        var (code, output, _) = await Run("profiles");

        Assert.Equal(0, code);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Assert.Equal(2, lines.Count);
        Assert.StartsWith("*^ ", lines.Single(l => l.Contains(Home.Id)));   // the active one is also Home here
        Assert.StartsWith("   ", lines.Single(l => l.Contains("Streaming")));
    }

    [Fact]
    public async Task Profile_PutsTheNamedProfileOnTheDeck()
    {
        var focus = AddProfile("Focus work");
        AddProfile("Streaming");

        var (code, output, _) = await Run("profile", "focus");

        Assert.Equal(0, code);
        Assert.Contains("Focus work", output);
        Assert.Equal(new[] { focus.Id }, _host.Activated);
    }

    [Fact]
    public async Task Profile_WithAnAmbiguousOrUnknownName_ChangesNothing()
    {
        AddProfile("Focus work");
        AddProfile("Focus music");

        var ambiguous = await Run("profile", "focus");
        var unknown = await Run("profile", "nothing");
        var none = await Run("profile");

        Assert.All(new[] { ambiguous, unknown, none }, r =>
        {
            Assert.Equal(1, r.Code);
            Assert.Equal("", r.Out);
            Assert.StartsWith("error:", r.Err);
        });
        Assert.Contains("Focus music", ambiguous.Err);
        Assert.Empty(_host.Activated);
    }

    // ── key ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Key_ChangesTheLabelAndColour_AndKeepsWhatTheKeyDoes()
    {
        var mine = _profiles.GetActiveProfile();
        ProfileValidator.Apply(mine, new[] { new ControlChange { Control = "keys.14", Action = "MediaNext", Title = "Next" } });
        _profiles.SaveProfile(mine);

        var (code, output, error) = await Run("key", "15", "--label", "Build OK", "--color", "#00aa00");

        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.Contains("Key 15", output);
        var key = _profiles.GetActiveProfile().Keys.Single(k => k.Index == 14);   // keys are numbered from 1, indexes from 0
        Assert.Equal("Build OK", key.CustomTitle);
        Assert.Equal("#00aa00", key.BackgroundColorHex);
        Assert.Equal(DeckActionType.MediaNext, key.Action);
    }

    [Fact]
    public async Task Key_CanSetAnActionAndClearAKey()
    {
        var (code, _, _) = await Run("key", "1", "--action", "Hotkey", "--param", "Ctrl+Shift+M", "--label", "Mute");
        Assert.Equal(0, code);
        var key = _profiles.GetActiveProfile().Keys.Single(k => k.Index == 0);
        Assert.Equal(DeckActionType.Hotkey, key.Action);
        Assert.Equal("Ctrl+Shift+M", key.ActionParameter);

        var (clearCode, output, _) = await Run("key", "1", "--clear");
        Assert.Equal(0, clearCode);
        Assert.Contains("empty", output);
        Assert.Equal(DeckActionType.None, _profiles.GetActiveProfile().Keys.Single(k => k.Index == 0).Action);
    }

    [Fact]
    public async Task Key_CanTargetAnotherProfile_AndAnotherPage()
    {
        var other = AddProfile("Streaming");
        other.Pages.Add(new KeyPageModel { PageIndex = 1, Name = "Two" });
        _profiles.SaveProfile(other);

        var (code, _, error) = await Run("key", "5", "--label", "Live", "--profile", "stream", "--page", "2");

        Assert.Equal(0, code);
        Assert.Equal("", error);
        var saved = _profiles.GetAllProfiles().Single(p => p.Id == other.Id);
        Assert.Equal("Live", saved.Pages[1].Keys.Single(k => k.Index == 4).CustomTitle);
        Assert.DoesNotContain(_profiles.GetActiveProfile().Keys, k => k.CustomTitle == "Live");   // the active profile was not touched
    }

    [Theory]
    [InlineData("key")]                                              // no key number
    [InlineData("key", "0", "--label", "x")]                         // numbered from 1
    [InlineData("key", "16", "--label", "x")]                        // there are 15
    [InlineData("key", "abc", "--label", "x")]
    [InlineData("key", "3")]                                         // nothing to change
    [InlineData("key", "3", "--clear", "--label", "x")]              // clear is alone
    [InlineData("key", "3", "--param", "x")]                         // a parameter belongs to an action
    [InlineData("key", "3", "--label", "x", "--color", "green")]     // colours are #rrggbb
    [InlineData("key", "3", "--label", "x", "--page", "0")]
    [InlineData("key", "3", "--label", "x", "--icon", "star")]       // not available: said plainly
    [InlineData("key", "3", "--label", "x", "--bogus", "1")]
    public async Task Key_WithABadCommandLine_ChangesNothing_AndSaysWhy(params string[] args)
    {
        string before = JsonSerializer.Serialize(_profiles.GetActiveProfile());

        var (code, output, error) = await Run(args);

        Assert.Equal(1, code);
        Assert.Equal("", output);
        Assert.StartsWith("error:", error);
        Assert.Equal(before, JsonSerializer.Serialize(_profiles.GetActiveProfile()));
    }

    [Fact]
    public async Task Key_RefusedByTheApp_PrintsTheAppsReasons_AndChangesNothing()
    {
        string before = JsonSerializer.Serialize(_profiles.GetActiveProfile());

        var (code, output, error) = await Run("key", "2", "--action", "NotAnAction");

        Assert.Equal(1, code);
        Assert.Equal("", output);
        Assert.Contains("nothing was changed", error);
        Assert.Contains("is not a known action", error);
        Assert.Equal(before, JsonSerializer.Serialize(_profiles.GetActiveProfile()));
    }

    [Fact]
    public async Task Key_OnAPageThatDoesNotExist_IsRefusedByTheApp()
    {
        var (code, _, error) = await Run("key", "2", "--label", "x", "--page", "4");

        Assert.Equal(1, code);
        Assert.Contains("page 4 does not exist", error);
    }

    [Fact]
    public async Task Key_WithALongLabel_WarnsOnTheErrorWriter_ButSucceeds()
    {
        var (code, output, error) = await Run("key", "2", "--label", "A label that is too long");

        Assert.Equal(0, code);
        Assert.Contains("updated", output);
        Assert.Contains("warning:", error);
    }

    // ── lights ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("rainbow", null, LightsMode.Rainbow)]
    [InlineData("off", null, LightsMode.Off)]
    [InlineData("profile", null, LightsMode.Profile)]
    [InlineData("color", "#ff8800", LightsMode.Color)]
    public async Task Lights_AreHandedToTheApp(string mode, string? color, LightsMode expected)
    {
        var args = color == null ? new[] { "lights", mode } : new[] { "lights", mode, color };

        var (code, output, error) = await Run(args);

        Assert.Equal(0, code);
        Assert.Equal("", error);
        Assert.NotEqual("", output);
        Assert.Equal((expected, color), Assert.Single(_host.Lights));
    }

    [Theory]
    [InlineData("lights")]
    [InlineData("lights", "disco")]
    [InlineData("lights", "color")]
    [InlineData("lights", "color", "orange")]
    [InlineData("lights", "rainbow", "#ff0000")]
    [InlineData("lights", "color", "#ff0000", "extra")]
    public async Task BadLights_AreRefused_BeforeTheyReachTheApp(params string[] args)
    {
        var (code, _, error) = await Run(args);

        Assert.Equal(1, code);
        Assert.StartsWith("error:", error);
        Assert.Empty(_host.Lights);
    }

    [Fact]
    public async Task WhenTheAppCannotChangeTheLights_TheCliSaysSo()
    {
        _host.LightsWork = false;

        var (code, _, error) = await Run("lights", "off");

        Assert.Equal(1, code);
        Assert.Contains("could not change the lights", error);
    }

    // ── doctor ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Doctor_PassesWhenTheAppAnswers_AndOnlyWarnsAboutAMissingDeck()
    {
        var (code, output, _) = await Run("doctor");

        Assert.Equal(0, code);
        Assert.Contains("[ok]   MasterHub is running", output);
        Assert.Contains("[ok]   The app accepts the token", output);
        Assert.Contains("[ok]   A profile is active", output);
        Assert.Contains("[warn] A deck is connected", output);
        Assert.DoesNotContain("[fail]", output);
    }

    [Fact]
    public async Task Doctor_FailsWhenTheAppIsNotRunning()
    {
        _server.Stop();   // the app closes: its port and token files go away

        var (code, output, _) = await Run("doctor");

        Assert.Equal(1, code);
        Assert.Contains("[fail] MasterHub is running", output);
        Assert.Contains("not running", output);
    }

    // ── an app that is not running ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("status")]
    [InlineData("profiles")]
    [InlineData("profile", "x")]
    [InlineData("key", "1", "--label", "x")]
    [InlineData("lights", "off")]
    public async Task WithoutARunningApp_EveryCommand_ExitsWithOne_AndSaysItIsNotRunning(params string[] args)
    {
        _server.Stop();

        var (code, output, error) = await Run(args);

        Assert.Equal(1, code);
        Assert.Equal("", output);
        Assert.Contains("MasterHub is not running", error);
    }

    [Fact]
    public async Task StaleFilesOfACrashedApp_AreTakenForANotRunningApp()
    {
        // a crash leaves api-port / api-token behind, but nothing listens on that port any more
        string portFile = Path.Combine(_dir, "api-port");
        string tokenFile = Path.Combine(_dir, "api-token");
        int deadPort = _server.Port;
        string token = File.ReadAllText(tokenFile);
        _server.Stop();
        File.WriteAllText(portFile, deadPort.ToString());
        File.WriteAllText(tokenFile, token);

        var (code, _, error) = await Run("status");

        Assert.Equal(1, code);
        Assert.Contains("MasterHub is not running", error);
    }

    [Fact]
    public async Task ATokenThatChanged_IsExplained()
    {
        File.WriteAllText(Path.Combine(_dir, "api-token"), _protection.Protect("an-old-token")!);

        var (code, _, error) = await Run("status");

        Assert.Equal(1, code);
        Assert.Contains("token", error);
    }

    // ── help and the rest ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Help_IsPrinted_WithExitCodeZero()
    {
        foreach (var args in new[] { new[] { "help" }, new[] { "--help" }, new[] { "key", "--help" } })
        {
            var (code, output, _) = await Run(args);
            Assert.Equal(0, code);
            Assert.Contains("masterhub-cli key <1-15>", output);
        }
    }

    [Fact]
    public async Task NoCommand_PrintsTheHelpToTheErrorWriter_AndFails()
    {
        var (code, output, error) = await Run();

        Assert.Equal(1, code);
        Assert.Equal("", output);
        Assert.Contains("Usage:", error);
    }

    [Fact]
    public async Task AnUnknownCommand_Fails_AndSaysSo()
    {
        var (code, _, error) = await Run("dance");

        Assert.Equal(1, code);
        Assert.Contains("Unknown command \"dance\"", error);
    }

    [Fact]
    public async Task Version_NeedsNoApp()
    {
        _server.Stop();

        var (code, output, _) = await Run("--version");

        Assert.Equal(0, code);
        Assert.Contains("masterhub-cli", output);
        Assert.Contains("API 1", output);
    }

    // ── the HTTP client on its own ──────────────────────────────────────────────────────────

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond { get; set; } = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? RespondWithToken { get; set; }
        public List<(HttpRequestMessage Request, string Body)> Seen { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Seen.Add((request, body));
            return RespondWithToken != null ? await RespondWithToken(request, cancellationToken) : await Respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static LocalApiClient ClientFor(StubHandler handler, TimeSpan? timeout = null) =>
        new(new ApiConnection(28900, "tok"), handler, timeout);

    [Fact]
    public async Task TheClient_SendsTheBearerToken_AndJsonBodies()
    {
        var handler = new StubHandler { Respond = _ => Task.FromResult(Json(HttpStatusCode.OK, "{\"changed\":1}")) };
        using var client = ClientFor(handler);

        var result = await client.SetControlsAsync("p1", new[] { new ControlChange { Control = "keys.0", Title = "x" } });

        Assert.Equal(1, result.Changed);
        var (request, body) = Assert.Single(handler.Seen);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("tok", request.Headers.Authorization.Parameter);
        Assert.Equal("http://127.0.0.1:28900/api/profiles/p1/controls", request.RequestUri!.ToString());
        Assert.StartsWith("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("\"control\":\"keys.0\"", body);
        Assert.False(request.Headers.Contains("Origin"));   // the app refuses a request that has one
    }

    [Fact]
    public async Task ARefusedRequest_CarriesTheAppsReasons()
    {
        var handler = new StubHandler
        {
            Respond = _ => Task.FromResult(Json((HttpStatusCode)422, "{\"error\":\"validation failed\",\"errors\":[\"a is wrong\",\"b is wrong\"],\"warnings\":[\"w\"]}"))
        };
        using var client = ClientFor(handler);

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.SetControlsAsync("p", new[] { new ControlChange { Control = "keys.0", Title = "x" } }));

        Assert.Equal(422, ex.Status);
        Assert.Equal("validation failed", ex.Message);
        Assert.Equal(new[] { "a is wrong", "b is wrong" }, ex.Errors);
        Assert.Equal(new[] { "w" }, ex.Warnings);
    }

    [Fact]
    public async Task AnAnswerThatIsNotJson_StillGivesAUsefulMessage()
    {
        var handler = new StubHandler { Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>oops</html>") }) };
        using var client = ClientFor(handler);

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetStateAsync());

        Assert.Equal(502, ex.Status);
        Assert.Contains("502", ex.Message);
    }

    [Fact]
    public async Task ARefusedConnection_IsANotRunningApp()
    {
        var handler = new StubHandler { Respond = _ => throw new HttpRequestException("refused") };
        using var client = ClientFor(handler);

        var ex = await Assert.ThrowsAsync<MasterHubNotRunningException>(() => client.GetVersionAsync());

        Assert.Contains("28900", ex.Message);
    }

    [Fact]
    public async Task AnAppThatDoesNotAnswer_TimesOutWithAClearMessage()
    {
        var handler = new StubHandler { RespondWithToken = async (r, ct) => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return new HttpResponseMessage(HttpStatusCode.OK); } };
        using var client = ClientFor(handler, TimeSpan.FromMilliseconds(150));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetVersionAsync());

        Assert.Equal(0, ex.Status);
        Assert.Contains("did not answer", ex.Message);
    }

    [Fact]
    public void TheConnection_IsReadFresh_AndAMissingFileMeansNotRunning()
    {
        string empty = Path.Combine(_dir, "nothing-here");
        Directory.CreateDirectory(empty);

        var ex = Assert.Throws<MasterHubNotRunningException>(() => ApiConnection.Read(empty, _protection));
        Assert.Contains("not running", ex.Message);

        File.WriteAllText(Path.Combine(empty, "api-port"), "not a number");
        File.WriteAllText(Path.Combine(empty, "api-token"), "x");
        Assert.Throws<MasterHubNotRunningException>(() => ApiConnection.Read(empty, _protection));

        File.WriteAllText(Path.Combine(empty, "api-port"), "28901");
        var connection = ApiConnection.Read(empty, _protection);
        Assert.Equal(28901, connection.Port);
        Assert.Equal("x", connection.Token);
    }
}
