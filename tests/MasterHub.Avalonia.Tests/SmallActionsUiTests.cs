using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.YouTube;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Notices;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;

namespace MasterHub.UiTests;

/// <summary>T-17 (c–j) on the canvas: the small actions really reach the services.</summary>
public sealed class SmallActionsUiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_small_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private sealed class FakeAudio : IAudioService
    {
        public List<AudioSessionItem> Sessions { get; } = new();
        public Dictionary<string, float> Set { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Muted { get; } = new();
        public bool MicMuted { get; set; } = true;
        public int MicToggles { get; private set; }
        public float Master { get; private set; } = -1;

        public float GetMasterPlaybackVolume() => Master;
        public void SetMasterPlaybackVolume(float volume) => Master = volume;
        public bool ToggleMasterPlaybackMute() => false;
        public bool IsMasterPlaybackMuted() => false;
        public float GetMasterRecordingVolume() => 1;
        public void SetMasterRecordingVolume(float volume) { }
        public bool ToggleMasterRecordingMute() { MicToggles++; MicMuted = !MicMuted; return MicMuted; }
        public bool IsMasterRecordingMuted() => MicMuted;
        public List<AudioSessionItem> GetActiveAudioSessions() => Sessions;
        public void SetProcessVolume(string processName, float volume) => Set[processName] = volume;
        public bool ToggleProcessMute(string processName) { Muted.Add(processName); return true; }
        public bool IsProcessMuted(string processName) => false;
        public void Dispose() { }
    }

    private sealed class FakeProbe : IElevationProbe
    {
        public bool IsSelfElevated => false;
        public string Front { get; set; } = "vlc";
        public ForegroundTarget? GetForeground() => new(1, Front, false);
    }

    private (CanvasViewModel Canvas, RecordingProxy<ISystemControlService> System, FakeAudio Audio, FakeProbe Probe) Create()
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var (system, log) = RecordingProxy<ISystemControlService>.Create();
        var audio = new FakeAudio();
        var probe = new FakeProbe();
        var canvas = new CanvasViewModel(F<IMasterHubDeviceService>(), F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), system,
            audio, F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles)
        {
            ElevationProbe = probe,
            Notices = new NoticeCenter()
        };
        canvas.ApplyProfile(profiles.GetActiveProfile());
        canvas.SuppressAudioOnStartup = false;
        log.Clear();
        return (canvas, log, audio, probe);
    }

    private static void Press(CanvasViewModel canvas, DeckActionType action, string parameter = "")
    {
        var key = canvas.Keys[0];
        key.AssignedAction = action;
        key.ActionParameter = parameter;
        canvas.ExecuteKeyAction(key);
    }

    [AvaloniaFact]
    public void Seek_SendsTheKeysOfThePlayerInFront()
    {
        var (canvas, system, _, probe) = Create();

        Press(canvas, DeckActionType.MediaSeekForward);
        probe.Front = "spotify";
        Press(canvas, DeckActionType.MediaSeekBackward);
        Press(canvas, DeckActionType.MediaSeekForward, "vlc-short");   // a player named in the parameter wins

        var sent = system.Calls.Where(c => c.Name == "SendHotkey").Select(c => (string)c.Args[0]!).ToList();
        Assert.Equal(new[] { "Alt+Right", "Shift+Left", "Shift+Right" }, sent);
    }

    [AvaloniaFact]
    public void TypeText_TypesWithoutTheClipboard()
    {
        var (canvas, system, _, _) = Create();
        Press(canvas, DeckActionType.TypeText, "Zażółć gęślą jaźń");

        Assert.Equal(1, system.Count("TypeText"));
        Assert.Equal(0, system.Count("PasteText"));
    }

    [AvaloniaFact]
    public void TheProgramInFront_GetsLouder_AndCanBeMuted()
    {
        var (canvas, _, audio, probe) = Create();
        probe.Front = "Spotify";
        audio.Sessions.Add(new AudioSessionItem { ProcessName = "Spotify.exe", Volume = 0.5f });

        Press(canvas, DeckActionType.ForegroundAppVolumeUp, "10");
        Press(canvas, DeckActionType.ForegroundAppMute);

        Assert.Equal(0.6f, audio.Set["Spotify.exe"], 3);
        Assert.Equal(new[] { "Spotify" }, audio.Muted);
    }

    [AvaloniaFact]
    public async Task AMultiAction_RunsItsSteps_InOrder_WithAwaitedPauses()
    {
        var (canvas, system, _, _) = Create();
        var pauses = new List<int>();
        canvas.StepDelay = ms => { pauses.Add(ms); return Task.CompletedTask; };
        var key = canvas.Keys[0];
        key.AssignedAction = DeckActionType.MultiAction;
        key.ActionParameter = "Hotkey|Ctrl+C;delay:300;TypeText|hi;delay:99999";

        await canvas.RunMultiActionAsync(key);

        Assert.Equal("Ctrl+C", system.Calls.First(c => c.Name == "SendHotkey").Args[0]);
        Assert.Equal(1, system.Count("TypeText"));
        Assert.Equal(new[] { 300, MultiActionPlan.MaxStepDelayMs }, pauses);
    }

    [AvaloniaFact]
    public async Task ASecondPress_WhileASequenceRuns_IsIgnored()
    {
        var (canvas, system, _, _) = Create();
        var gate = new TaskCompletionSource();
        canvas.StepDelay = _ => gate.Task;
        var key = canvas.Keys[0];
        key.AssignedAction = DeckActionType.MultiAction;
        key.ActionParameter = "MediaNext;delay:100;MediaPrevious";

        var first = canvas.RunMultiActionAsync(key);
        await canvas.RunMultiActionAsync(key);   // returns at once
        gate.SetResult();
        await first;

        Assert.Equal(1, system.Count("NextTrack"));
        Assert.Equal(1, system.Count("PreviousTrack"));
    }

    [AvaloniaFact]
    public void PushToTalk_OpensTheMic_OnlyWhileHeld()
    {
        var (canvas, _, audio, _) = Create();
        var key = canvas.Keys[2];
        key.AssignedAction = DeckActionType.MicMute;
        key.MutePressMode = MutePressMode.PushToTalk;

        Assert.True(canvas.HandleMutePress(key, pressed: true));
        Assert.False(audio.MicMuted);
        Assert.True(canvas.HandleMutePress(key, pressed: false));
        Assert.True(audio.MicMuted);
        Assert.Equal(2, audio.MicToggles);

        key.MutePressMode = MutePressMode.Toggle;
        Assert.False(canvas.HandleMutePress(key, pressed: true));   // the normal action handles a toggle key
    }

    [AvaloniaFact]
    public void AModesKnob_TurnsWithTheCurrentMode_AndAPressMovesOn()
    {
        var (canvas, system, _, _) = Create();
        var knob = canvas.Knobs[0];
        knob.RotateAction = RollerActionType.Modes;
        knob.RotateModes = "MediaTrack, ScrollVertical";

        canvas.OnKnobRotated(0, +1);
        canvas.OnKnobPressed(0, true);
        canvas.OnKnobRotated(0, +1);

        Assert.Equal(1, system.Count("NextTrack"));
        Assert.Equal(1, system.Count("ScrollVertical"));
        Assert.Equal(1, knob.ModeIndex);
        Assert.Contains("ScrollVertical", Assert.Single(canvas.Notices.Current).Text);
    }

    [AvaloniaFact]
    public void ABrightnessKnob_NeverGoesBelowOne()
    {
        var (canvas, _, _, _) = Create();
        canvas.Knobs[0].RotateAction = RollerActionType.DeckBrightness;
        canvas.KeypadBrightness = 8;

        canvas.OnKnobRotated(0, -5);
        Assert.Equal(1, canvas.KeypadBrightness);
        canvas.OnKnobRotated(0, +2);
        Assert.Equal(11, canvas.KeypadBrightness);
    }

    [AvaloniaFact]
    public void TheEverythingElseFader_MovesOnlyProgramsNoOtherFaderOwns()
    {
        var (canvas, _, audio, _) = Create();
        audio.Sessions.Add(new AudioSessionItem { ProcessName = "Spotify" });
        audio.Sessions.Add(new AudioSessionItem { ProcessName = "chrome" });
        audio.Sessions.Add(new AudioSessionItem { ProcessName = "game" });
        canvas.Sliders[0].SetTarget(AudioTargetType.Process, "Spotify", "Spotify");
        canvas.Sliders[1].SetTarget(AudioTargetType.EverythingElse, "", "Everything else");

        canvas.OnSliderValueChanged(1, 128, 40);

        Assert.Equal(new[] { "chrome", "game" }, audio.Set.Keys.OrderBy(k => k));
        Assert.All(audio.Set.Values, v => Assert.Equal(0.4f, v, 3));
    }

    [AvaloniaFact]
    public void AFailedUrlCall_MarksTheKey()
    {
        var (canvas, _, _, _) = Create();
        canvas.HttpSender = _ => Task.FromResult(false);
        var key = canvas.Keys[0];
        key.AssignedAction = DeckActionType.HttpRequest;
        key.ActionParameter = "POST https://x.test/hook";

        canvas.ExecuteKeyAction(key);
        for (int i = 0; i < 20 && !key.LastActionFailed; i++)
        {
            Thread.Sleep(10);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        Assert.True(key.LastActionFailed);
    }

    [AvaloniaFact]
    public void TheVolumePopupSetting_SwitchesTheFaderPath()
    {
        var (canvas, system, audio, _) = Create();
        canvas.Sliders[0].SetTarget(AudioTargetType.MasterPlayback, "", "Master");

        canvas.OnSliderValueChanged(0, 100, 50);
        Assert.Equal(0.5f, audio.Master, 3);
        Assert.Equal(0, system.Count("VolumeUp"));

        canvas.ApplyVolumeOsdSetting(true);
        canvas.OnSliderValueChanged(0, 160, 70);
        Assert.Equal(1, system.Count("VolumeUp"));   // the real key, so Windows shows its popup
    }
}
