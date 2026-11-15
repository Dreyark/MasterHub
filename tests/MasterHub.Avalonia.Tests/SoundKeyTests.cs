using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Avalonia.Views.Inspector;
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
using MasterHub.Core.Localization;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Timers;
using MasterHub.Core.Wooting;

namespace MasterHub.UiTests;

/// <summary>T-14 in the Studio: the editor, pressing a sound key, failures, the gesture bindings and the timer end sound.</summary>
public sealed class SoundKeyTests : IDisposable
{
    private sealed class FakeBoard : ISoundboardService
    {
        public List<SoundSettings> Played { get; } = new();
        public int StopAllCalls { get; private set; }
        public SoundResult Result { get; set; } = new(SoundOutcome.Started);
        public bool Playing { get; set; }
        public int ActiveVoices => Playing ? 1 : 0;
        public event Action? PlaybackChanged;

        public SoundResult Play(SoundSettings settings)
        {
            Played.Add(settings);
            return Result;
        }

        public void StopAll() => StopAllCalls++;
        public bool IsPlaying(SoundSettings settings) => Playing;
        public void Raise() => PlaybackChanged?.Invoke();
        public void Dispose() { }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_sound_ui_" + Guid.NewGuid().ToString("N"));
    private TimeSpan _now;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private (CanvasViewModel Canvas, FakeBoard Board, ProfileService Profiles) Create()
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var board = new FakeBoard();
        var canvas = new CanvasViewModel(F<IMasterHubDeviceService>(), F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles,
            timers: new TimerService(() => _now), soundboard: board);
        canvas.ApplyProfile(profiles.GetActiveProfile());
        return (canvas, board, profiles);
    }

    private static KeyItemViewModel SoundKey(CanvasViewModel canvas, int index, string file = @"C:\sounds\airhorn.mp3")
    {
        var key = canvas.Keys[index];
        key.AssignedAction = DeckActionType.SoundPlay;
        key.LoadSound(new SoundSettings { FilePath = file, VolumePercent = 120, Mode = SoundPlayMode.Restart });
        return key;
    }

    // ── the editor ──────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Editor_SetsTheFile_Volume_Mode_AndNotifiesTheStudio()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.SoundPlay };
        int saves = 0;
        key.ActionChangedCallback = _ => saves++;

        var editor = key.SoundEditor;
        editor.FilePath = @"  C:\sounds\airhorn.mp3  ";
        editor.VolumePercent = 500;                                  // clamped to twice the file
        editor.SelectedMode = editor.Modes.First(m => m.Mode == SoundPlayMode.Overlap);

