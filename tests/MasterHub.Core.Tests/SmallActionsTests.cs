using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Actions;
using MasterHub.Core.Api;
using MasterHub.Core.Audio;
using MasterHub.Core.Media;
using MasterHub.Core.SystemControl;
using Xunit;

namespace MasterHub.Core.Tests;

/// <summary>T-17 (c–j): the small actions' logic.</summary>
public class SmallActionsTests
{
    // ── j: multi-action ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void AMultiAction_IsReadFromText_WithPausesAfterTheSteps()
    {
        var steps = MultiActionPlan.Parse("Hotkey|Ctrl+C; delay:200; OpenApplication|notepad.exe ;wait:50ms; calc.exe");

        Assert.Equal(3, steps.Count);
        Assert.Equal((DeckActionType.Hotkey, "Ctrl+C", 200), (steps[0].Action, steps[0].Parameter, steps[0].DelayMs));
        Assert.Equal((DeckActionType.OpenApplication, "notepad.exe", 50), (steps[1].Action, steps[1].Parameter, steps[1].DelayMs));
        Assert.Equal((DeckActionType.OpenApplication, "calc.exe", 0), (steps[2].Action, steps[2].Parameter, steps[2].DelayMs));
    }

    [Fact]
    public void Pauses_AreCappedAt10Seconds_AndLoopsAreNotPossible()
    {
        var steps = MultiActionPlan.Parse("MediaNext;delay:999999;MultiAction|x;Delay|500;MediaPrevious");

        Assert.Equal(new[] { DeckActionType.MediaNext, DeckActionType.MediaPrevious }, steps.Select(s => s.Action));
        Assert.Equal(MultiActionPlan.MaxStepDelayMs, steps[0].DelayMs);
    }

    [Fact]
    public void TheStepList_WinsOverTheText_AndIsCappedToo()
    {
        var steps = MultiActionPlan.Parse("MediaNext", new[]
        {
            new MultiActionStepModel { Action = DeckActionType.MediaPlayPause, DelayMs = 60_000 },
            new MultiActionStepModel { Action = DeckActionType.MultiAction }
        });

        var only = Assert.Single(steps);
        Assert.Equal(DeckActionType.MediaPlayPause, only.Action);
        Assert.Equal(10_000, only.DelayMs);
        Assert.Empty(MultiActionPlan.Parse(null));
        Assert.True(MultiActionPlan.Parse(string.Join(";", Enumerable.Repeat("MediaNext", 80))).Count <= MultiActionPlan.MaxSteps);
    }

    // ── e: seek with the players' own keys ──────────────────────────────────────────────────

    [Fact]
    public void EverySeekShortcut_Parses()
    {
        Assert.Equal(12, PlayerSeekShortcuts.Players.Count);
        foreach (var p in PlayerSeekShortcuts.Players.Append(PlayerSeekShortcuts.Generic))
        {
            Assert.NotNull(HotkeyParser.Parse(p.Forward));
            Assert.NotNull(HotkeyParser.Parse(p.Back));
        }
    }

    [Theory]
    [InlineData("vlc.exe", null, "vlc")]
    [InlineData("VLC", null, "vlc")]
    [InlineData("wmplayer", null, "wmp-legacy")]
    [InlineData("chrome", "Song - YouTube - Google Chrome", "youtube")]
    [InlineData("chrome", "Docs", "generic")]
    [InlineData("notepad", null, "generic")]
    public void ThePlayer_IsFoundFromTheProgramInFront(string process, string? title, string expected)
    {
        Assert.Equal(expected, PlayerSeekShortcuts.ForProcess(process, title).Id);
    }

    // ── f: mute press modes ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MutePressMode.PushToTalk, true, false)]
    [InlineData(MutePressMode.PushToTalk, false, true)]
    [InlineData(MutePressMode.PushToMute, true, true)]
    [InlineData(MutePressMode.PushToMute, false, false)]
    public void PushModes_WantAStatePerEdge(MutePressMode mode, bool pressed, bool muted)
    {
        Assert.Equal(muted, MutePress.Wanted(mode, pressed));
    }

    [Fact]
    public void Toggle_LeavesItToTheNormalAction_AndKnownStatesAreNotToggledTwice()
    {
        Assert.Null(MutePress.Wanted(MutePressMode.Toggle, true));
        Assert.False(MutePress.NeedsToggle(true, true));
        Assert.True(MutePress.NeedsToggle(false, true));
        Assert.True(MutePress.NeedsToggle(null, true));   // a toggle-only service: toggle on each edge
    }

    // ── d / h: program volume, everything else ──────────────────────────────────────────────

    [Fact]
    public void AppVolume_StepsAndClamps()
    {
        Assert.Equal(0.55f, AppVolume.Step(0.5f, +1, 5), 3);
        Assert.Equal(1f, AppVolume.Step(0.98f, +1, 5));
        Assert.Equal(0f, AppVolume.Step(0.02f, -3, 5));
        Assert.Equal(5, AppVolume.ParseStep(""));
        Assert.Equal(10, AppVolume.ParseStep("10%"));
        Assert.Equal(50, AppVolume.ParseStep("90"));
    }

