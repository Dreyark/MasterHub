using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MasterHub.Cli;
using MasterHub.Core.Api;
using MasterHub.Core.Api.Client;
using MasterHub.Core.Looks;
using MasterHub.Core.Profiles;
using MasterHub.Core.Security;
using MasterHub.Core.Status;

namespace MasterHub.Core.Tests;

/// <summary>T-25: what connected apps and Claude Code sessions report.</summary>
public class StatusBoardTests
{
    private static StatusItem Item(string source, string id, AppState state, string? label = null, DateTime? at = null) =>
        new(source, id, state, label, null, null, at ?? new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Items_AreSortedMostUrgentFirst()
    {
        var board = new StatusBoard();
        board.Replace("a", new[] { Item("a", "1", AppState.Idle), Item("a", "2", AppState.Done), Item("a", "3", AppState.Waiting), Item("a", "4", AppState.Working), Item("a", "5", AppState.Error) });

        Assert.Equal(new[] { AppState.Waiting, AppState.Error, AppState.Done, AppState.Working, AppState.Idle }, board.Items.Select(i => i.State));
    }

    [Fact]
    public void Replace_ChangesOnlyThatSource()
    {
        var board = new StatusBoard();
        board.Replace("a", new[] { Item("a", "1", AppState.Working) });
        board.Replace("b", new[] { Item("b", "1", AppState.Waiting) });

        board.Replace("a", new[] { Item("a", "2", AppState.Done) });

        Assert.Equal(new[] { "b/1", "a/2" }, board.Items.Select(i => $"{i.Source}/{i.Id}"));
        board.RemoveSource("a");
        Assert.Single(board.Items);
    }

    [Fact]
    public void ItRaisesChanged_WhenSomethingChanges()
    {
        var board = new StatusBoard();
        int raised = 0;
        board.Changed += () => raised++;

        board.Set(Item("a", "1", AppState.Working));
        board.Remove("a", "1");
        board.Remove("a", "1");   // nothing there: no change

        Assert.Equal(2, raised);
    }

    [Fact]
    public void ASourceNobodyRefreshedForItsTimeToLive_Disappears()
    {
        var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        var board = new StatusBoard(() => now);
        board.Replace("a", new[] { Item("a", "1", AppState.Waiting, at: now) }, TimeSpan.FromHours(6));
        board.Replace("keep", new[] { Item("keep", "1", AppState.Done, at: now) });   // no time to live: stays

        now = now.AddHours(5);
        Assert.Equal(1, board.Count(AppState.Waiting));
        now = now.AddHours(2);

        Assert.Equal(0, board.Count(AppState.Waiting));
        Assert.Equal("keep", Assert.Single(board.Items).Source);
    }

    [Theory]
    [InlineData("waiting", true, AppState.Waiting)]
    [InlineData(" DONE ", true, AppState.Done)]
    [InlineData("offline", true, AppState.Offline)]
    [InlineData("busy", false, AppState.Idle)]
    [InlineData("2", false, AppState.Idle)]
    [InlineData("", false, AppState.Idle)]
    public void StatesAreParsedByName_NeverByNumber(string text, bool ok, AppState expected)
    {
        Assert.Equal(ok, StatusBoard.TryParseState(text, out var state));
        Assert.Equal(expected, state);
    }
}

public class AgentTrackerTests
{
    private DateTime _now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
    private readonly StatusBoard _board;
    private readonly AgentTracker _tracker;

    public AgentTrackerTests()
    {
        _board = new StatusBoard(() => _now);
        _tracker = new AgentTracker(_board, () => _now);
    }

    private bool Send(string session, string hookEvent, string project = @"D:\Work\MasterHub", string? tool = null) =>
        _tracker.Apply(new AgentEvent(session, project, hookEvent, tool));

