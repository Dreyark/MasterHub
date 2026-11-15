using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Devices;
using MasterHub.Core.Localization;
using MasterHub.Core.Media;
using MasterHub.Core.Notices;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>
/// The small actions of T-17 (c–j): seek through the player's own keys, type text without the clipboard, volume of the
/// program in front, "Call a URL", multi-action sequences with non-blocking pauses, and – on knobs, the dial and faders –
/// deck brightness, program-in-front volume, seek, "modes" and "everything else".
/// </summary>
public partial class CanvasViewModel
{
    private static readonly Lazy<HttpClient> s_http = new(() => new HttpClient { Timeout = HttpAction.Timeout });

    /// <summary>For tests: replaces the HTTP sender of "Call a URL".</summary>
    internal Func<HttpActionSpec, Task<bool>>? HttpSender { get; set; }

    /// <summary>For tests: replaces <c>Task.Delay</c> between multi-action steps.</summary>
    internal Func<int, Task>? StepDelay { get; set; }

    private bool _multiActionRunning;
    private VolumeOsd? _volumeOsd;

    /// <summary>T-17 g: a fader on the main volume also shows the Windows volume popup (setting "VolumeOsdForFaders").</summary>
    public void ApplyVolumeOsdSetting(bool enabled) =>
        _volumeOsd = enabled ? new VolumeOsd(v => _audioService.SetMasterPlaybackVolume(v), () => _systemControl.VolumeUp()) : null;

    internal bool IsVolumeOsdOn => _volumeOsd != null;

