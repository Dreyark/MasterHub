using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MasterHub.Core.Actions;
using MasterHub.Core.Api;
using MasterHub.Core.Profiles;
using MasterHub.Core.Security;

namespace MasterHub.Core.Tests;

/// <summary>T-04: the loopback API on an ephemeral port – authentication, browser/DNS-rebinding defences, limits, all-or-nothing writes.</summary>
public sealed class LocalApiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_api_" + Guid.NewGuid().ToString("N"));
    private readonly ProfileService _profiles;
    private readonly LocalApiServer _server;
    private readonly HttpClient _http = new();
    private readonly FakeHost _host = new();

    private sealed class FakeProtection : ISecretProtectionService
    {
        public bool IsProtected(string? value) => value?.StartsWith("enc:v1:") == true;
        public string? Protect(string? plainText) => string.IsNullOrEmpty(plainText) || IsProtected(plainText) ? plainText : "enc:v1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        public string? Unprotect(string? cipherText) => IsProtected(cipherText) ? Encoding.UTF8.GetString(Convert.FromBase64String(cipherText!["enc:v1:".Length..])) : cipherText;
    }

    private sealed class FakeHost : ILocalApiHost
    {
        public List<string> Activated { get; } = new();
        public int Flushes { get; private set; }
        public List<string> Changed { get; } = new();
        public bool ActivateProfile(string profileId) { Activated.Add(profileId); return true; }
        public void FlushActiveProfile() => Flushes++;
        public void ProfileChanged(string profileId) => Changed.Add(profileId);
    }

    public LocalApiTests()
    {
        _profiles = new ProfileService(_dir, new FakeProtection());
        _server = new LocalApiServer(_profiles, new FakeProtection(), options: new LocalApiOptions
        {
            PreferredPort = 0,           // ephemeral
            BaseDirectory = _dir,
            MaxBodyBytes = 4096
        })
        { Host = _host };
        _server.Start();
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Url(string path) => $"http://127.0.0.1:{_server.Port}{path}";

    private HttpRequestMessage Request(HttpMethod method, string path, string? json = null, bool auth = true)
    {
        var request = new HttpRequestMessage(method, Url(path));
        if (auth) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _server.Token);
        if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpRequestMessage request)
    {
        using var response = await _http.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        JsonElement body = default;
        if (!string.IsNullOrWhiteSpace(text)) body = JsonDocument.Parse(text).RootElement.Clone();
        return (response.StatusCode, body);
    }

    // ── authentication & browser defences ──────────────────────────────────────────────────

    [Fact]
    public async Task MissingOrWrongToken_Is401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(Request(HttpMethod.Get, "/api/state", auth: false))).Status);

        var wrong = Request(HttpMethod.Get, "/api/state", auth: false);
        wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(wrong)).Status);

        var basic = Request(HttpMethod.Get, "/api/state", auth: false);
        basic.Headers.Authorization = new AuthenticationHeaderValue("Basic", _server.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(basic)).Status);
    }

    [Fact]
    public async Task OriginHeader_Is403_EvenWithAValidToken()
    {
        var request = Request(HttpMethod.Get, "/api/state");
        request.Headers.Add("Origin", "https://evil.example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(request)).Status);
    }

    [Fact]
    public async Task ForeignHost_IsRejected()
    {
        var request = Request(HttpMethod.Get, "/api/state");
        request.Headers.Host = "evil.example.com";
        var status = (await Send(request)).Status;

        // http.sys answers 400 before our own 403 check can run; either way the request never reaches a handler.
        Assert.True(status is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden, $"was {status}");
    }

    [Fact]
    public async Task OversizeBody_Is413_AndNonJson_Is415()
    {
        var big = Request(HttpMethod.Post, "/api/profiles/default/controls", new string(' ', 8000));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Send(big)).Status);

        var text = Request(HttpMethod.Post, "/api/profiles/default/controls");
        text.Content = new StringContent("hello", Encoding.UTF8, "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Send(text)).Status);
    }

    [Fact]
    public async Task UnknownRoute_Is404_AndWrongMethod_Is405()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Send(Request(HttpMethod.Get, "/api/nope"))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(Request(HttpMethod.Get, "/api/profiles/missing"))).Status);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Send(Request(HttpMethod.Delete, "/api/profiles"))).Status);
    }

    // ── files ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PortAndTokenFiles_AreWritten_TokenIsProtected_AndRemovedOnDispose()
    {
        string portFile = Path.Combine(_dir, "api-port");
        string tokenFile = Path.Combine(_dir, "api-token");

        Assert.Equal(_server.Port.ToString(), File.ReadAllText(portFile));
        string stored = File.ReadAllText(tokenFile);
        Assert.StartsWith("enc:v1:", stored);
        Assert.DoesNotContain(_server.Token, stored);
        Assert.Equal(_server.Token, new FakeProtection().Unprotect(stored));
        Assert.True(_server.Token.Length >= 43); // 32 random bytes, base64url

        _server.Dispose();

        Assert.False(File.Exists(portFile));
        Assert.False(File.Exists(tokenFile));
        Assert.False(_server.IsRunning);
    }

    // ── reading ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task State_ListsProfilesAndTheActiveOne()
    {
        var (status, body) = await Send(Request(HttpMethod.Get, "/api/state"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("default", body.GetProperty("activeProfileId").GetString());
        Assert.Equal(1, body.GetProperty("profiles").GetArrayLength());
    }

    [Fact]
    public async Task ProfileDetails_NeverContainCredentials()
    {
        var profile = _profiles.GetActiveProfile();
        profile.ObsPassword = "super-secret";
        profile.TwitchOAuthToken = "oauth:secret";
        _profiles.SaveProfile(profile);

        using var response = await _http.SendAsync(Request(HttpMethod.Get, "/api/profiles/default"));
        string text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("super-secret", text);
        Assert.DoesNotContain("oauth:secret", text);
    }

    [Fact]
    public async Task ModulesAndControls_WorkWithoutHardware()
    {
        var (s1, modules) = await Send(Request(HttpMethod.Get, "/api/modules"));
        var (s2, controls) = await Send(Request(HttpMethod.Get, "/api/controls"));

        Assert.Equal(HttpStatusCode.OK, s1);
        Assert.False(modules.GetProperty("keys").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, s2);
    }

    [Fact]
    public async Task Activate_GoesThroughTheHost()
    {
        var created = _profiles.CreateProfile("Second", setAsActive: false);

        var (status, _) = await Send(Request(HttpMethod.Post, $"/api/profiles/{created.Id}/activate", "{}"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new[] { created.Id }, _host.Activated);
    }

    // ── writing: all-or-nothing ─────────────────────────────────────────────────────────────

    private string ControlsJson(params string[] controls) => "{\"controls\":[" + string.Join(",", controls) + "]}";

    [Fact]
    public async Task ValidWrite_ChangesTheKey_TakesASnapshot_AndNotifiesTheHost()
    {
        var (status, body) = await Send(Request(HttpMethod.Post, "/api/profiles/default/controls", ControlsJson(
            """{"control":"keys.3","action":"MediaPlayPause","title":"Play","backgroundColorHex":"#112233"}""",
            """{"control":"keys.4","action":"Hotkey","parameter":"Ctrl+Shift+M","title":"A very long label"}""")));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, body.GetProperty("changed").GetInt32());
        Assert.Equal(1, body.GetProperty("warnings").GetArrayLength()); // long title: warning, not failure

        var profile = _profiles.GetAllProfiles().Single();
        var key = profile.Keys.Single(k => k.Index == 3);
        Assert.Equal(DeckActionType.MediaPlayPause, key.Action);
        Assert.Equal("Play", key.CustomTitle);
        Assert.Equal("#112233", key.BackgroundColorHex);
        Assert.Equal(DeckActionType.MediaPlayPause, profile.Pages[0].Keys.Single(k => k.Index == 3).Action);

        Assert.Contains(_profiles.Backups.List(), b => b.Reason == "before-replace");
        Assert.True(_host.Flushes >= 1);
        Assert.Equal(new[] { "default" }, _host.Changed);
    }

    [Fact]
    public async Task OneInvalidChange_RejectsEverything_AndReportsAllErrors()
    {
        var before = JsonSerializer.Serialize(_profiles.GetAllProfiles().Single().Keys);

        var (status, body) = await Send(Request(HttpMethod.Post, "/api/profiles/default/controls", ControlsJson(
            """{"control":"keys.0","action":"MediaNext"}""",                       // fine
            """{"control":"keys.99","action":"MediaNext"}""",                      // bad id
            """{"control":"keys.1","action":"NoSuchAction"}""",                    // bad action
            """{"control":"keys.2","action":"Hotkey","parameter":"Ctrl+"}""",      // bad hotkey
            """{"control":"keys.5","action":"MediaNext","backgroundColorHex":"red"}""", // bad colour
            """{"control":"sliders.0","action":"MediaNext"}""",                    // not supported yet
            """{"control":"keys.0","action":"MediaStop"}""")));                    // duplicate

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.True(body.GetProperty("errors").GetArrayLength() >= 6);
        Assert.Equal(before, JsonSerializer.Serialize(_profiles.GetAllProfiles().Single().Keys)); // nothing changed
        Assert.DoesNotContain(_profiles.Backups.List(), b => b.Reason == "before-replace");
        Assert.Empty(_host.Changed);
    }

    [Fact]
    public async Task Clear_EmptiesAKey_AndIsNotCombinableWithAnAction()
    {
        await Send(Request(HttpMethod.Post, "/api/profiles/default/controls", ControlsJson("""{"control":"keys.7","action":"MediaNext","title":"Next"}""")));
        var (cleared, _) = await Send(Request(HttpMethod.Post, "/api/profiles/default/controls", ControlsJson("""{"control":"keys.7","clear":true}""")));
        Assert.Equal(HttpStatusCode.OK, cleared);
        Assert.Equal(DeckActionType.None, _profiles.GetAllProfiles().Single().Keys.Single(k => k.Index == 7).Action);

        var (both, _) = await Send(Request(HttpMethod.Post, "/api/profiles/default/controls", ControlsJson("""{"control":"keys.7","clear":true,"action":"MediaNext"}""")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, both);
    }

    [Fact]
    public async Task MalformedJson_Is400()
    {
        var (status, _) = await Send(Request(HttpMethod.Post, "/api/profiles/default/controls", "{ not json"));
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    // ── validator on its own ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Ctrl+Shift+M", true)]
    [InlineData("Win+D", true)]
    [InlineData("F5", true)]
    [InlineData("Ctrl+", false)]
    [InlineData("Ctrl+Alt", false)]
    [InlineData("", false)]
    [InlineData("Foo+M", false)]
    public void Hotkeys_AreChecked(string text, bool expected) => Assert.Equal(expected, ProfileValidator.IsParseableHotkey(text));

    [Theory]
    [InlineData("keys.0", 0)]
    [InlineData("keys.14", 14)]
    [InlineData("keys.15", null)]
    [InlineData("keys.-1", null)]
    [InlineData("key.1", null)]
    public void KeyIds_AreParsed(string id, int? expected) => Assert.Equal(expected, ProfileValidator.ParseKeyIndex(id));

    [Fact]
    public async Task Server_StopsCleanly_AndAcceptsRestartOnAFreePort()
    {
        _server.Stop();
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => _http.GetAsync(Url("/api/state")));
        _server.Start();
        Assert.True(_server.IsRunning);
        Assert.Equal(HttpStatusCode.OK, (await Send(Request(HttpMethod.Get, "/api/state"))).Status);
    }
}