    [Theory]
    [InlineData("SessionStart", AppState.Idle)]
    [InlineData("UserPromptSubmit", AppState.Working)]
    [InlineData("PreToolUse", AppState.Working)]
    [InlineData("PostToolUse", AppState.Working)]
    [InlineData("Notification", AppState.Waiting)]
    [InlineData("PermissionRequest", AppState.Waiting)]
    [InlineData("Stop", AppState.Done)]
    public void EachHookEvent_MapsToOneOfTheSharedStates(string hookEvent, AppState expected)
    {
        Assert.True(Send("s1", hookEvent));

        Assert.Equal(expected, Assert.Single(_board.Items).State);
    }

    [Fact]
    public void OnlyTheProjectFolderName_ReachesTheBoard_NeverThePath()
    {
        Send("s1", "SessionStart", @"C:\Users\someone\secret client\MasterHub");
        Send("s2", "SessionStart", "/home/someone/projects/other/");

        var labels = _board.Items.Select(i => i.Label).OrderBy(l => l).ToList();
        Assert.Equal(new[] { "MasterHub", "other" }, labels);
        Assert.All(_board.Items, i => Assert.DoesNotContain("someone", i.Label));
    }

    [Fact]
    public void ASessionEnd_RemovesTheSession()
    {
        Send("s1", "SessionStart");
        Send("s2", "SessionStart");

        Send("s1", "SessionEnd");

        Assert.Equal("s2", Assert.Single(_tracker.Sessions).Id);
        Assert.Single(_board.Items);
    }

    [Fact]
    public void ASessionNotHeardFromForSixHours_IsDropped()
    {
        Send("quiet", "Stop");
        _now = _now.AddHours(5);
        Send("busy", "PreToolUse");
        _now = _now.AddHours(2);   // quiet: 7 h, busy: 2 h

        Send("busy", "PostToolUse");

        Assert.Equal(new[] { "busy" }, _tracker.Sessions.Select(s => s.Id));
        Assert.Equal(new[] { "busy" }, _board.Items.Select(i => i.Id));
    }

    [Fact]
    public void ToolName_IsKeptOnlyForAPermission()
    {
        Send("s1", "PermissionRequest", tool: "Bash");
        Assert.Equal("Bash", Assert.Single(_board.Items).Text);

        Send("s1", "PostToolUse", tool: "Bash");
        Assert.Null(Assert.Single(_board.Items).Text);
    }

    [Fact]
    public void AnUnknownEventOrAMissingSession_ChangesNothing()
    {
        Assert.False(Send("s1", "SomethingNew"));
        Assert.False(Send("", "Stop"));
        Assert.False(Send("   ", "Stop"));

        Assert.Empty(_board.Items);
    }

    [Fact]
    public void AnEventWithoutAProject_KeepsTheKnownOne()
    {
        Send("s1", "SessionStart", "/work/alpha");

        Send("s1", "Stop", "");

        Assert.Equal("alpha", Assert.Single(_board.Items).Label);
    }

    [Fact]
    public void Sessions_AreCapped_TheOldestGoFirst()
    {
        for (int i = 0; i < AgentTracker.MaxSessions + 5; i++)
        {
            _now = _now.AddSeconds(1);
            Send("s" + i, "Stop");
        }

        Assert.Equal(AgentTracker.MaxSessions, _tracker.Sessions.Count);
        Assert.DoesNotContain(_tracker.Sessions, s => s.Id == "s0");
        Assert.Contains(_tracker.Sessions, s => s.Id == "s" + (AgentTracker.MaxSessions + 4));
    }
}

public class StatusRequestsTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private static StateRequest Request(string source, params (string Id, string State)[] items) =>
        new() { Source = source, Items = items.Select(i => new StateItemRequest { Id = i.Id, State = i.State, Label = i.Id }).ToList() };

    [Fact]
    public void AGoodRequest_BecomesItems_WithTheLowerCasedSource()
    {
        var (items, source, errors) = StatusRequests.Validate(Request("Build-Server", ("ci", "working"), ("deploy", "done")), Now);

        Assert.Empty(errors);
        Assert.Equal("build-server", source);
        Assert.Equal(new[] { AppState.Working, AppState.Done }, items!.Select(i => i.State));
        Assert.All(items!, i => Assert.Equal(Now, i.UpdatedAt));
    }

