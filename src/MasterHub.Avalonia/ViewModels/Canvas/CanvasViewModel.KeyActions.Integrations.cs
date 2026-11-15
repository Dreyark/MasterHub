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
    private bool TryExecuteJetKvmAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // JetKVM ATX Remote Power Control
            case DeckActionType.JetKvmPowerShort:
                Track(key, _jetKvmService.PowerShortPressAsync());
                break;
            case DeckActionType.JetKvmPowerLong:
                Track(key, _jetKvmService.PowerLongPressAsync());
                break;
            case DeckActionType.JetKvmReset:
                Track(key, _jetKvmService.ResetAsync());
                break;
            case DeckActionType.JetKvmPowerStatus:
                Track(key, _jetKvmService.RefreshStateAsync());
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteSpotifyAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Spotify Extended Playback & Playlists
            case DeckActionType.SpotifyToggleShuffle:
                Track(key, _spotifyService.ToggleShuffleAsync());
                break;
            case DeckActionType.SpotifyToggleRepeat:
                Track(key, _spotifyService.ToggleRepeatAsync());
                break;
            case DeckActionType.SpotifyLikeTrack:
                Track(key, _spotifyService.ToggleLikeTrackAsync());
                break;
            case DeckActionType.SpotifyAddToQueue:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                {
                    Track(key, _spotifyService.AddToQueueAsync(key.ActionParameter));
                }
                break;
            case DeckActionType.SpotifyPlayUri:
                Track(key, _spotifyService.PlayUriAsync(key.ActionParameter));
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteVlcAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // VLC Media Player
            case DeckActionType.VlcPlayPause:
                Track(key, _vlcService.PlayPauseAsync());
                break;
            case DeckActionType.VlcStop:
                Track(key, _vlcService.StopAsync());
                break;
            case DeckActionType.VlcNext:
                Track(key, _vlcService.NextTrackAsync());
                break;
            case DeckActionType.VlcPrevious:
                Track(key, _vlcService.PreviousTrackAsync());
                break;
            case DeckActionType.VlcFullscreenToggle:
                Track(key, _vlcService.ToggleFullscreenAsync());
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteVoicemodAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Voicemod
            case DeckActionType.VoicemodVoiceSelect:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                {
                    Track(key, _voicemodService.SelectVoiceAsync(key.ActionParameter));
                }
                break;
            case DeckActionType.VoicemodVoiceToggle:
                Track(key, _voicemodService.ToggleVoiceChangerAsync());
                break;
            case DeckActionType.VoicemodMuteVoice:
                Track(key, _voicemodService.ToggleMuteMicAsync());
                break;
            case DeckActionType.VoicemodHearMyVoiceToggle:
                Track(key, _voicemodService.ToggleHearMyVoiceAsync());
                break;
            case DeckActionType.VoicemodBackgroundToggle:
                Track(key, _voicemodService.ToggleBackgroundEffectsAsync());
                break;
            case DeckActionType.VoicemodPlaySound:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                {
                    Track(key, _voicemodService.PlaySoundAsync(key.ActionParameter));
                }
                break;
            case DeckActionType.VoicemodStopAllSounds:
                Track(key, _voicemodService.StopAllSoundsAsync());
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteHomeAssistantAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Smart Home: Home Assistant
            case DeckActionType.HomeAssistantToggle:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter) && _homeAssistantService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        bool ok = await _homeAssistantService.ToggleAsync(key.ActionParameter);
                        if (ok)
                        {
                            var entity = _homeAssistantService.GetEntity(key.ActionParameter);
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (entity != null)
                                {
                                    key.IsActiveToggleState = entity.IsOn;
                                }
                                else
                                {
                                    key.IsActiveToggleState = !key.IsActiveToggleState;
                                }
                                RenderKeyVisual(key, sendToHardware: true);
                            });
                        }
                    });
                }
                break;
            case DeckActionType.HomeAssistantTurnOn:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter) && _homeAssistantService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        bool ok = await _homeAssistantService.TurnOnAsync(key.ActionParameter);
                        if (ok)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                key.IsActiveToggleState = true;
                                RenderKeyVisual(key, sendToHardware: true);
                            });
                        }
                    });
                }
                break;
            case DeckActionType.HomeAssistantTurnOff:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter) && _homeAssistantService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        bool ok = await _homeAssistantService.TurnOffAsync(key.ActionParameter);
                        if (ok)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                key.IsActiveToggleState = false;
                                RenderKeyVisual(key, sendToHardware: true);
                            });
                        }
                    });
                }
                break;
            case DeckActionType.HomeAssistantCallService:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter) && _homeAssistantService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        var parts = key.ActionParameter.Split('.', 2);
                        if (parts.Length == 2)
                        {
                            await _homeAssistantService.CallServiceAsync(parts[0], parts[1]);
                        }
                    });
                }
                break;
            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteStreamDeckAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            case DeckActionType.StreamDeckPluginAction:
                if (!string.IsNullOrEmpty(key.StreamDeckPluginUuid) && _streamDeckPluginHost != null)
                {
                    _ = _streamDeckPluginHost.SendKeyDownAsync(key.Index, key.StreamDeckPluginUuid, key.StreamDeckActionUuid, key.StreamDeckSettingsJson);
                    _ = Task.Delay(50).ContinueWith(_ => _streamDeckPluginHost.SendKeyUpAsync(key.Index, key.StreamDeckPluginUuid, key.StreamDeckActionUuid, key.StreamDeckSettingsJson));
                }
                break;
            default:
                return false;
        }

        return true;
    }
}
