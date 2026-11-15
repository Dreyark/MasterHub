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

    private void OnKnobActionChanged(KnobItemViewModel knob) => ScheduleAutoSave();

    private void OnKnobColorChanged(KnobItemViewModel knob, string hex)
    {
        if (global::Avalonia.Media.Color.TryParse(hex, out var col))
        {
            var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, knob.Brightness);
            _deviceService.SetKnobLedColor(knob.Index, r, g, b);
        }
        ScheduleAutoSave();
    }

    private void OnKnobBrightnessChanged(KnobItemViewModel knob, byte brightness)
    {
        OnKnobColorChanged(knob, knob.ColorHex);
        _deviceService.SetKnobLedBrightness(knob.Index, brightness);
        ScheduleAutoSave();
    }

    private void OnKnobLedModeChanged(KnobItemViewModel knob, KnobLedMode mode)
    {
        _deviceService.SetKnobLedMode(knob.Index, mode);
        ScheduleAutoSave();
    }

    private void OnDialActionChanged(DialItemViewModel dial) => ScheduleAutoSave();

    private void OnDialColorChanged(DialItemViewModel dial, string hex)
    {
        if (global::Avalonia.Media.Color.TryParse(hex, out var col))
        {
            _deviceService.SetDialColor(col.R, col.G, col.B);
        }
        ScheduleAutoSave();
    }

    private void OnDialBrightnessChanged(DialItemViewModel dial, byte brightness)
    {
        _deviceService.SetDialBrightness(brightness);
        ScheduleAutoSave();
    }

    private void OnDialImageChanged(DialItemViewModel dial, string? imagePath)
    {
        if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
        {
            byte[] dialJpeg = ButtonImageRenderer.RenderDialImage(imagePath, "Cover");
            _ = _deviceService.SetDialImageAsync(dialJpeg);
        }
        else
        {
            OnDialColorChanged(dial, dial.ColorHex);
        }
        ScheduleAutoSave();
    }

    private void OnBaseColorChanged(BaseStationItemViewModel baseStation, string hex)
    {
        ApplyBaseEdge();
        ScheduleAutoSave();
    }

    private void OnBaseBrightnessChanged(BaseStationItemViewModel baseStation, byte brightness)
    {
        OnBaseColorChanged(baseStation, baseStation.ColorHex);
        if (IsSyncBrightness)
        {
            SyncAllBrightness(brightness, "Base");
        }
        ScheduleAutoSave();
    }

    private void OnSliderColorChanged(SliderItemViewModel slider, string hex)
    {
        if (global::Avalonia.Media.Color.TryParse(hex, out var col))
        {
            var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, slider.Brightness);
            _deviceService.SetSliderLedColor(slider.Index, r, g, b);
        }
        ScheduleAutoSave();
    }

    private void OnSliderBrightnessChanged(SliderItemViewModel slider, byte brightness)
    {
        OnSliderColorChanged(slider, slider.ColorHex);
        if (IsSyncBrightness)
        {
            SyncAllBrightness(brightness, "Slider");
        }
        ScheduleAutoSave();
    }

    private void OnSliderTargetChanged(SliderItemViewModel slider, string target)
    {
        ScheduleAutoSave();
    }

    private void OnRollerActionChanged(RollerItemViewModel roller)
    {
        ScheduleAutoSave();
    }

    private void OnRollerColorChanged(RollerItemViewModel roller, string hex)
    {
        if (global::Avalonia.Media.Color.TryParse(hex, out var col))
        {
            var (r, g, b) = ColorStudioHelper.CalibrateForHardwareLed(col.R, col.G, col.B, roller.Brightness);
            _deviceService.SetRollerLedColor(roller.Index, r, g, b);
        }
        ScheduleAutoSave();
    }

    private void OnRollerBrightnessChanged(RollerItemViewModel roller, byte brightness)
    {
        OnRollerColorChanged(roller, roller.ColorHex);
        ScheduleAutoSave();
    }

    private void WireHardwareEvents()
    {
        _deviceService.KeyStateChanged += OnKeyStateChanged;
        _deviceService.SliderValueChanged += OnSliderValueChanged;
        _deviceService.RollerRotated += OnRollerRotated;
        _deviceService.KnobRotated += OnKnobRotated;
        _deviceService.KnobPressed += OnKnobPressed;
        _deviceService.DialRotated += OnDialRotated;
        _deviceService.DialPressed += OnDialPressed;
        _deviceService.StandbyStateChanged += OnStandbyStateChanged;
        _deviceService.BaseOrientationChanged += OnBaseOrientationChanged;
        _deviceService.EffectiveRotationChanged += OnEffectiveRotationChanged;
    }

    private void WireRenderingPipeline()
    {
        _keyRenderer.KeyRendered += OnKeyRenderedByService;
        _animatedKeyRenderer.FrameRenderedWithJpeg += OnAnimatedFrameRendered;

        // Inicjalne renderowanie podglądu UI bez zalewania magistrali USB
        for (int i = 0; i < Keys.Count; i++)
        {
            RenderKeyVisual(Keys[i], sendToHardware: false);
        }
    }

    private void WireMonitoringPipeline()
    {
        try
        {
            _systemMonitorService.MetricsUpdated += OnMetricsUpdated;
            UpdateMonitoringDemand();   // monitor działa tylko, gdy jakiś klawisz pokazuje metryki/zegar
            StartReconnectTimer();
            WireMacroEvents();

            _nowPlayingService.TrackChanged += OnMetricsUpdated;

            _obsService.CurrentProgramSceneChanged += OnObsSceneChanged;
            _obsService.StreamStateChanged += OnObsStreamChanged;
            _obsService.RecordStateChanged += OnObsRecordChanged;
            _obsService.VirtualCamStateChanged += OnObsVirtualCamChanged;
            _obsService.ReplayBufferStateChanged += OnObsReplayBufferChanged;
            _obsService.StudioModeStateChanged += OnObsStudioModeChanged;
            _obsService.ConnectionStateChanged += OnObsConnectionStateChanged;
            _discordService.VoiceStateChanged += OnDiscordVoiceStateChanged;
            _youtubeService.StatusUpdated += OnYouTubeStatusUpdated;
            _twitchService.EmoteOnlyStateChanged += OnTwitchEmoteOnlyChanged;
            _twitchService.SubOnlyStateChanged += OnTwitchSubOnlyChanged;
            _kickService.EmoteOnlyStateChanged += OnKickEmoteOnlyChanged;
            _kickService.SubOnlyStateChanged += OnKickSubOnlyChanged;
            _jetKvmService.PowerStateChanged += OnJetKvmPowerStateChanged;
            _spotifyService.PlaybackStateChanged += OnSpotifyPlaybackStateChanged;
            _vlcService.PlaybackStateChanged += OnVlcPlaybackStateChanged;
            _voicemodService.StateChanged += OnVoicemodStateChanged;
            _voicemodService.ConnectionStateChanged += OnVoicemodConnectionChanged;
            _voicemodService.VoicesLoaded += OnVoicemodVoicesLoaded;
            _voicemodService.SoundsLoaded += OnVoicemodSoundsLoaded;

            // Automatyczne podłączenie do Discord Desktop w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    bool isDiscordRunning = MasterHub.Core.SystemControl.ProcessHelper.IsAnyProcessRunning("Discord", "DiscordCanary", "DiscordPTB");

                    if (isDiscordRunning)
                    {
                        var dcCfg = _profileService.GetDiscordSettings();
                        await _discordService.ConnectAsync(dcCfg.ClientId, dcCfg.AccessToken);
                    }
                }
                catch { }
            });

            // Automatyczne podłączenie do OBS Studio WebSocket w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var obsCfg = _profileService.GetObsSettings();
                    if (obsCfg.AutoConnect)
                    {
                        bool isLocal = string.IsNullOrWhiteSpace(obsCfg.Host) ||
                                       obsCfg.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                                       obsCfg.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

                        bool isRunning = !isLocal || MasterHub.Core.SystemControl.ProcessHelper.IsAnyProcessRunning("obs64", "obs32", "obs");

                        if (isRunning)
                        {
                            await _obsService.ConnectAsync(obsCfg.Host, obsCfg.Port, obsCfg.Password);
                        }
                    }
                }
                catch { }
            });

            // Automatyczne podłączenie do Twitch w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var twitchCfg = _profileService.GetTwitchSettings();
                    if (twitchCfg.AutoConnect && !string.IsNullOrWhiteSpace(twitchCfg.Channel))
                    {
                        await _twitchService.ConnectAsync(twitchCfg.Channel, twitchCfg.OAuthToken, twitchCfg.BotUsername, twitchCfg.ClientId);
                    }
                }
                catch { }
            });

            // Automatyczne podłączenie do Kick w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var kickCfg = _profileService.GetKickSettings();
                    if (kickCfg.AutoConnect && !string.IsNullOrWhiteSpace(kickCfg.Channel))
                    {
                        await _kickService.ConnectAsync(kickCfg.Channel, kickCfg.AuthToken, kickCfg.BotUsername);
                    }
                }
                catch { }
            });

            // Automatyczna konfiguracja i nasłuch JetKVM w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var jetCfg = _profileService.GetJetKvmSettings();
                    _jetKvmService.Configure(jetCfg);
                    bool isConfigured = !string.IsNullOrWhiteSpace(jetCfg.Host) &&
                                        (!jetCfg.Host.Equals("192.168.1.100", StringComparison.OrdinalIgnoreCase) ||
                                         !string.IsNullOrEmpty(jetCfg.LocalPassword) ||
                                         !string.IsNullOrEmpty(jetCfg.AuthToken));

                    if (jetCfg.AutoConnect && isConfigured)
                    {
                        await _jetKvmService.RefreshStateAsync();
                    }
                }
                catch { }
            });

            // Automatyczna konfiguracja i nasłuch Spotify w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var spCfg = _profileService.GetSpotifySettings();
                    _spotifyService.Configure(spCfg);
                    if (spCfg.AutoConnect && !string.IsNullOrWhiteSpace(spCfg.AccessToken))
                    {
                        await _spotifyService.RefreshPlaybackStateAsync();
                    }
                }
                catch { }
            });

            // Automatyczna konfiguracja i nasłuch VLC w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var vlcCfg = _profileService.GetVlcSettings();
                    _vlcService.Configure(vlcCfg);
                    if (vlcCfg.AutoConnect)
                    {
                        await _vlcService.RefreshPlaybackStateAsync();
                    }
                }
                catch { }
            });

            // Automatyczna konfiguracja i połączenie z Voicemod w tle
            _ = Task.Run(async () =>
            {
                try
                {
                    var vmCfg = _profileService.GetVoicemodSettings();
                    _voicemodService.Configure(vmCfg);
                    if (vmCfg.AutoConnect)
                    {
                        await _voicemodService.ConnectAsync();
                    }
                }
                catch { }
            });

            // Inicjalne pobranie metryk na start
            foreach (var key in Keys)
            {
                UpdateKeyMonitoringValue(key);
            }
        }
        catch { }
    }
}