    [Fact]
    public void EverythingElse_IsEveryProgramNoOtherFaderOwns()
    {
        var sessions = new[]
        {
            new AudioSessionItem { ProcessName = "Spotify.exe" }, new AudioSessionItem { ProcessName = "chrome" },
            new AudioSessionItem { ProcessName = "Discord" }, new AudioSessionItem { ProcessName = "MasterHub.Avalonia" },
            new AudioSessionItem { ProcessName = "" }, new AudioSessionItem { ProcessName = "chrome" }
        };

        var rest = AppVolume.EverythingElse(sessions, new[] { "spotify", "Discord.exe" });

        Assert.Equal(new[] { "chrome" }, rest);
    }

    // ── g: the Windows volume popup ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ThePopup_SetsOneStepShort_PressesVolumeUp_ThenSetsTheExactLevel()
    {
        var log = new List<string>();
        var osd = new VolumeOsd(v => log.Add($"set {v:0.00}"), () => log.Add("up"), _ => Task.CompletedTask);

        await osd.Request(0.6f);

        Assert.Equal(new[] { "set 0.58", "up", "set 0.60" }, log);
    }

    [Fact]
    public async Task FastRequests_AreMerged_TheNewestWins()
    {
        var log = new List<string>();
        var gate = new TaskCompletionSource();
        VolumeOsd? osd = null;
        int delays = 0;
        osd = new VolumeOsd(v => log.Add($"set {v:0.00}"), () => log.Add("up"), async settle =>
        {
            if (delays++ == 0)
            {
                _ = osd!.Request(0.3f);   // arrive while the first one waits
                _ = osd!.Request(0.4f);
                await gate.Task;
            }
        });

        var first = osd.Request(0.2f);
        gate.SetResult();
        await first;

        Assert.Equal(new[] { "set 0.18", "up", "set 0.20", "set 0.38", "up", "set 0.40" }, log);
    }

    // ── i: modes, Call a URL ────────────────────────────────────────────────────────────────

    [Fact]
    public void Modes_Are2To4KnownTurnJobs()
    {
        Assert.Equal(new[] { RollerActionType.MasterVolume, RollerActionType.ScrollVertical, RollerActionType.MediaTrack },
            RollerModes.Parse("MasterVolume, scrollvertical; MediaTrack|Nonsense|Modes"));
        Assert.Empty(RollerModes.Parse("MasterVolume"));   // one job is not a mode switch
        Assert.Equal(4, RollerModes.Parse("MasterVolume,ScrollVertical,ScrollHorizontal,MediaTrack,DeckBrightness").Count);
        Assert.Equal(0, RollerModes.Next(2, 3));
        Assert.Equal(RollerActionType.None, RollerModes.Current(Array.Empty<RollerActionType>(), 0));
    }

    [Theory]
    [InlineData("https://example.com/hook", "GET", "https://example.com/hook", null)]
    [InlineData("POST http://127.0.0.1:8080/api | {\"on\":true}", "POST", "http://127.0.0.1:8080/api", "{\"on\":true}")]
    [InlineData("delete https://x.test/a", "DELETE", "https://x.test/a", null)]
    public void AUrlCall_IsRead(string parameter, string method, string url, string? body)
    {
        var spec = HttpAction.Parse(parameter)!;
        Assert.Equal(method, spec.Method.Method);
        Assert.Equal(url, spec.Uri.ToString());
        Assert.Equal(body, spec.Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("FOO https://x.test")]
    [InlineData("ftp://x.test/")]
    [InlineData("not a url")]
    public void OnlyHttpAddresses_AreCalled(string parameter)
    {
        Assert.Null(HttpAction.Parse(parameter));
    }

    private sealed class Answer : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        public HttpRequestMessage? Seen { get; private set; }
        public string? Body { get; private set; }
        public Answer(HttpStatusCode code) => _code = code;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_code);
        }
    }

    [Fact]
    public async Task AUrlCall_SucceedsOn2xx_AndSendsJsonBodies()
    {
        var ok = new Answer(HttpStatusCode.NoContent);
        Assert.True(await HttpAction.SendAsync(new HttpClient(ok), HttpAction.Parse("POST https://x.test/a | {\"a\":1}")!));
        Assert.Equal("application/json", ok.Seen!.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("{\"a\":1}", ok.Body);

        Assert.False(await HttpAction.SendAsync(new HttpClient(new Answer(HttpStatusCode.InternalServerError)), HttpAction.Parse("https://x.test/b")!));
    }

    [Fact]
    public void TheAssistant_NeedsMorePower_ForUrlCallsAndSequences()
    {
        var change = new[] { new ControlChange { Control = "keys.1", Action = "HttpRequest", Parameter = "https://x.test" } };
        Assert.False(ProfileValidator.Validate(change, 1, new AssistantLimits(false)).IsValid);
        Assert.True(ProfileValidator.Validate(change, 1, new AssistantLimits(true)).IsValid);
        Assert.False(ProfileValidator.Validate(new[] { new ControlChange { Control = "keys.1", Action = "HttpRequest", Parameter = "ftp://x" } }, 1).IsValid);
    }
}