    [Fact]
    public void OneBadItem_RefusesTheWholeRequest()
    {
        var (items, _, errors) = StatusRequests.Validate(Request("ci", ("a", "working"), ("b", "exploding"), ("a", "done")), Now);

        Assert.Null(items);
        Assert.Contains(errors, e => e.Contains("items[1].state"));
        Assert.Contains(errors, e => e.Contains("appears twice"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("../etc")]
    [InlineData("claude")]   // Claude Code's own source cannot be forged through /api/state
    public void ABadSource_IsRefused(string source)
    {
        var (items, _, errors) = StatusRequests.Validate(Request(source, ("a", "idle")), Now);

        Assert.Null(items);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void TooManyOrTooLongItems_AreRefused()
    {
        var many = Request("ci", Enumerable.Range(0, StatusRequests.MaxItems + 1).Select(i => ("id" + i, "idle")).ToArray());
        Assert.Null(StatusRequests.Validate(many, Now).Items);

        var long1 = Request("ci", ("a", "idle"));
        long1.Items[0].Text = new string('x', StatusRequests.MaxText + 1);
        Assert.Null(StatusRequests.Validate(long1, Now).Items);
    }

    [Fact]
    public void ControlCharactersAreRemoved()
    {
        var request = Request("ci", ("a", "idle"));
        request.Items[0].Text = "line1\r\nline2\u0007";

        var (items, _, _) = StatusRequests.Validate(request, Now);

        Assert.Equal("line1line2", items!.Single().Text);
    }
}

/// <summary>The routes and the hook: the whole way from Claude Code's hook input to the board.</summary>
public sealed class StatusApiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_status_" + Guid.NewGuid().ToString("N"));
    private readonly FakeProtection _protection = new();
    private readonly StatusBoard _board = new();
    private readonly AgentTracker _agents;
    private readonly LocalApiServer _server;
    private readonly HttpClient _http = new();

    private sealed class FakeProtection : ISecretProtectionService
    {
        public bool IsProtected(string? value) => value?.StartsWith("enc:v1:") == true;
        public string? Protect(string? plainText) => string.IsNullOrEmpty(plainText) || IsProtected(plainText) ? plainText : "enc:v1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        public string? Unprotect(string? cipherText) => IsProtected(cipherText) ? Encoding.UTF8.GetString(Convert.FromBase64String(cipherText!["enc:v1:".Length..])) : cipherText;
    }

    public StatusApiTests()
    {
        _agents = new AgentTracker(_board);
        var profiles = new ProfileService(_dir, _protection);
        _server = new LocalApiServer(profiles, _protection, options: new LocalApiOptions { PreferredPort = 0, BaseDirectory = _dir })
        { Status = _board, Agents = _agents };
        _server.Start();
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Send(HttpMethod method, string path, string? json = null)
    {
        var request = new HttpRequestMessage(method, $"http://127.0.0.1:{_server.Port}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _server.Token);
        if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task APushedState_AppearsOnTheBoard_AndInTheStatusList()
    {
        var (status, _) = await Send(HttpMethod.Post, "/api/state", """{"source":"ci","items":[{"id":"build","state":"waiting","label":"Build #12"}]}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, _board.Count(AppState.Waiting));

        var (_, body) = await Send(HttpMethod.Get, "/api/status");
        Assert.Equal(1, body.GetProperty("waiting").GetInt32());
        var item = body.GetProperty("items")[0];
        Assert.Equal("ci", item.GetProperty("source").GetString());
        Assert.Equal("waiting", item.GetProperty("state").GetString());
    }

    [Fact]
    public async Task ABadPushedState_Is422_AndChangesNothing()
    {
        _board.Set(new StatusItem("ci", "old", AppState.Done, null, null, null, DateTime.UtcNow));

        var (status, body) = await Send(HttpMethod.Post, "/api/state", """{"source":"ci","items":[{"id":"x","state":"nope"}]}""");

        Assert.Equal((HttpStatusCode)422, status);
        Assert.True(body.GetProperty("errors").GetArrayLength() > 0);
        Assert.Equal("old", Assert.Single(_board.Items).Id);
    }

    [Fact]
    public async Task AHookEvent_ReachesTheBoard()
    {
        var (status, _) = await Send(HttpMethod.Post, "/api/agent", """{"session":"s1","project":"MasterHub","event":"PermissionRequest","tool":"Bash"}""");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, _board.Count(AppState.Waiting));
    }

    [Fact]
    public async Task AnUnknownHookEvent_Is422()
    {
        var (status, _) = await Send(HttpMethod.Post, "/api/agent", """{"session":"s1","event":"Whatever"}""");

        Assert.Equal((HttpStatusCode)422, status);
        Assert.Empty(_board.Items);
    }

    [Fact]
    public async Task WithoutABoard_TheRoutesAnswer503()
    {
        string dir = _dir + "_b";
        using var bare = new LocalApiServer(new ProfileService(dir, _protection), _protection,
            options: new LocalApiOptions { PreferredPort = 0, BaseDirectory = dir });
        bare.Start();
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{bare.Port}/api/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bare.Token);

        using var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        bare.Dispose();
        try { Directory.Delete(dir, true); } catch { }
    }

    // ── masterhub-cli --claude-hook ─────────────────────────────────────────────────────────

    /// <summary>A real hook input as Claude Code writes it: it carries the prompt, tool input and more than we ever want.</summary>
    private const string HookInput = """
        {"session_id":"abc-123","transcript_path":"C:\\Users\\someone\\.claude\\projects\\x\\abc.jsonl","cwd":"D:\\Clients\\Secret Corp\\MasterHub",
         "hook_event_name":"UserPromptSubmit","prompt":"my password is hunter2, please deploy to prod",
         "tool_name":"Bash","tool_input":{"command":"curl https://secret.example/?token=SECRET"},"message":"needs your permission"}
        """;

    private async Task<(int Code, string Out, string Err)> RunHook(string input)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var runner = new CliRunner(() => new LocalApiClient(ApiConnection.Read(_dir, _protection)), new StringReader(input));
        int code = await runner.RunAsync(new[] { "--claude-hook" }, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void TheHookInput_IsReducedToTheFourFields_NoPromptNoToolInputNoPath()
    {
        var request = CliRunner.ReadHookInput(new StringReader(HookInput))!;

        string json = JsonSerializer.Serialize(request, ApiJson.Options);
        Assert.Equal("abc-123", request.Session);
        Assert.Equal("MasterHub", request.Project);
        Assert.Equal("UserPromptSubmit", request.Event);
        Assert.Null(request.Tool);   // not a permission: the tool is not even looked at
        foreach (string secret in new[] { "hunter2", "prod", "SECRET", "curl", "Secret Corp", "someone", "permission", "transcript" })
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AHook_ShowsUpOnTheBoard_AndPrintsNothing()
    {
        var (code, output, error) = await RunHook(HookInput.Replace("UserPromptSubmit", "PermissionRequest"));

        Assert.Equal(0, code);
        Assert.Equal("", output);   // hook output can reach the model: it stays empty
        Assert.Equal("", error);
        var item = Assert.Single(_board.Items);
        Assert.Equal(AppState.Waiting, item.State);
        Assert.Equal("MasterHub", item.Label);
        Assert.Equal("Bash", item.Text);   // a permission shows the tool name, and only that
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"hook_event_name\":\"Stop\"}")]   // no session id
    public async Task ABrokenHookInput_IsSilentlyIgnored(string input)
    {
        var (code, output, error) = await RunHook(input);

        Assert.Equal(0, code);
        Assert.Equal("", output + error);
        Assert.Empty(_board.Items);
    }

    [Fact]
    public async Task WhenTheAppIsNotRunning_TheHookStillReturnsZeroAndSaysNothing()
    {
        var runner = new CliRunner(() => throw new MasterHubNotRunningException("MasterHub is not running"), new StringReader(HookInput));
        var output = new StringWriter();
        var error = new StringWriter();

        int code = await runner.RunAsync(new[] { "--claude-hook" }, output, error);

        Assert.Equal(0, code);
        Assert.Equal("", output.ToString() + error);
    }

    [Fact]
    public async Task AHookInputThatIsFarTooLarge_IsSkipped()
    {
        string huge = "{\"session_id\":\"s\",\"hook_event_name\":\"Stop\",\"prompt\":\"" + new string('x', CliRunner.MaxHookInputChars) + "\"}";

        var (code, _, _) = await RunHook(huge);

        Assert.Equal(0, code);
        Assert.Empty(_board.Items);
    }
}

/// <summary>The "When" rules and the label tokens understand the board (T-25).</summary>
public class StatusLooksTests
{
    private sealed class Signals : ILookSignals
    {
        public double Cpu => 0; public double Ram => 0; public double Gpu => 0; public double CpuTemp => 0; public double GpuTemp => 0;
        public bool MusicPlaying => false; public bool MicMuted => false; public bool MasterMuted => false;
        public string? ForegroundApp => null; public string? Song => null; public string? Artist => null;
        public DateTime Now => new(2026, 11, 14, 12, 0, 0);
        public int StatusWaiting { get; set; }
        public int StatusWorking { get; set; }
        public int StatusDone { get; set; }
        public int StatusError { get; set; }
        public string? StatusTop { get; set; }
        public string? StatusTopLabel { get; set; }
    }

    private static KeyLookRule Rule(LookConditionKind kind, string bg) =>
        new() { When = new KeyLookCondition { Kind = kind }, BackgroundColorHex = bg };

    [Fact]
    public void ARuleHolds_WhileSomethingWaitsForTheUser()
    {
        var rules = new[] { Rule(LookConditionKind.StatusWaiting, "#ff8c00"), Rule(LookConditionKind.StatusDone, "#00c850") };
        var signals = new Signals();

        Assert.True(KeyLookEvaluator.Evaluate(rules, signals, new KeyLookState()).IsNone);

        signals.StatusWaiting = 1;
        Assert.Equal("#ff8c00", KeyLookEvaluator.Evaluate(rules, signals, new KeyLookState()).Background);

        signals.StatusDone = 2;   // a later rule wins when both hold
        Assert.Equal("#00c850", KeyLookEvaluator.Evaluate(rules, signals, new KeyLookState()).Background);
    }

    [Fact]
    public void TheStatusTokens_AreFilled()
    {
        var signals = new Signals { StatusWaiting = 2, StatusWorking = 1, StatusTop = "waiting", StatusTopLabel = "MasterHub" };

        Assert.Equal("MasterHub: waiting (2/1)", LabelTokens.Substitute("{status_name}: {status} ({waiting}/{working})", signals));
    }

    [Fact]
    public void WithoutAnyStatus_TheTokensAreEmpty()
    {
        Assert.Equal("[] 0", LabelTokens.Substitute("[{status}] {waiting}", new Signals()));
    }

    [Fact]
    public void AStatusRuleOrToken_MakesTheLookTimerRun_ButSamplesNothing()
    {
        var rule = LookDemand.For(new[] { Rule(LookConditionKind.StatusWaiting, "#ff8c00") });
        var token = LookDemand.For(null, "{status}");

        Assert.True(rule.Any);
        Assert.True(token.Any);
        Assert.All(new[] { rule, token }, d =>
        {
            Assert.Equal(MasterHub.Core.Monitoring.MonitorMetrics.None, d.Metrics);
            Assert.False(d.NowPlaying || d.Audio || d.Foreground || d.Clock);
        });
    }
}
