using System;
using System.Linq;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>Sound keys (T-14) and the sound a timer plays when it runs out. The playing itself is in <see cref="ISoundboardService"/>.</summary>
public partial class CanvasViewModel
{
    private readonly ISoundboardService _soundboard;
    private bool _ownsSoundboard;

    private void InitializeSoundboard() => _soundboard.PlaybackChanged += OnSoundPlaybackChanged;

    private bool TryExecuteSoundAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            case DeckActionType.SoundPlay:
                PlaySoundKey(key);
                return true;
            case DeckActionType.SoundStopAll:
                _soundboard.StopAll();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Plays the key's sound. A Hold / Double binding has no sound set-up of its own (it runs on a throw-away key), so there
    /// the action parameter is taken as the file name, with the default volume, device and mode.
    /// </summary>
    private void PlaySoundKey(KeyItemViewModel key)
    {
        var settings = key.SoundSettings.IsConfigured ? key.SoundSettings : new SoundSettings { FilePath = key.ActionParameter ?? "" };
        var result = _soundboard.Play(settings);

        var target = key.Index >= 0 && key.Index < Keys.Count ? Keys[key.Index] : key;   // the red corner belongs to the real key
        ReportActionResult(target, result.IsFailure
            ? ActionResult.Failed(SoundMessages.For(result.Problem, settings.FilePath, settings.DeviceName) + (string.IsNullOrEmpty(result.Detail) || result.Problem == SoundProblem.FileMissing ? "" : " " + result.Detail))
            : ActionResult.Ok);
    }

    /// <summary>A timer that ran out plays its end sound next to whatever else is playing.</summary>
    private void PlayTimerEndSound(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var result = _soundboard.Play(new SoundSettings { FilePath = path, Mode = SoundPlayMode.Overlap });
        if (result.IsFailure) System.Diagnostics.Debug.WriteLine($"Timer end sound not played: {result.Problem} {result.Detail}");
    }

    /// <summary>A sound key shows its active look while its sound plays (its own colours / picture for the "on" state).</summary>
    private void OnSoundPlaybackChanged()
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            RefreshSoundKeys();
        });
    }

    internal void RefreshSoundKeys()
    {
        foreach (var key in Keys.Where(k => k.IsSoundAction))
        {
            bool playing = key.SoundSettings.IsConfigured && _soundboard.IsPlaying(key.SoundSettings);
            if (key.IsActiveToggleState == playing) continue;

            key.IsActiveToggleState = playing;
            RenderKeyVisual(key, sendToHardware: true);
        }
    }

    private void DisposeSoundboard()
    {
        _soundboard.PlaybackChanged -= OnSoundPlaybackChanged;
        if (_ownsSoundboard) _soundboard.Dispose();
    }
}