        Assert.Equal(@"C:\sounds\airhorn.mp3", key.SoundSettings.FilePath);
        Assert.Equal(200, key.SoundSettings.VolumePercent);
        Assert.Equal(SoundPlayMode.Overlap, key.SoundSettings.Mode);
        Assert.Equal("airhorn.mp3", editor.FileName);
        Assert.True(saves >= 3);
    }

    [AvaloniaFact]
    public void AChosenFile_NamesTheKey_OnlyWhileTheTitleIsStillTheAutomaticOne()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.SoundPlay };

        key.SoundEditor.FilePath = @"C:\sounds\airhorn.mp3";
        Assert.Equal("airhorn", key.CustomTitle);

        key.SoundEditor.FilePath = @"C:\sounds\a-very-long-sound-name.wav";    // a later file renames it again
        Assert.Equal("a-very-long…", key.CustomTitle);

        key.CustomTitle = "My label";                                // the user typed something
        key.SoundEditor.FilePath = @"C:\sounds\other.mp3";
        Assert.Equal("My label", key.CustomTitle);
    }

    [AvaloniaFact]
    public void TheEditor_ExplainsWhatIsWrong_BeforeTheKeyIsPressed()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.SoundPlay };
        var editor = key.SoundEditor;
        var loc = LocalizationService.Instance;

        Assert.Equal(loc.GetString("Sound_NoFile", ""), editor.ProblemText);

        editor.FilePath = @"C:\sounds\clip.ogg";
        Assert.Equal(loc.GetString("Sound_Unsupported", ""), editor.ProblemText);

        editor.FilePath = @"C:\definitely\not\here\clip.mp3";
        Assert.Contains("clip.mp3", editor.ProblemText);             // names the missing file
        Assert.True(editor.HasProblem);

        string real = Path.Combine(_dir, "ok.wav");
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(real, new byte[] { 1, 2, 3 });
        editor.FilePath = real;
        Assert.False(editor.HasProblem);
    }

    [AvaloniaFact]
    public void ASavedDeviceThatIsNotConnected_StaysVisible_AndIsReported()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.SoundPlay };
        string real = Path.Combine(_dir, "ok.wav");
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(real, new byte[] { 1 });
        key.SoundEditor.FilePath = real;

        key.SoundEditor.SelectedDevice = new AudioDeviceOption("{cable}", "CABLE Input");

        Assert.Equal("{cable}", key.SoundSettings.DeviceId);
        Assert.Equal("CABLE Input", key.SoundSettings.DeviceName);
        Assert.Contains(key.SoundEditor.Devices, d => d.Id == "{cable}");           // not silently turned into "default"
        Assert.Equal("{cable}", key.SoundEditor.SelectedDevice.Id);
        Assert.Contains("CABLE Input", key.SoundEditor.ProblemText);

        key.SoundEditor.SelectedDevice = key.SoundEditor.Devices[0];                 // back to the default device
        Assert.True(key.SoundSettings.UsesDefaultDevice);
        Assert.False(key.SoundEditor.HasProblem);
    }

    [AvaloniaFact]
    public void Settings_AreSavedOnlyForSoundKeys_AndRoundTrip()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.SoundPlay };
        key.SoundEditor.FilePath = @"C:\sounds\a.mp3";
        key.SoundEditor.VolumePercent = 75;

        var model = new KeyAssignmentModel { Index = 0, Sound = key.ToSoundModel() };
        var back = JsonSerializer.Deserialize<KeyAssignmentModel>(JsonSerializer.Serialize(model))!;

        var loaded = new KeyItemViewModel { Index = 1, AssignedAction = DeckActionType.SoundPlay };
        loaded.LoadSound(back.Sound);
        Assert.Equal(@"C:\sounds\a.mp3", loaded.SoundSettings.FilePath);
        Assert.Equal(75, loaded.SoundSettings.VolumePercent);

        Assert.Null(new KeyItemViewModel { Index = 2, AssignedAction = DeckActionType.MediaNext }.ToSoundModel());
    }

    [AvaloniaFact]
    public void TheSetUp_IsPersistedWithTheProfile()
    {
        var (canvas, _, profiles) = Create();
        SoundKey(canvas, 3);
        canvas.SaveCurrentStateToActiveProfile();

        var saved = profiles.GetActiveProfile().Keys.Single(k => k.Index == 3);
        Assert.Equal(@"C:\sounds\airhorn.mp3", saved.Sound!.FilePath);
        Assert.Equal(SoundPlayMode.Restart, saved.Sound.Mode);

        canvas.ApplyProfile(profiles.GetActiveProfile(), saveCurrent: false);
        Assert.Equal(120, canvas.Keys[3].SoundSettings.VolumePercent);
    }

    [AvaloniaFact]
    public void Inspector_Renders()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.SoundPlay };
        key.SoundEditor.FilePath = @"C:\sounds\airhorn.mp3";
        key.SoundEditor.VolumePercent = 150;

        Assert.True(File.Exists(Shot.Render(new KeySoundSettingsView { DataContext = key }, "inspector_key_sound", 340, 520)));
    }

    // ── pressing ────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Press_PlaysTheKeysSound_WithItsSetUp()
    {
        var (canvas, board, _) = Create();
        var key = SoundKey(canvas, 2);

        canvas.ExecuteKeyAction(key);

        var played = Assert.Single(board.Played);
        Assert.Equal(@"C:\sounds\airhorn.mp3", played.FilePath);
        Assert.Equal(120, played.VolumePercent);
        Assert.Equal(SoundPlayMode.Restart, played.Mode);
    }

    [AvaloniaFact]
    public void AFailedSound_MarksTheKeyWithItsReason_AndASuccessClearsIt()
    {
        var (canvas, board, _) = Create();
        var key = SoundKey(canvas, 2);

        board.Result = SoundResult.Fail(SoundProblem.FileMissing, key.SoundSettings.FilePath);
        canvas.ExecuteKeyAction(key);
        Dispatcher.UIThread.RunJobs();

        Assert.True(key.LastActionFailed);
        Assert.Contains("airhorn.mp3", key.LastActionError);

        board.Result = new SoundResult(SoundOutcome.Started);
        canvas.ExecuteKeyAction(key);
        Dispatcher.UIThread.RunJobs();

        Assert.False(key.LastActionFailed);
    }

    [AvaloniaFact]
    public void ADeviceThatIsMissing_IsNamedInTheReason()
    {
        var (canvas, board, _) = Create();
        var key = SoundKey(canvas, 2);
        key.SoundSettings.DeviceName = "CABLE Input";
        board.Result = SoundResult.Fail(SoundProblem.DeviceMissing, "CABLE Input");

        canvas.ExecuteKeyAction(key);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("CABLE Input", key.LastActionError);
    }

    [AvaloniaFact]
    public void StopAll_StopsEverythingOnTheBoard()
    {
        var (canvas, board, _) = Create();
        var key = canvas.Keys[4];
        key.AssignedAction = DeckActionType.SoundStopAll;

        canvas.ExecuteKeyAction(key);

        Assert.Equal(1, board.StopAllCalls);
        Assert.Empty(board.Played);
    }

    [AvaloniaFact]
    public void ASoundBoundToHold_PlaysTheFileInItsParameter_WithTheDefaults()
    {
        var (canvas, board, _) = Create();
        var key = canvas.Keys[5];
        key.AssignedAction = DeckActionType.MediaNext;
        key.HoldAction = DeckActionType.SoundPlay;
        key.HoldParameter = @"C:\sounds\hold.wav";

        canvas.RunGesture(5, KeyGesture.Hold);

        var played = Assert.Single(board.Played);
        Assert.Equal(@"C:\sounds\hold.wav", played.FilePath);
        Assert.Equal(100, played.VolumePercent);
        Assert.True(played.UsesDefaultDevice);
    }

    [AvaloniaFact]
    public void TheKey_ShowsItsActiveLook_WhileTheSoundPlays()
    {
        var (canvas, board, _) = Create();
        var key = SoundKey(canvas, 2);
        var other = canvas.Keys[3];
        other.AssignedAction = DeckActionType.MediaNext;

        board.Playing = true;
        board.Raise();
        Dispatcher.UIThread.RunJobs();
        Assert.True(key.IsActiveToggleState);
        Assert.False(other.IsActiveToggleState);                       // other keys are not touched

        board.Playing = false;
        board.Raise();
        Dispatcher.UIThread.RunJobs();
        Assert.False(key.IsActiveToggleState);
    }

    // ── the timer end sound ─────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ATimer_PlaysItsEndSound_OnceWhenTheTimeIsUp()
    {
        var (canvas, board, _) = Create();
        var key = canvas.Keys[7];
        key.AssignedAction = DeckActionType.TimerCountdown;
        key.LoadTimer(new TimerSettings { TotalSeconds = 5, EndSoundPath = @"C:\sounds\ding.wav" });

        canvas.RunGesture(7, KeyGesture.Press);
        Assert.Empty(board.Played);                                    // nothing yet

        _now += TimeSpan.FromSeconds(5.5);
        canvas.OnTimerTick();
        canvas.OnTimerTick();

        var played = Assert.Single(board.Played);
        Assert.Equal(@"C:\sounds\ding.wav", played.FilePath);
        Assert.Equal(SoundPlayMode.Overlap, played.Mode);              // it never cuts another sound
    }

    [AvaloniaFact]
    public void ATimerWithoutAnEndSound_StaysSilent()
    {
        var (canvas, board, _) = Create();
        var key = canvas.Keys[7];
        key.AssignedAction = DeckActionType.TimerCountdown;
        key.LoadTimer(new TimerSettings { TotalSeconds = 5 });

        canvas.RunGesture(7, KeyGesture.Press);
        _now += TimeSpan.FromSeconds(6);
        canvas.OnTimerTick();

        Assert.Empty(board.Played);
    }

    [AvaloniaFact]
    public void TheEndSoundField_IsEditedOnTheTimerKey()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.TimerCountdown };

        key.TimerEditor.EndSoundPath = @"  C:\sounds\ding.wav ";

        Assert.Equal(@"C:\sounds\ding.wav", key.TimerSettings.EndSoundPath);
    }
}
