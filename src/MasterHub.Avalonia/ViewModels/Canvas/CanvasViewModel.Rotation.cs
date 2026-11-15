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

    [RelayCommand]
    public void CycleRotation()
    {
        RotationMode = RotationMode switch
        {
            DeckRotationMode.Auto => DeckRotationMode.Manual90,
            DeckRotationMode.Manual90 => DeckRotationMode.Manual180,
            DeckRotationMode.Manual180 => DeckRotationMode.Manual270,
            DeckRotationMode.Manual270 => DeckRotationMode.Manual0,
            _ => DeckRotationMode.Auto
        };

        ManualRotationAngle = RotationMode switch
        {
            DeckRotationMode.Manual90 => 90,
            DeckRotationMode.Manual180 => 180,
            DeckRotationMode.Manual270 => 270,
            _ => 0
        };

        _deviceService.RotationMode = RotationMode;
        _deviceService.ManualRotationAngle = ManualRotationAngle;
        UpdateRotationDisplayState(_deviceService.EffectiveRotationAngle);
        ScheduleAutoSave();
    }

    private void OnBaseOrientationChanged(DeckOrientation orientation)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (RotationMode == DeckRotationMode.Auto)
            {
                UpdateRotationDisplayState(_deviceService.EffectiveRotationAngle);
            }
        });
    }

    private void OnEffectiveRotationChanged(DeckRotationAngle angle)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateRotationDisplayState(angle);
        });
    }

    private void UpdateRotationDisplayState(DeckRotationAngle angle)
    {
        IsPortraitLayout = (angle == DeckRotationAngle.Deg90 || angle == DeckRotationAngle.Deg270);
        KeypadColumns = IsPortraitLayout ? 3 : 5;
        KeypadRows = IsPortraitLayout ? 5 : 3;

        var loc = LocalizationService.Instance;
        string angleText = angle switch
        {
            DeckRotationAngle.Deg90 => loc.GetString("Orientation_90", "90° (Pionowa)"),
            DeckRotationAngle.Deg180 => loc.GetString("Orientation_180", "180° (Odwrócona)"),
            DeckRotationAngle.Deg270 => loc.GetString("Orientation_270", "270° (Pionowa)"),
            _ => loc.GetString("Orientation_0", "0° (Pozioma)")
        };

        OrientationStatusText = (RotationMode == DeckRotationMode.Auto)
            ? $"{angleText} {loc.GetString("Orientation_Auto", "[Auto]")}"
            : angleText;

        RefreshAllKeyVisuals();
    }

    /// <summary>
    /// Podczas standby (blokada Windows / wygaszone ekrany) nikt nie ogląda stanu odtwarzacza ani KVM, więc zatrzymujemy
    /// ich cykliczne odpytywanie (Spotify Web API co 2 s, VLC HTTP, JetKVM); po wybudzeniu wznawiamy tylko te aktywne.
    /// </summary>
    private void SetBackgroundPollingPaused(bool paused)
    {
        try
        {
            if (paused)
            {
                _spotifyService.StopMonitoring();
                _vlcService.StopMonitoring();
                _jetKvmService.StopMonitoring();
                return;
            }

            if (_profileService.GetSpotifySettings().AutoConnect || _spotifyService.IsConnected) _spotifyService.StartMonitoring();
            if (_profileService.GetVlcSettings().AutoConnect || _vlcService.IsConnected) _vlcService.StartMonitoring();
            if (_profileService.GetJetKvmSettings().AutoConnect || _jetKvmService.IsConnected) _jetKvmService.StartMonitoring();
        }
        catch { }
    }

    private void OnStandbyStateChanged(bool isStandby)
    {
        // Ekrany decka wygaszone (blokada Windows): nikt nie ogląda metryk - monitor stoi do wybudzenia.
        if (isStandby) _systemMonitorService.IsEnabled = false;
        else UpdateMonitoringDemand();
        _animatedKeyRenderer.SetPaused(isStandby);
        if (_liveValues != null) _liveValues.Paused = isStandby;
        SetBackgroundPollingPaused(isStandby);

        if (!isStandby)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(350);
                var active = _profileService.GetActiveProfile();
                if (active != null)
                {
                    ApplyProfile(active, saveCurrent: false);
                }
                else
                {
                    PushAllHardwareState();
                }
            });
        }
    }

    public void PushAllHardwareState()
    {
        try
        {
            _deviceService.SetBrightness(KeypadBrightness);

            foreach (var key in Keys)
            {
                RenderKeyVisual(key, sendToHardware: true);
            }

            foreach (var slider in Sliders)
            {
                if (global::Avalonia.Media.Color.TryParse(slider.ColorHex, out var col))
                {
                    var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, slider.Brightness);
                    _deviceService.SetSliderLedColor(slider.Index, r, g, b);
                }
            }

            foreach (var roller in Rollers)
            {
                if (global::Avalonia.Media.Color.TryParse(roller.ColorHex, out var col))
                {
                    var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, roller.Brightness);
                    _deviceService.SetRollerLedColor(roller.Index, r, g, b);
                }
            }

            ApplyBaseEdge(); // colour, effect and speed of the profile's edge light (a status light, if any, wins)

            foreach (var knob in Knobs)
            {
                if (global::Avalonia.Media.Color.TryParse(knob.ColorHex, out var col))
                {
                    var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, knob.Brightness);
                    _deviceService.SetKnobLedColor(knob.Index, r, g, b);
                }
                _deviceService.SetKnobLedMode(knob.Index, knob.LedMode);
                _deviceService.SetKnobLedBrightness(knob.Index, knob.Brightness);
            }

            if (Dial != null)
            {
                _deviceService.SetDialBrightness(Dial.Brightness);
                if (!string.IsNullOrEmpty(Dial.ImagePath) && System.IO.File.Exists(Dial.ImagePath))
                {
                    byte[] dialJpeg = ButtonImageRenderer.RenderDialImage(Dial.ImagePath, "Cover");
                    _ = _deviceService.SetDialImageAsync(dialJpeg);
                }
                else if (global::Avalonia.Media.Color.TryParse(Dial.ColorHex, out var dialCol))
                {
                    _deviceService.SetDialColor(dialCol.R, dialCol.G, dialCol.B);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Błąd wysyłania stanu do modułów: {ex.Message}");
        }
    }
}
