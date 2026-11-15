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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _deviceService.KeyStateChanged -= OnKeyStateChanged;
        _deviceService.SliderValueChanged -= OnSliderValueChanged;
        _deviceService.RollerRotated -= OnRollerRotated;
        _deviceService.KnobRotated -= OnKnobRotated;
        _deviceService.KnobPressed -= OnKnobPressed;
        _deviceService.DialRotated -= OnDialRotated;
        _deviceService.DialPressed -= OnDialPressed;
        _deviceService.StandbyStateChanged -= OnStandbyStateChanged;
        _deviceService.BaseOrientationChanged -= OnBaseOrientationChanged;
        _deviceService.EffectiveRotationChanged -= OnEffectiveRotationChanged;
        _keyRenderer.KeyRendered -= OnKeyRenderedByService;
        _animatedKeyRenderer.FrameRenderedWithJpeg -= OnAnimatedFrameRendered;
        _deviceService.DevicesUpdated -= UpdateConnectionStates;
        _systemMonitorService.MetricsUpdated -= OnMetricsUpdated;

        _obsService.CurrentProgramSceneChanged -= OnObsSceneChanged;
        _obsService.StreamStateChanged -= OnObsStreamChanged;
        _obsService.RecordStateChanged -= OnObsRecordChanged;
        _obsService.VirtualCamStateChanged -= OnObsVirtualCamChanged;
        _obsService.ReplayBufferStateChanged -= OnObsReplayBufferChanged;
        _obsService.StudioModeStateChanged -= OnObsStudioModeChanged;
        _obsService.ConnectionStateChanged -= OnObsConnectionStateChanged;
        _nowPlayingService.TrackChanged -= OnMetricsUpdated;
        _discordService.VoiceStateChanged -= OnDiscordVoiceStateChanged;
        _youtubeService.StatusUpdated -= OnYouTubeStatusUpdated;
        _twitchService.EmoteOnlyStateChanged -= OnTwitchEmoteOnlyChanged;
        _twitchService.SubOnlyStateChanged -= OnTwitchSubOnlyChanged;
        _kickService.EmoteOnlyStateChanged -= OnKickEmoteOnlyChanged;
        _kickService.SubOnlyStateChanged -= OnKickSubOnlyChanged;
        _jetKvmService.PowerStateChanged -= OnJetKvmPowerStateChanged;
        _spotifyService.PlaybackStateChanged -= OnSpotifyPlaybackStateChanged;
        _vlcService.PlaybackStateChanged -= OnVlcPlaybackStateChanged;
        _voicemodService.StateChanged -= OnVoicemodStateChanged;
        _voicemodService.ConnectionStateChanged -= OnVoicemodConnectionChanged;
        _voicemodService.VoicesLoaded -= OnVoicemodVoicesLoaded;
        _voicemodService.SoundsLoaded -= OnVoicemodSoundsLoaded;
        _keyInteractionEngine.KeyTriggered -= OnKeyTriggered;
        _keyInteractionEngine.GestureTriggered -= OnGestureTriggered;
        if (_liveValues != null) _liveValues.ReadingChanged -= OnLiveReadingChanged;
        if (_statusLights != null) _statusLights.Changed -= OnStatusLightChanged;
        _keyInteractionEngine.Dispose();

        if (_streamDeckPluginHost != null)
        {
            _streamDeckPluginHost.KeyImageUpdated -= OnStreamDeckKeyImageUpdated;
            _streamDeckPluginHost.KeyTitleUpdated -= OnStreamDeckKeyTitleUpdated;
            _streamDeckPluginHost.KeyStateUpdated -= OnStreamDeckKeyStateUpdated;
            _streamDeckPluginHost.KeyAlertRequested -= OnStreamDeckKeyAlertRequested;
            _streamDeckPluginHost.KeyOkRequested -= OnStreamDeckKeyOkRequested;
            _streamDeckPluginHost.KeySettingsUpdated -= OnStreamDeckKeySettingsUpdated;
            _streamDeckPluginHost.SwitchToProfileRequested -= OnStreamDeckSwitchToProfileRequested;
        }

        if (_homeAssistantService != null)
        {
            _homeAssistantService.EntityStateChanged -= OnHomeAssistantEntityStateChanged;
        }

        _autoSaveTimer.Stop();
        DisposeLooks();
        DisposePanorama();
        DisposeHoldProgress();
        _deviceService.StandbyStateChanged -= OnStandbyForPanorama;
        DisposeTimers();
        DisposeSoundboard();
        _reconnectTimer?.Dispose();
        UnwireMacroEvents();
        _reconnectTimer = null;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;

        _animatedKeyRenderer.StopAllAnimations();
        _virtualActionKey.Dispose();
        foreach (var k in Keys) k.Dispose();
        foreach (var s in Sliders) s.Dispose();
        foreach (var r in Rollers) r.Dispose();
        foreach (var kn in Knobs) kn.Dispose();
        Dial?.Dispose();
        BaseStation?.Dispose();
        MediaFrameExtractor.ClearCache();
        WindowsAppIconExtractor.ClearCache();
        ButtonImageRenderer.ClearCache();
    }
}
