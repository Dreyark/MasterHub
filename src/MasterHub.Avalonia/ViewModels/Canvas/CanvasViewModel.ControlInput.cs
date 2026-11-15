using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Integrations.YouTube;
using MasterHub.Core.Media;
using MasterHub.Core.Models;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Canvas;

public partial class CanvasViewModel
{

    internal void OnSliderValueChanged(int sliderIndex, byte raw, int percent)
    {
        if (sliderIndex < 0 || sliderIndex >= Sliders.Count) return;

        var slider = Sliders[sliderIndex];
        int prevPercent = slider.Percent;
        slider.RawValue = raw;
        slider.Percent = percent;

        // Absolutnie NIE dotykaj audio Windows podczas startu programu i inicjalizacji urządzeń
        if (_suppressAudioOnStartup) return;

        // T-08: with a draft on the deck a fader must not move the real volume
        if (slider.TargetType != AudioTargetType.None && Math.Abs(percent - prevPercent) >= 1 && BlockedByDryRun(slider.TargetType.ToString())) return;

        // Jeśli suwak nie ma przypisanego celu audio, nie rób nic
        if (slider.TargetType == AudioTargetType.None) return;

        // Ogranicz drobne drgania (jitter)
        if (Math.Abs(percent - prevPercent) < 1) return;

        try
        {
            float vol = Math.Clamp(percent / 100.0f, 0.0f, 1.0f);
            switch (slider.TargetType)
            {
                case AudioTargetType.MasterPlayback:
                    if (_volumeOsd != null) _ = _volumeOsd.Request(vol);   // T-17 g: with the Windows volume popup
                    else _audioService.SetMasterPlaybackVolume(vol);
                    break;
                case AudioTargetType.ForegroundApp:
                    if (ForegroundProcessName() is { } frontApp) _audioService.SetProcessVolume(frontApp, vol);   // T-17 d
                    break;
                case AudioTargetType.EverythingElse:
                    SetEverythingElseVolume(vol);   // T-17 h
                    break;
                case AudioTargetType.MasterRecording:
                    _audioService.SetMasterRecordingVolume(vol);
                    break;
                case AudioTargetType.Process:
                    if (!string.IsNullOrWhiteSpace(slider.TargetProcessName))
                    {
                        _audioService.SetProcessVolume(slider.TargetProcessName, vol);
                    }
                    break;
                case AudioTargetType.HomeAssistantLight:
                    if (!string.IsNullOrWhiteSpace(slider.TargetProcessName) && _homeAssistantService != null)
                    {
                        byte brightness = (byte)Math.Clamp((int)Math.Round((percent / 100.0) * 255.0), 0, 255);
                        _ = _homeAssistantService.SetLightBrightnessAsync(slider.TargetProcessName, brightness);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CanvasViewModel] Błąd regulacji głośności: {ex.Message}");
        }
    }

    private void ExecuteRollerAction(RollerActionType action, int direction)
    {
        if (action != RollerActionType.None && BlockedByDryRun(action.ToString())) return;
        if (TryExecuteSmallRollerAction(action, direction)) return;   // T-17: brightness, program volume, seek

        switch (action)
        {
            case RollerActionType.MasterVolume:
                int steps = Math.Abs(direction);
                for (int s = 0; s < steps; s++)
                {
                    if (direction > 0) _systemControl.VolumeUp();
                    else _systemControl.VolumeDown();
                }
                break;
            case RollerActionType.ScrollVertical:
                _systemControl.ScrollVertical(direction * 3);
                break;
            case RollerActionType.ScrollHorizontal:
                _systemControl.ScrollHorizontal(direction * 3);
                break;
            case RollerActionType.MediaTrack:
                if (direction > 0) _systemControl.NextTrack();
                else _systemControl.PreviousTrack();
                break;
            case RollerActionType.RazerDpiCycle:
                _razerService.CycleDpi(direction);
                break;
            case RollerActionType.RazerProfileCycle:
                _razerService.CycleProfile(direction);
                break;
            case RollerActionType.RazerPollingRateCycle:
                _razerService.CyclePollingRate(direction);
                break;
            case RollerActionType.WootingProfileCycle:
                if (direction > 0) _wootingService.CycleProfileNext();
                else _wootingService.CycleProfile(-1);
                break;
            case RollerActionType.HomeAssistantLightBrightness:
                if (_homeAssistantService != null)
                {
                    _ = _homeAssistantService.AdjustLightBrightnessAsync(null, direction * 15);
                }
                break;
            case RollerActionType.None:
            default:
                break;
        }
    }

    private void OnRollerRotated(int rollerIndex, int direction)
    {
        if (rollerIndex >= 0 && rollerIndex < Rollers.Count)
        {
            var roller = Rollers[rollerIndex];
            roller.Steps += direction;
            roller.LastDirection = direction > 0 ? "Góra ▲" : "Dół ▼";
            ExecuteRollerAction(roller.AssignedAction, direction);
        }
    }

    internal void OnKnobRotated(int knobIndex, int delta)
    {
        if (knobIndex >= 0 && knobIndex < Knobs.Count)
        {
            var knob = Knobs[knobIndex];
            knob.Steps += delta;
            knob.LastDirection = delta > 0 ? "CW ▶" : "CCW ◀";
            knob.IsActivityActive = true;
            ExecuteRollerAction(EffectiveRotateAction(knob.RotateAction, knob.RotateModes, knob.ModeIndex), delta);
        }
    }

    internal void OnKnobPressed(int knobIndex, bool isPressed)
    {
        if (knobIndex >= 0 && knobIndex < Knobs.Count)
        {
            var knob = Knobs[knobIndex];
            knob.IsPressed = isPressed;
            if (isPressed && knob.RotateAction == RollerActionType.Modes)
            {
                knob.ModeIndex = NextMode(knob.RotateModes, knob.ModeIndex);   // T-17 i: a press moves to the next mode
                return;
            }
            if (isPressed && knob.PressAction != DeckActionType.None)
            {
                _virtualActionKey.AssignedAction = knob.PressAction;
                _virtualActionKey.ActionParameter = knob.PressActionParameter;
                ExecuteKeyAction(_virtualActionKey);
            }
        }
    }

    internal void OnDialRotated(int delta)
    {
        if (Dial != null)
        {
            Dial.Steps += delta;
            Dial.LastDirection = delta > 0 ? "CW ▶" : "CCW ◀";
            Dial.IsActivityActive = true;
            ExecuteRollerAction(EffectiveRotateAction(Dial.RotateAction, Dial.RotateModes, Dial.ModeIndex), delta);
        }
    }

    internal void OnDialPressed(bool isPressed)
    {
        if (Dial != null)
        {
            Dial.IsPressed = isPressed;
            if (isPressed && Dial.RotateAction == RollerActionType.Modes)
            {
                Dial.ModeIndex = NextMode(Dial.RotateModes, Dial.ModeIndex);
                return;
            }
            if (isPressed && Dial.PressAction != DeckActionType.None)
            {
                _virtualActionKey.AssignedAction = Dial.PressAction;
                _virtualActionKey.ActionParameter = Dial.PressActionParameter;
                ExecuteKeyAction(_virtualActionKey);
            }
        }
    }
}