    private bool TryExecuteSmallAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            case DeckActionType.MediaSeekForward:
            case DeckActionType.MediaSeekBackward:
                SeekInForegroundPlayer(key.AssignedAction == DeckActionType.MediaSeekForward ? 1 : -1, key.ActionParameter);
                return true;
            case DeckActionType.TypeText:
                if (!string.IsNullOrEmpty(key.ActionParameter)) _systemControl.TypeText(key.ActionParameter);
                return true;
            case DeckActionType.ForegroundAppVolumeUp:
                StepForegroundAppVolume(+1, AppVolume.ParseStep(key.ActionParameter));
                return true;
            case DeckActionType.ForegroundAppVolumeDown:
                StepForegroundAppVolume(-1, AppVolume.ParseStep(key.ActionParameter));
                return true;
            case DeckActionType.ForegroundAppMute:
                if (ForegroundProcessName() is { } app) _audioService.ToggleProcessMute(app);
                return true;
            case DeckActionType.HttpRequest:
                CallUrl(key);
                return true;
            case DeckActionType.MultiAction:
                _ = RunMultiActionAsync(key);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The program in front, without <c>.exe</c>; <c>null</c> when it is MasterHub itself or unknown.</summary>
    private string? ForegroundProcessName()
    {
        string? name = ElevationProbe.GetForeground()?.ProcessName;
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("MasterHub", StringComparison.OrdinalIgnoreCase)) return null;
        return AppVolume.Normalize(name);
    }

    /// <summary>T-17 e: the key the player in front uses to jump; the parameter may name a player (<c>vlc-short</c>).</summary>
    internal void SeekInForegroundPlayer(int direction, string? playerId = null)
    {
        var player = PlayerSeekShortcuts.ById(playerId) ?? PlayerSeekShortcuts.ForProcess(ElevationProbe.GetForeground()?.ProcessName);
        _systemControl.SendHotkey(direction > 0 ? player.Forward : player.Back);
    }

    /// <summary>T-17 d: one step of the volume of the program in front (its audio session).</summary>
    internal void StepForegroundAppVolume(int direction, int stepPercent)
    {
        string? app = ForegroundProcessName();
        if (app == null) return;
        var session = _audioService.GetActiveAudioSessions()
            .FirstOrDefault(s => AppVolume.Normalize(s.ProcessName).Equals(app, StringComparison.OrdinalIgnoreCase));
        if (session == null) return;   // the program plays no sound: nothing to turn
        _audioService.SetProcessVolume(session.ProcessName, AppVolume.Step(session.Volume, direction, stepPercent));
    }

    /// <summary>T-17 h: a fader for every program that no other fader of the profile owns.</summary>
    internal void SetEverythingElseVolume(float volume)
    {
        var claimed = Sliders.Where(s => s.TargetType == AudioTargetType.Process && !string.IsNullOrWhiteSpace(s.TargetProcessName))
            .Select(s => s.TargetProcessName);
        foreach (string app in AppVolume.EverythingElse(_audioService.GetActiveAudioSessions(), claimed))
        {
            _audioService.SetProcessVolume(app, volume);
        }
    }

    /// <summary>T-17 i: "Call a URL" – the answer decides the key's result (red corner on failure).</summary>
    private void CallUrl(KeyItemViewModel key)
    {
        var spec = HttpAction.Parse(key.ActionParameter);
        if (spec == null)
        {
            ReportActionResult(key, ActionResult.Failed(LocalizationService.Instance.GetString("Http_BadParameter", "not a valid http(s) address")));
            return;
        }
        Track(key, HttpSender != null ? HttpSender(spec) : HttpAction.SendAsync(s_http.Value, spec));
    }

    /// <summary>
    /// T-17 j: runs the steps of a multi-action one after another; the pauses are awaited (never a blocked thread) and
    /// capped at 10 s. A second press while one runs is ignored, and a multi-action cannot start another one.
    /// </summary>
    internal async Task RunMultiActionAsync(KeyItemViewModel key)
    {
        if (_multiActionRunning) return;
        var steps = MultiActionPlan.Parse(key.ActionParameter);
        if (steps.Count == 0) return;

        _multiActionRunning = true;
        try
        {
            foreach (var step in steps)
            {
                using var temp = new KeyItemViewModel
                {
                    Index = key.Index,
                    AssignedAction = step.Action,
                    ActionParameter = step.Parameter,
                    SkipPowerConfirmation = false   // a power step inside a sequence always asks
                };
                ExecuteKeyAction(temp);
                if (step.DelayMs > 0) await (StepDelay?.Invoke(step.DelayMs) ?? Task.Delay(step.DelayMs));
            }
        }
        finally
        {
            _multiActionRunning = false;
        }
    }

    /// <summary>T-17 a / d / e on knobs and the dial (and the jobs of a "modes" control).</summary>
    private bool TryExecuteSmallRollerAction(RollerActionType action, int direction)
    {
        switch (action)
        {
            case RollerActionType.DeckBrightness:
                KeypadBrightness = (byte)Math.Clamp(KeypadBrightness + Math.Sign(direction) * Math.Max(1, Math.Abs(direction)) * 5, DeckBrightness.Min, DeckBrightness.Max);
                return true;
            case RollerActionType.ForegroundAppVolume:
                StepForegroundAppVolume(direction, 2 * Math.Max(1, Math.Abs(direction)));
                return true;
            case RollerActionType.MediaSeek:
                for (int i = 0; i < Math.Max(1, Math.Abs(direction)); i++) SeekInForegroundPlayer(direction);
                return true;
            default:
                return false;
        }
    }

    /// <summary>T-17 i: the turn job of a control whose action is "Modes".</summary>
    internal static RollerActionType EffectiveRotateAction(RollerActionType action, string modes, int modeIndex) =>
        action == RollerActionType.Modes ? RollerModes.Current(RollerModes.Parse(modes), modeIndex) : action;

    /// <summary>T-17 i: a press on a "modes" control moves to the next mode and says which one.</summary>
    internal int NextMode(string modes, int modeIndex)
    {
        var list = RollerModes.Parse(modes);
        if (list.Count == 0) return 0;
        int next = RollerModes.Next(modeIndex, list.Count);
        Notices.Post(NoticeLevel.Info,
            string.Format(LocalizationService.Instance.GetString("Modes_Now", "Mode {0}/{1}: {2}"), next + 1, list.Count, list[next]),
            "modes");
        return next;
    }

    /// <summary>
    /// T-17 f: a mute key in push-to-talk / push-to-mute mode acts on key down and up itself (its Hold / Double are not
    /// used while held). Returns <c>true</c> when the key was handled here.
    /// </summary>
    internal bool HandleMutePress(KeyItemViewModel key, bool pressed)
    {
        if (!key.IsMuteAction || key.MutePressMode == MutePressMode.Toggle) return false;
        if (MutePress.Wanted(key.MutePressMode, pressed) is not bool wanted) return false;

        switch (key.AssignedAction)
        {
            case DeckActionType.MicMute:
                // the system microphone tells its state, so it is set exactly
                if (MutePress.NeedsToggle(_audioService.IsMasterRecordingMuted(), wanted)) _audioService.ToggleMasterRecordingMute();
                break;
            case DeckActionType.DiscordMuteToggle:
            case DeckActionType.TeamsMuteToggle:
            {
                // toggle-only services: the normal action (RPC or shortcut) once on each edge
                using var temp = new KeyItemViewModel { Index = key.Index, AssignedAction = key.AssignedAction, ActionParameter = key.ActionParameter };
                ExecuteKeyAction(temp);
                break;
            }
        }
        key.IsActiveToggleState = wanted;
        RenderKeyVisual(key, sendToHardware: true);
        return true;
    }
}
