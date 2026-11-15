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

    public async Task SendStreamDeckWillAppearAsync(KeyItemViewModel key)
    {
        if (_streamDeckPluginHost == null || string.IsNullOrEmpty(key.StreamDeckPluginUuid)) return;
        await _streamDeckPluginHost.StartPluginAsync(key.StreamDeckPluginUuid);
        await _streamDeckPluginHost.SendWillAppearAsync(key.Index, key.StreamDeckPluginUuid, key.StreamDeckActionUuid, key.StreamDeckSettingsJson);
    }

    public async Task SendStreamDeckWillDisappearAsync(int keyIndex, string pluginUuid, string actionUuid)
    {
        if (_streamDeckPluginHost == null || string.IsNullOrEmpty(pluginUuid)) return;
        await _streamDeckPluginHost.SendWillDisappearAsync(keyIndex, pluginUuid, actionUuid);
    }

    private void OnHomeAssistantEntityStateChanged(object? sender, HomeAssistantEntityModel entity)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var key in Keys)
            {
                if ((key.AssignedAction is DeckActionType.HomeAssistantToggle or
                                           DeckActionType.HomeAssistantTurnOn or
                                           DeckActionType.HomeAssistantTurnOff) &&
                    string.Equals(key.ActionParameter, entity.EntityId, StringComparison.OrdinalIgnoreCase))
                {
                    key.IsActiveToggleState = entity.IsOn;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnStreamDeckKeyImageUpdated(int keyIndex, byte[] jpegBytes)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (keyIndex >= 0 && keyIndex < Keys.Count)
            {
                var key = Keys[keyIndex];
                if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
                {
                    key.SetPluginDynamicImage(jpegBytes);
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnStreamDeckKeyTitleUpdated(int keyIndex, string title)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (keyIndex >= 0 && keyIndex < Keys.Count)
            {
                var key = Keys[keyIndex];
                if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
                {
                    key.CustomTitle = title;
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnStreamDeckKeyStateUpdated(int keyIndex, int state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (keyIndex >= 0 && keyIndex < Keys.Count)
            {
                var key = Keys[keyIndex];
                if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
                {
                    // T-31 d: any number of states – each with its own manifest picture
                    key.PluginStateIndex = state;
                    key.PluginStateImagePath = _streamDeckPluginHost?.GetStateImagePath(keyIndex);
                    key.IsActiveToggleState = (state != 0);
                    RenderKeyVisual(key, sendToHardware: true);
                }
            }
        });
    }

    private void OnStreamDeckKeyAlertRequested(int keyIndex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (keyIndex >= 0 && keyIndex < Keys.Count)
            {
                var key = Keys[keyIndex];
                if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
                {
                    key.IsStreamDeckAlert = true;
                    RenderKeyVisual(key, sendToHardware: true);
                    Task.Delay(1500).ContinueWith(_ =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            key.IsStreamDeckAlert = false;
                            RenderKeyVisual(key, sendToHardware: true);
                        });
                    });
                }
            }
        });
    }

    private void OnStreamDeckKeyOkRequested(int keyIndex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (keyIndex >= 0 && keyIndex < Keys.Count)
            {
                var key = Keys[keyIndex];
                if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
                {
                    key.IsStreamDeckOk = true;
                    RenderKeyVisual(key, sendToHardware: true);
                    Task.Delay(1500).ContinueWith(_ =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            key.IsStreamDeckOk = false;
                            RenderKeyVisual(key, sendToHardware: true);
                        });
                    });
                }
            }
        });
    }

    private void OnStreamDeckKeySettingsUpdated(int keyIndex, string settingsJson)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (keyIndex >= 0 && keyIndex < Keys.Count)
            {
                var key = Keys[keyIndex];
                if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
                {
                    key.StreamDeckSettingsJson = settingsJson;
                    ScheduleAutoSave();
                }
            }
        });
    }

    private void OnStreamDeckSwitchToProfileRequested(string profileName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (string.IsNullOrWhiteSpace(profileName)) return;
            var targetProf = _profileService.GetAllProfiles().FirstOrDefault(p =>
                p.Id.Equals(profileName, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
            if (targetProf != null)
            {
                DeselectAll();
                _profileService.SetActiveProfile(targetProf.Id);
                ApplyProfile(targetProf);
            }
        });
    }
}
