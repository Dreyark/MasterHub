using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Packages;
using MasterHub.Core.Timers;

namespace MasterHub.Core.Tests;

/// <summary>T-14: the sound board – play modes, device handling, the idle close – on a fake output.</summary>
public class SoundboardTests
{
    private sealed class FakeVoice : ISoundVoice
    {
        public bool IsPlaying { get; set; } = true;
        public bool StopRaisesFinished { get; set; }
        public float Gain { get; init; }
        public string File { get; init; } = "";
        public event Action? Finished;

        public void Stop()
        {
            IsPlaying = false;
            if (StopRaisesFinished) Finished?.Invoke();   // a voice that reports its end from inside Stop()
        }

        public void End()
        {
            IsPlaying = false;
            Finished?.Invoke();
        }
    }

    private sealed class FakeDevice : ISoundDevice
    {
        public string Id { get; init; } = "";
        public List<FakeVoice> Voices { get; } = new();
        public bool Disposed { get; private set; }
        public Exception? ThrowOnStart { get; set; }
        public bool StopRaisesFinished { get; set; }

        public ISoundVoice Start(string file, float gain)
        {
            if (ThrowOnStart != null) throw ThrowOnStart;
            var voice = new FakeVoice { File = file, Gain = gain, StopRaisesFinished = StopRaisesFinished };
            Voices.Add(voice);
            return voice;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeFactory : ISoundOutputFactory
    {
        public List<FakeDevice> Opened { get; } = new();
        public Exception? ThrowOnOpen { get; set; }
        public bool StopRaisesFinished { get; set; }

        public ISoundDevice Open(string deviceId)
        {
            if (ThrowOnOpen != null) throw ThrowOnOpen;
            var d = new FakeDevice { Id = deviceId, StopRaisesFinished = StopRaisesFinished };
            Opened.Add(d);
            return d;
        }
    }

    private sealed class FakeDevices : IAudioDeviceService
    {
        public List<AudioDeviceInfo> Playback { get; } = new();
        public IReadOnlyList<AudioDeviceInfo> GetDevices(bool capture) => capture ? new List<AudioDeviceInfo>() : Playback;
        public IReadOnlyList<AudioDeviceInfo> GetAllDevices() => Playback;
        public bool SetDefault(string deviceId, out string error) { error = ""; return true; }
        public event Action? DefaultDeviceChanged { add { } remove { } }
        public void Dispose() { }
    }

    private DateTime _now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private (SoundboardService Board, FakeFactory Factory) Create(IAudioDeviceService? devices = null, bool stopRaisesFinished = false)
    {
        var factory = new FakeFactory { StopRaisesFinished = stopRaisesFinished };
        var board = new SoundboardService(factory, devices, () => _now, _ => true, autoClose: false);
        return (board, factory);
    }

    private static SoundSettings Sound(string file = @"C:\sounds\airhorn.mp3", SoundPlayMode mode = SoundPlayMode.PlayStop,
        int volume = 100, string deviceId = "", string deviceName = "") =>
        new() { FilePath = file, Mode = mode, VolumePercent = volume, DeviceId = deviceId, DeviceName = deviceName };

    // ── file check ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", SoundProblem.NoFile)]
    [InlineData("   ", SoundProblem.NoFile)]
    [InlineData(null, SoundProblem.NoFile)]
    [InlineData(@"C:\x\clip.ogg", SoundProblem.UnsupportedType)]     // OGG needs its own decoder
    [InlineData(@"C:\x\clip.txt", SoundProblem.UnsupportedType)]
    [InlineData(@"C:\x\clip", SoundProblem.UnsupportedType)]
    [InlineData(@"C:\x\clip.MP3", SoundProblem.None)]
    [InlineData(@"C:\x\clip.wav", SoundProblem.None)]
    [InlineData(@"C:\x\clip.flac", SoundProblem.None)]
    public void Check_ValidatesBeforeAnythingStarts(string? path, SoundProblem expected) =>
        Assert.Equal(expected, SoundFiles.Check(path, _ => true));

    [Fact]
    public void Check_ReportsAMissingFile()
    {
        Assert.Equal(SoundProblem.FileMissing, SoundFiles.Check(@"C:\x\gone.mp3", _ => false));
    }

    [Fact]
    public void Play_RefusesABadFile_WithoutOpeningAnyDevice()
    {
        var (board, factory) = Create();

        Assert.Equal(SoundProblem.NoFile, board.Play(Sound("")).Problem);
        Assert.Equal(SoundProblem.UnsupportedType, board.Play(Sound("a.ogg")).Problem);
        Assert.Empty(factory.Opened);                              // a bad file never touches the audio device
    }

    [Fact]
    public void Play_ReportsAMissingFile()
    {
        var factory = new FakeFactory();
        var board = new SoundboardService(factory, null, () => _now, _ => false, autoClose: false);

        var result = board.Play(Sound());

        Assert.True(result.IsFailure);
        Assert.Equal(SoundProblem.FileMissing, result.Problem);
    }

    // ── modes ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PlayStop_ASecondPressStops_ThirdStartsAgain()
    {
        var (board, factory) = Create();

        Assert.Equal(SoundOutcome.Started, board.Play(Sound()).Outcome);
        Assert.True(board.IsPlaying(Sound()));

        Assert.Equal(SoundOutcome.Stopped, board.Play(Sound()).Outcome);
        Assert.False(board.IsPlaying(Sound()));
        Assert.False(factory.Opened[0].Voices[0].IsPlaying);

        Assert.Equal(SoundOutcome.Started, board.Play(Sound()).Outcome);
        Assert.Equal(2, factory.Opened[0].Voices.Count);
    }

    [Fact]
    public void Restart_CutsTheRunningCopy_AndStartsFromTheBeginning()
    {
        var (board, factory) = Create();
        var s = Sound(mode: SoundPlayMode.Restart);

        board.Play(s);
        board.Play(s);

        var voices = factory.Opened[0].Voices;
        Assert.Equal(2, voices.Count);
        Assert.False(voices[0].IsPlaying);
        Assert.True(voices[1].IsPlaying);
        Assert.Equal(1, board.ActiveVoices);
    }

    [Fact]
    public void Overlap_PlaysOneMoreCopyOverTheFirst()
    {
        var (board, factory) = Create();
        var s = Sound(mode: SoundPlayMode.Overlap);

        board.Play(s);
        board.Play(s);
        board.Play(s);

        Assert.Equal(3, board.ActiveVoices);
        Assert.All(factory.Opened[0].Voices, v => Assert.True(v.IsPlaying));
    }

    [Fact]
    public void DifferentFiles_AreDifferentSounds_SoPlayStopOnOneNeverStopsTheOther()
    {
        var (board, factory) = Create();

        board.Play(Sound(@"C:\a.mp3"));
        board.Play(Sound(@"C:\b.mp3"));

        Assert.Equal(2, board.ActiveVoices);
        board.Play(Sound(@"C:\a.mp3"));                            // stops only a
        Assert.Equal(1, board.ActiveVoices);
        Assert.True(board.IsPlaying(Sound(@"C:\b.mp3")));
        Assert.Single(factory.Opened);                            // one output serves both
    }

    [Fact]
    public void ASoundThatEnded_IsNotPlayingAnyMore_SoThePressStartsItAgain()
    {
        var (board, factory) = Create();
        board.Play(Sound());

        factory.Opened[0].Voices[0].End();                         // played to the end

        Assert.False(board.IsPlaying(Sound()));
        Assert.Equal(SoundOutcome.Started, board.Play(Sound()).Outcome);   // not "stopped"
    }

    [Fact]
    public void Volume_IsPassedAsGain_UpToTwiceTheFile()
    {
        var (board, factory) = Create();

        board.Play(Sound(@"C:\a.mp3", SoundPlayMode.Overlap, volume: 150));
        board.Play(Sound(@"C:\b.mp3", SoundPlayMode.Overlap, volume: 900));    // clamped
        board.Play(Sound(@"C:\c.mp3", SoundPlayMode.Overlap, volume: 0));

        var gains = factory.Opened[0].Voices.Select(v => v.Gain).ToArray();
        Assert.Equal(new[] { 1.5f, 2.0f, 0f }, gains);
    }

    [Fact]
    public void StopAll_StopsEverything_AndTellsTheListeners()
    {
        var (board, factory) = Create();
        board.Play(Sound(@"C:\a.mp3", SoundPlayMode.Overlap));
        board.Play(Sound(@"C:\b.mp3", SoundPlayMode.Overlap));
        int changes = 0;
        board.PlaybackChanged += () => changes++;

        board.StopAll();

        Assert.Equal(0, board.ActiveVoices);
        Assert.All(factory.Opened[0].Voices, v => Assert.False(v.IsPlaying));
        Assert.Equal(1, changes);

        board.StopAll();                                           // nothing playing: no change to report
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AVoiceThatReportsItsEndFromInsideStop_DoesNotBreakStopAll()
    {
        var (board, _) = Create(stopRaisesFinished: true);
        board.Play(Sound(@"C:\a.mp3", SoundPlayMode.Overlap));
        board.Play(Sound(@"C:\b.mp3", SoundPlayMode.Overlap));

        board.StopAll();                                           // must not throw "collection was modified"

        Assert.Equal(0, board.ActiveVoices);
    }

    [Fact]
    public void PlaybackChanged_IsRaisedWhenASoundStartsStopsOrEnds()
    {
        var (board, factory) = Create();
        int changes = 0;
        board.PlaybackChanged += () => changes++;

        board.Play(Sound());                                       // started
        factory.Opened[0].Voices[0].End();                         // ended
        board.Play(Sound());                                       // started
        board.Play(Sound());                                       // stopped

        Assert.Equal(4, changes);
    }

    // ── devices ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NothingIsOpened_UntilTheFirstSound()
    {
        var (board, factory) = Create();

        Assert.Empty(factory.Opened);
        Assert.Equal(0, board.OpenDevices);

        board.Play(Sound());
        Assert.Equal(1, board.OpenDevices);
        Assert.Equal("", factory.Opened[0].Id);                    // the default device
    }

    [Fact]
    public void TheSameFileOnTwoDevices_IsTwoSounds_EachWithItsOwnOutput()
    {
        var devices = new FakeDevices();
        devices.Playback.Add(new AudioDeviceInfo("spk", "Speakers", false, true));
        devices.Playback.Add(new AudioDeviceInfo("cable", "CABLE Input", false, false));
        var (board, factory) = Create(devices);

        board.Play(Sound(deviceId: "spk", deviceName: "Speakers"));
        board.Play(Sound(deviceId: "cable", deviceName: "CABLE Input"));

        Assert.Equal(2, board.ActiveVoices);
        Assert.Equal(new[] { "spk", "cable" }, factory.Opened.Select(d => d.Id).ToArray());

        board.Play(Sound(deviceId: "cable", deviceName: "CABLE Input"));   // PlayStop on the cable only
        Assert.Equal(1, board.ActiveVoices);
        Assert.True(board.IsPlaying(Sound(deviceId: "spk", deviceName: "Speakers")));
    }

    [Fact]
    public void ADeviceThatIsNotConnected_IsReported_AndNothingIsOpened()
    {
        var devices = new FakeDevices();
        devices.Playback.Add(new AudioDeviceInfo("spk", "Speakers", false, true));
        var (board, factory) = Create(devices);

        var result = board.Play(Sound(deviceId: "gone", deviceName: "CABLE Input"));

        Assert.Equal(SoundProblem.DeviceMissing, result.Problem);
        Assert.Empty(factory.Opened);
    }

    [Fact]
    public void ADeviceWhoseIdChanged_IsFoundAgainByItsName()
    {
        var devices = new FakeDevices();
        devices.Playback.Add(new AudioDeviceInfo("new-id", "2 - CABLE Input", false, false));   // Windows added a number
        var (board, factory) = Create(devices);

        var result = board.Play(Sound(deviceId: "old-id", deviceName: "CABLE Input"));

        Assert.Equal(SoundOutcome.Started, result.Outcome);
        Assert.Equal("new-id", factory.Opened[0].Id);
    }

    [Fact]
    public void AnOutputThatFails_BecomesAReason_AndTheBoardKeepsWorking()
    {
        var (board, factory) = Create();
        board.Play(Sound());
        factory.Opened[0].ThrowOnStart = new SoundException(SoundProblem.PlaybackFailed, "decoder error");

        var failed = board.Play(Sound(@"C:\other.mp3"));
        Assert.Equal(SoundProblem.PlaybackFailed, failed.Problem);
        Assert.Contains("decoder", failed.Detail);

        factory.Opened[0].ThrowOnStart = new InvalidOperationException("boom");
        Assert.Equal(SoundProblem.PlaybackFailed, board.Play(Sound(@"C:\other2.mp3")).Problem);

        factory.Opened[0].ThrowOnStart = null;
        Assert.Equal(SoundOutcome.Started, board.Play(Sound(@"C:\other3.mp3")).Outcome);   // still usable
    }

    [Fact]
    public void AnOutputThatCannotBeOpened_IsAReason_NotAnException()
    {
        var (board, factory) = Create();
        factory.ThrowOnOpen = new SoundException(SoundProblem.DeviceMissing, "The output device is not active.");

        Assert.Equal(SoundProblem.DeviceMissing, board.Play(Sound()).Problem);
        Assert.Equal(0, board.OpenDevices);
    }

    // ── the idle close ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnOutput_IsClosedAfter30SecondsOfSilence_AndReopenedByTheNextSound()
    {
        var (board, factory) = Create();
        board.Play(Sound());
        factory.Opened[0].Voices[0].End();

        _now += TimeSpan.FromSeconds(29);
        Assert.Equal(0, board.CloseIdle());                        // not yet
        Assert.False(factory.Opened[0].Disposed);

        _now += TimeSpan.FromSeconds(2);
        Assert.Equal(1, board.CloseIdle());
        Assert.True(factory.Opened[0].Disposed);
        Assert.Equal(0, board.OpenDevices);                        // an idle app holds no audio device

        board.Play(Sound());
        Assert.Equal(2, factory.Opened.Count);                     // a fresh output
    }

    [Fact]
    public void AnOutput_WithASoundStillPlaying_IsNeverClosed()
    {
        var (board, factory) = Create();
        board.Play(Sound());

        _now += TimeSpan.FromMinutes(10);

        Assert.Equal(0, board.CloseIdle());
        Assert.False(factory.Opened[0].Disposed);
    }

    [Fact]
    public void TheIdleTime_CountsFromTheLastSound_NotFromTheFirst()
    {
        var (board, factory) = Create();
        board.Play(Sound(@"C:\a.mp3", SoundPlayMode.Overlap));
        factory.Opened[0].Voices[0].End();

        _now += TimeSpan.FromSeconds(25);
        board.Play(Sound(@"C:\b.mp3"));
        factory.Opened[0].Voices[1].End();

        _now += TimeSpan.FromSeconds(25);                          // 50 s after the first, 25 s after the last
        Assert.Equal(0, board.CloseIdle());
    }

    [Fact]
    public void Dispose_StopsTheSoundsAndClosesTheOutputs()
    {
        var (board, factory) = Create();
        board.Play(Sound());

        board.Dispose();

        Assert.False(factory.Opened[0].Voices[0].IsPlaying);
        Assert.True(factory.Opened[0].Disposed);
        Assert.Equal(SoundProblem.PlaybackFailed, board.Play(Sound()).Problem);   // a stopped board does not start again
    }

    // ── packages & timers ───────────────────────────────────────────────────────────────────

    [Fact]
    public void SoundPaths_AreNotPackedWithAProfile()
    {
        var profile = ProfileModel.CreateEmpty("P");
        var sound = new KeyAssignmentModel { Index = 0, Action = DeckActionType.SoundPlay, Sound = new SoundSettings { FilePath = @"C:\Users\me\x.mp3", VolumePercent = 80 } };
        var timer = new KeyAssignmentModel { Index = 1, Action = DeckActionType.TimerCountdown, Timer = new TimerSettings { EndSoundPath = @"C:\Users\me\ding.wav", TotalSeconds = 90 } };
        profile.Keys = new List<KeyAssignmentModel> { sound, timer };
        profile.Pages[0].Keys = new List<KeyAssignmentModel> { sound, timer };

        ProfilePaths.ClearSounds(profile);

        Assert.Equal("", sound.Sound!.FilePath);
        Assert.Equal(80, sound.Sound.VolumePercent);               // the rest of the set-up travels
        Assert.Equal("", timer.Timer!.EndSoundPath);
        Assert.Equal(90, timer.Timer.TotalSeconds);
    }

    [Fact]
    public void ATimerExpiry_CarriesItsEndSound()
    {
        var timer = new DeckTimer(TimerKind.Countdown, new TimerSettings { TotalSeconds = 5, EndSoundPath = @"C:\ding.wav" });
        timer.Apply(TimerCommand.StartPause, TimeSpan.Zero);

        var expiry = timer.Poll(TimeSpan.FromSeconds(5));

        Assert.Equal(@"C:\ding.wav", expiry!.Value.EndSoundPath);
    }

    [Fact]
    public void Settings_AreNormalised()
    {
        var s = new SoundSettings { VolumePercent = 999, FilePath = null!, DeviceId = null! }.Normalize();

        Assert.Equal(200, s.VolumePercent);
        Assert.Equal("", s.FilePath);
        Assert.True(s.UsesDefaultDevice);
        Assert.False(s.IsConfigured);
    }
}
