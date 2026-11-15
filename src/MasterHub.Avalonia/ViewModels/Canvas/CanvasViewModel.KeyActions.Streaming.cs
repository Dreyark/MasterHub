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
    private bool TryExecuteObsAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // OBS Studio
            case DeckActionType.ObsToggleStream:
                if (!_obsService.IsConnected)
                {
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                _ = ExecuteObsActionAsync(() => _obsService.ToggleStreamAsync());
                break;
            case DeckActionType.ObsStartStream:
                _ = ExecuteObsActionAsync(() => _obsService.StartStreamAsync());
                break;
            case DeckActionType.ObsStopStream:
                _ = ExecuteObsActionAsync(() => _obsService.StopStreamAsync());
                break;
            case DeckActionType.ObsToggleRecord:
                if (!_obsService.IsConnected)
                {
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                _ = ExecuteObsActionAsync(() => _obsService.ToggleRecordAsync());
                break;
            case DeckActionType.ObsStartRecord:
                _ = ExecuteObsActionAsync(() => _obsService.StartRecordAsync());
                break;
            case DeckActionType.ObsStopRecord:
                _ = ExecuteObsActionAsync(() => _obsService.StopRecordAsync());
                break;
            case DeckActionType.ObsToggleVirtualCam:
                if (!_obsService.IsConnected)
                {
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                _ = ExecuteObsActionAsync(() => _obsService.ToggleVirtualCamAsync());
                break;
            case DeckActionType.ObsSaveReplayBuffer:
                _ = ExecuteObsActionAsync(() => _obsService.SaveReplayBufferAsync());
                break;
            case DeckActionType.ObsSceneSwitch:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    _ = ExecuteObsActionAsync(() => _obsService.SetCurrentProgramSceneAsync(key.ActionParameter));
                break;
            case DeckActionType.ObsStudioModeToggle:
                if (!_obsService.IsConnected)
                {
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                _ = ExecuteObsActionAsync(() => _obsService.ToggleStudioModeAsync());
                break;
            case DeckActionType.ObsTriggerTransition:
                _ = ExecuteObsActionAsync(() => _obsService.TriggerStudioModeTransitionAsync());
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteTwitchAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Twitch Chat & Helix
            case DeckActionType.TwitchChatEmoteOnly:
                Track(key, _twitchService.ToggleEmoteOnlyAsync());
                key.IsActiveToggleState = !key.IsActiveToggleState;
                RenderKeyVisual(key, sendToHardware: true);
                break;
            case DeckActionType.TwitchChatSlowMode:
                {
                    int secs = int.TryParse(key.ActionParameter, out int s) && s > 0 ? s : 30;
                    Track(key, _twitchService.ToggleSlowModeAsync(secs));
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                break;
            case DeckActionType.TwitchChatSubOnly:
                Track(key, _twitchService.ToggleSubOnlyAsync());
                key.IsActiveToggleState = !key.IsActiveToggleState;
                RenderKeyVisual(key, sendToHardware: true);
                break;
            case DeckActionType.TwitchClearChat:
                Track(key, _twitchService.ClearChatAsync());
                break;
            case DeckActionType.TwitchSendMessage:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    Track(key, _twitchService.SendChatMessageAsync(key.ActionParameter));
                break;
            case DeckActionType.TwitchCreateClip:
                Track(key, _twitchService.CreateClipAsync());
                break;
            case DeckActionType.TwitchCreateMarker:
                Track(key, _twitchService.CreateStreamMarkerAsync(key.ActionParameter));
                break;
            case DeckActionType.TwitchStartRaid:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    Track(key, _twitchService.StartRaidAsync(key.ActionParameter));
                break;
            case DeckActionType.TwitchCancelRaid:
                Track(key, _twitchService.CancelRaidAsync());
                break;
            case DeckActionType.TwitchRunCommercial:
                {
                    int commSecs = int.TryParse(key.ActionParameter, out int cs) && cs > 0 ? cs : 30;
                    Track(key, _twitchService.StartCommercialAsync(commSecs));
                }
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteKickAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Kick Chat & Actions
            case DeckActionType.KickChatEmoteOnly:
                Track(key, _kickService.ToggleEmoteOnlyAsync());
                key.IsActiveToggleState = !key.IsActiveToggleState;
                RenderKeyVisual(key, sendToHardware: true);
                break;
            case DeckActionType.KickChatSlowMode:
                {
                    int secs = int.TryParse(key.ActionParameter, out int s) && s > 0 ? s : 30;
                    Track(key, _kickService.ToggleSlowModeAsync(secs));
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                break;
            case DeckActionType.KickChatSubOnly:
                Track(key, _kickService.ToggleSubOnlyAsync());
                key.IsActiveToggleState = !key.IsActiveToggleState;
                RenderKeyVisual(key, sendToHardware: true);
                break;
            case DeckActionType.KickClearChat:
                Track(key, _kickService.ClearChatAsync());
                break;
            case DeckActionType.KickSendMessage:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    Track(key, _kickService.SendChatMessageAsync(key.ActionParameter));
                break;
            case DeckActionType.KickCreateClip:
                Track(key, _kickService.CreateClipAsync());
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteStreamingAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Streaming (YouTube & Streamlabs)
            case DeckActionType.YouTubeLiveViewers:
                Track(key, _youtubeService.FetchStreamStatusAsync(key.ActionParameter));
                break;
            case DeckActionType.StreamlabsSceneSwitch:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    Track(key, _streamlabsService.SwitchSceneAsync(key.ActionParameter));
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteDiscordAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Discord
            case DeckActionType.DiscordMuteToggle:
                var dcMuteCfg = _profileService.GetDiscordSettings();
                if (dcMuteCfg.UseRpc && _discordService.IsConnected && _discordService.IsAuthenticated)
                {
                    Track(key, _discordService.ToggleMuteAsync());
                }
                else
                {
                    string hotkey = !string.IsNullOrWhiteSpace(dcMuteCfg.MuteHotkey) ? dcMuteCfg.MuteHotkey : "Ctrl+Shift+M";
                    _systemControl.SendHotkey(hotkey);
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                break;
            case DeckActionType.DiscordDeafenToggle:
                var dcDeafenCfg = _profileService.GetDiscordSettings();
                if (dcDeafenCfg.UseRpc && _discordService.IsConnected && _discordService.IsAuthenticated)
                {
                    Track(key, _discordService.ToggleDeafenAsync());
                }
                else
                {
                    string hotkey = !string.IsNullOrWhiteSpace(dcDeafenCfg.DeafenHotkey) ? dcDeafenCfg.DeafenHotkey : "Ctrl+Shift+D";
                    _systemControl.SendHotkey(hotkey);
                    key.IsActiveToggleState = !key.IsActiveToggleState;
                    RenderKeyVisual(key, sendToHardware: true);
                }
                break;
            case DeckActionType.DiscordCameraToggle:
                // Discord RPC nie udostępnia komendy kamery (TOGGLE_VIDEO nie istnieje) - zawsze skrót klawiszowy.
                var dcCamCfg = _profileService.GetDiscordSettings();
                _systemControl.SendHotkey(!string.IsNullOrWhiteSpace(dcCamCfg.CameraHotkey) ? dcCamCfg.CameraHotkey : "Ctrl+Shift+O");
                key.IsActiveToggleState = !key.IsActiveToggleState;
                RenderKeyVisual(key, sendToHardware: true);
                break;

            case DeckActionType.DiscordScreenShareToggle:
                // Discord RPC nie udostępnia komendy Go Live / screen share (TOGGLE_SCREENSHARE nie istnieje) - skrót klawiszowy.
                var dcShareCfg = _profileService.GetDiscordSettings();
                _systemControl.SendHotkey(!string.IsNullOrWhiteSpace(dcShareCfg.ScreenShareHotkey) ? dcShareCfg.ScreenShareHotkey : "Ctrl+Shift+S");
                key.IsActiveToggleState = !key.IsActiveToggleState;
                RenderKeyVisual(key, sendToHardware: true);
                break;

            case DeckActionType.DiscordPushToTalk:
                // Obsługiwane bezpośrednio w OnKeyStateChanged (press / release)
                break;
            case DeckActionType.DiscordJoinVoiceChannel:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                {
                    var (guildId, channelId) = DiscordService.ExtractGuildAndChannelId(key.ActionParameter);
                    string targetChannel = channelId ?? key.ActionParameter.Trim();
                    string targetGuild = !string.IsNullOrWhiteSpace(guildId) ? guildId : "@me";

                    if (_discordService.IsConnected && _discordService.IsAuthenticated)
                    {
                        Track(key, _discordService.SelectVoiceChannelAsync(targetChannel));
                    }

                    // Natywny protokół URI discord:// natychmiast przenosi i dołącza do kanału w aplikacji Discord Desktop
                    try
                    {
                        Process.Start(new ProcessStartInfo($"discord://-/channels/{targetGuild}/{targetChannel}") { UseShellExecute = true });
                    }
                    catch { }
                }
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteTeamsAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // MS Teams
            case DeckActionType.TeamsMuteToggle:
                Track(key, _teamsService.ToggleMuteAsync());
                break;
            case DeckActionType.TeamsCameraToggle:
                Track(key, _teamsService.ToggleCameraAsync());
                break;
            case DeckActionType.TeamsRaiseHand:
                Track(key, _teamsService.ToggleRaiseHandAsync());
                break;
            case DeckActionType.TeamsLeaveMeeting:
                Track(key, _teamsService.LeaveMeetingAsync());
                break;

            default:
                return false;
        }

        return true;
    }
}
