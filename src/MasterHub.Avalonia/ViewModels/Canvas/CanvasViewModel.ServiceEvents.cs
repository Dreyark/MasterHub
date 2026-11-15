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

    private void OnObsSceneChanged(string sceneName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.ObsSceneSwitch)
                {
                    key.IsActiveToggleState = string.Equals(key.ActionParameter, sceneName, StringComparison.OrdinalIgnoreCase);
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsStreamChanged(bool isStreaming)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.ObsToggleStream)
                {
                    key.IsActiveToggleState = isStreaming;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsRecordChanged(bool isRecording)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.ObsToggleRecord)
                {
                    key.IsActiveToggleState = isRecording;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsVirtualCamChanged(bool isActive)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.ObsToggleVirtualCam)
                {
                    key.IsActiveToggleState = isActive;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsReplayBufferChanged(bool isActive)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.ObsSaveReplayBuffer)
                {
                    key.IsActiveToggleState = isActive;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsStudioModeChanged(bool isActive)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.ObsStudioModeToggle)
                {
                    key.IsActiveToggleState = isActive;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnObsConnectionStateChanged(bool isConnected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (isConnected)
            {
                _ = _obsService.RefreshStreamStatusAsync();
                _ = _obsService.RefreshRecordStatusAsync();
                _ = _obsService.RefreshVirtualCamStatusAsync();
                _ = _obsService.RefreshReplayBufferStatusAsync();
                _ = _obsService.GetStudioModeEnabledAsync();
                _ = _obsService.GetCurrentProgramSceneAsync();
            }
        });
    }

    private async Task ExecuteObsActionAsync(Func<Task> obsAction)
    {
        try
        {
            if (!_obsService.IsConnected)
            {
                var obsCfg = _profileService.GetObsSettings();
                await _obsService.ConnectAsync(obsCfg.Host, obsCfg.Port, obsCfg.Password);
            }

            if (_obsService.IsConnected)
            {
                await obsAction();
            }
        }
        catch { }
    }

    private void OnDiscordVoiceStateChanged(bool isMuted, bool isDeafened)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.DiscordMuteToggle)
                {
                    key.IsActiveToggleState = isMuted;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.DiscordDeafenToggle)
                {
                    key.IsActiveToggleState = isDeafened;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnYouTubeStatusUpdated(YouTubeStreamStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.YouTubeLiveViewers)
                {
                    key.LiveValueText = status.IsLive
                        ? FormatViewerCount(status.ConcurrentViewers)
                        : "OFFLINE";
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private static string FormatViewerCount(int count)
    {
        if (count >= 1_000_000) return $"{count / 1_000_000.0:0.#}M";
        if (count >= 1_000) return $"{count / 1_000.0:0.#}K";
        return count.ToString();
    }

    private void OnTwitchEmoteOnlyChanged(bool enabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.TwitchChatEmoteOnly)
                {
                    key.IsActiveToggleState = enabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnTwitchSubOnlyChanged(bool enabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.TwitchChatSubOnly)
                {
                    key.IsActiveToggleState = enabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnKickEmoteOnlyChanged(bool enabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.KickChatEmoteOnly)
                {
                    key.IsActiveToggleState = enabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnKickSubOnlyChanged(bool enabled)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.KickChatSubOnly)
                {
                    key.IsActiveToggleState = enabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnJetKvmPowerStateChanged(JetKvmPowerState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.JetKvmPowerStatus ||
                    key.AssignedAction == DeckActionType.JetKvmPowerShort)
                {
                    key.IsActiveToggleState = state.Power;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnSpotifyPlaybackStateChanged(SpotifyPlaybackState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.SpotifyToggleShuffle)
                {
                    key.IsActiveToggleState = state.IsShuffle;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.SpotifyToggleRepeat)
                {
                    key.IsActiveToggleState = state.RepeatMode != SpotifyRepeatMode.Off;
                    key.ActionParameter = state.RepeatMode switch
                    {
                        SpotifyRepeatMode.Track => "REP 1",
                        SpotifyRepeatMode.Context => "REP ALL",
                        _ => "REP OFF"
                    };
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.SpotifyLikeTrack)
                {
                    key.IsActiveToggleState = state.IsLiked;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnVlcPlaybackStateChanged(VlcPlaybackState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.VlcPlayPause)
                {
                    key.IsActiveToggleState = state.IsPlaying;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.VlcFullscreenToggle)
                {
                    key.IsActiveToggleState = state.IsFullscreen;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnVoicemodStateChanged(VoicemodState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if (key.AssignedAction == DeckActionType.VoicemodVoiceToggle)
                {
                    key.IsActiveToggleState = state.IsVoiceChangerEnabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.VoicemodMuteVoice)
                {
                    key.IsActiveToggleState = state.IsMicMuted;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.VoicemodHearMyVoiceToggle)
                {
                    key.IsActiveToggleState = state.IsHearMyVoiceEnabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.VoicemodBackgroundToggle)
                {
                    key.IsActiveToggleState = state.IsBackgroundEffectsEnabled;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                else if (key.AssignedAction == DeckActionType.VoicemodVoiceSelect)
                {
                    bool isCurrent = !string.IsNullOrWhiteSpace(key.ActionParameter) &&
                                     (string.Equals(key.ActionParameter, state.CurrentVoiceId, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(key.ActionParameter, state.CurrentVoiceName, StringComparison.OrdinalIgnoreCase));
                    key.IsActiveToggleState = isCurrent;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
            SelectedKey?.RefreshVoicemodProperties();
        });
    }

    private void OnVoicemodConnectionChanged(bool connected)
    {
        Dispatcher.UIThread.Post(() => SelectedKey?.RefreshVoicemodProperties());
    }

    private void OnVoicemodVoicesLoaded(IReadOnlyList<VoicemodVoiceItem> voices)
    {
        Dispatcher.UIThread.Post(() => SelectedKey?.RefreshVoicemodProperties());
    }

    private void OnVoicemodSoundsLoaded(IReadOnlyList<VoicemodSoundItem> sounds)
    {
        Dispatcher.UIThread.Post(() => SelectedKey?.RefreshVoicemodProperties());
    }

    private void OnKeyStateChanged(int keyIndex, bool isPressed)
    {
        if (keyIndex >= 0 && keyIndex < Keys.Count)
        {
            var key = Keys[keyIndex];
            key.IsPressed = isPressed;

            if (key.AssignedAction == DeckActionType.DiscordPushToTalk)
            {
                _ = _discordService.SetPushToTalkActiveAsync(isPressed);
            }

            if (key.AssignedAction == DeckActionType.StreamDeckPluginAction && !string.IsNullOrEmpty(key.StreamDeckPluginUuid))
            {
                if (isPressed)
                {
                    _ = _streamDeckPluginHost?.SendKeyDownAsync(key.Index, key.StreamDeckPluginUuid, key.StreamDeckActionUuid, key.StreamDeckSettingsJson);
                }
                else
                {
                    _ = _streamDeckPluginHost?.SendKeyUpAsync(key.Index, key.StreamDeckPluginUuid, key.StreamDeckActionUuid, key.StreamDeckSettingsJson);
                }
            }

            if (key.HasActiveImage || !string.IsNullOrWhiteSpace(key.ActiveCustomTitle) || !string.IsNullOrWhiteSpace(key.ActiveBackgroundColorHex) || key.IsActiveToggleState)
            {
                RenderKeyVisual(key, sendToHardware: true);
            }

            OnHoldConfirmKey(key, isPressed);   // T-03: bar on a power key while it is held
            if (HandleMutePress(key, isPressed)) return;   // T-17 f: push-to-talk / push-to-mute

            if (key.HasGestures)
            {
                // Press + Hold and/or Double on one key: the engine tells them apart (the tap waits for the release).
                _keyInteractionEngine.HandleGestureEvent(keyIndex, isPressed, key.GestureBindings);
                return;
            }

            var (engineMode, holdMs) = PowerActionGuard.Resolve(key.AssignedAction, key.PressMode, key.SkipPowerConfirmation);
            _keyInteractionEngine.HandleKeyEvent(keyIndex, isPressed, engineMode, holdMs);
        }
    }

    private void OnKeyTriggered(int keyIndex, KeyPressMode mode)
    {
        if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            RunTriggeredKey(keyIndex, mode);
        }
        else
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => RunTriggeredKey(keyIndex, mode));
        }
    }

    private void RunTriggeredKey(int keyIndex, KeyPressMode mode)
    {
        if (keyIndex < 0 || keyIndex >= Keys.Count) return;

        var key = Keys[keyIndex];
        CountPress(key);   // T-37: counted only when the user turned counting on
        EndHoldProgress(key);   // the hold is complete: the bar goes away

        // Hold-to-confirm: the engine forced OnHold for this destructive key and the hold has now elapsed,
        // so the power action runs without a second question.
        bool confirmedByHold = mode == KeyPressMode.OnHold
                               && PowerActionGuard.IsDestructive(key.AssignedAction)
                               && !key.SkipPowerConfirmation;
        if (confirmedByHold) _holdConfirmedKey = key;
        try
        {
            ExecuteKeyAction(key);
        }
        finally
        {
            if (confirmedByHold) _holdConfirmedKey = null;
        }
    }
}
