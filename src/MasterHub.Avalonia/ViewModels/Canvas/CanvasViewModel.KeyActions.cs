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
    /// <summary>
    /// Wykonuje akcję przypisaną do klawisza. Dispatcher deleguje do handlerów per kategoria
    /// (każdy zwraca true, jeśli rozpoznał typ akcji). Kategorie zewnętrzne: KeyActions.Hardware / Streaming / Integrations.
    /// </summary>
    public void ExecuteKeyAction(KeyItemViewModel key)
    {
        if (key == null || key.AssignedAction == DeckActionType.None) return;
        if (BlockedByDryRun(key)) return;   // T-08: a draft is on the deck, nothing real may run
        WarnIfKeystrokesWillBeDropped(key);   // T-34 #12: the program in front runs as administrator

        try
        {
            _ = TryExecuteMediaAction(key)
                || TryExecuteAudioAction(key)
                || TryExecuteSystemToolsAction(key)
                || TryExecutePowerAction(key)
                || TryExecuteRazerAction(key)
                || TryExecuteWootingAction(key)
                || TryExecuteMacroAction(key)
                || TryExecuteObsAction(key)
                || TryExecuteTwitchAction(key)
                || TryExecuteKickAction(key)
                || TryExecuteJetKvmAction(key)
                || TryExecuteSpotifyAction(key)
                || TryExecuteVlcAction(key)
                || TryExecuteVoicemodAction(key)
                || TryExecuteStreamingAction(key)
                || TryExecuteDiscordAction(key)
                || TryExecuteTeamsAction(key)
                || TryExecuteNavigationAction(key)
                || TryExecuteMonitorAction(key)
                || TryExecuteHomeAssistantAction(key)
                || TryExecuteStreamDeckAction(key)
                || TryExecuteLiveValueAction(key)
                || TryExecuteTimerAction(key)
                || TryExecuteSoundAction(key)
                || TryExecuteBrightnessAction(key)
                || TryExecuteSmallAction(key);   // T-17 c–j
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error executing action {key.AssignedAction}: {ex.Message}");
        }
    }

    private bool TryExecuteMediaAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Multimedia
            case DeckActionType.MediaPlayPause:
                _systemControl.PlayPause();
                key.IsActiveToggleState = !key.IsActiveToggleState;
                RenderKeyVisual(key, sendToHardware: true);
                break;
            case DeckActionType.MediaNext:
                _systemControl.NextTrack();
                break;
            case DeckActionType.MediaPrevious:
                _systemControl.PreviousTrack();
                break;
            case DeckActionType.MediaStop:
                _systemControl.StopTrack();
                key.IsActiveToggleState = false;
                RenderKeyVisual(key, sendToHardware: true);
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteAudioAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Audio
            case DeckActionType.VolumeUp:
                _systemControl.VolumeUp();
                break;
            case DeckActionType.VolumeDown:
                _systemControl.VolumeDown();
                break;
            case DeckActionType.MasterMute:
                _audioService.ToggleMasterPlaybackMute();
                key.IsActiveToggleState = !key.IsActiveToggleState;
                RenderKeyVisual(key, sendToHardware: true);
                break;
            case DeckActionType.AudioOutputSwitch:
                SwitchAudioDevice(key);
                break;
            case DeckActionType.MicMute:
                _audioService.ToggleMasterRecordingMute();
                key.IsActiveToggleState = !key.IsActiveToggleState;
                RenderKeyVisual(key, sendToHardware: true);
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteSystemToolsAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Narzędzia systemowe
            case DeckActionType.Screenshot:
                _systemControl.TakeScreenshot();
                break;
            case DeckActionType.TaskManager:
                _systemControl.OpenTaskManager();
                break;
            case DeckActionType.Calculator:
                _systemControl.OpenCalculator();
                break;
            case DeckActionType.Browser:
                _systemControl.OpenDefaultBrowser();
                break;
            case DeckActionType.OpenApplication:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    _systemControl.OpenApplication(key.ActionParameter);
                break;
            case DeckActionType.OpenWebsite:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    _systemControl.OpenWebsite(key.ActionParameter);
                break;
            case DeckActionType.OpenFile:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    _systemControl.OpenFile(key.ActionParameter);
                break;
            case DeckActionType.CloseApplication:
                _systemControl.CloseApplication(key.ActionParameter);
                break;
            case DeckActionType.TextPaste:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    _systemControl.PasteText(key.ActionParameter);
                break;
            case DeckActionType.Hotkey:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                    _systemControl.SendHotkey(key.ActionParameter, key.HotkeySendMode, key.HotkeyHoldMs);
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecutePowerAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Zasilanie i blokada
            case DeckActionType.SystemSleep:
                RunPowerAction(key, "Sleep", _systemControl.SystemSleep);
                break;
            case DeckActionType.SystemShutdown:
                RunPowerAction(key, "Shutdown", _systemControl.SystemShutdown);
                break;
            case DeckActionType.SystemRestart:
                RunPowerAction(key, "Restart", _systemControl.SystemRestart);
                break;
            case DeckActionType.SystemLockScreen:
                _systemControl.SystemLockScreen();
                break;
            case DeckActionType.SystemScreensaver:
                _systemControl.SystemScreensaver();
                break;
            case DeckActionType.SystemVirtualDesktopNext:
                _systemControl.VirtualDesktopNext();
                break;
            case DeckActionType.SystemVirtualDesktopPrev:
                _systemControl.VirtualDesktopPrev();
                break;
            case DeckActionType.SystemVirtualDesktopCreate:
                _systemControl.VirtualDesktopCreate();
                break;
            case DeckActionType.SystemVirtualDesktopClose:
                _systemControl.VirtualDesktopClose();
                break;
            case DeckActionType.SystemTaskView:
                _systemControl.TaskView();
                break;
            case DeckActionType.SystemShowDesktop:
                _systemControl.ShowDesktop();
                key.IsActiveToggleState = !key.IsActiveToggleState;
                RenderKeyVisual(key, sendToHardware: true);
                break;
            case DeckActionType.SystemClipboardHistory:
                _systemControl.OpenClipboardHistory();
                break;
            case DeckActionType.ConditionalAction:
                ExecuteConditionalAction(key);
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteNavigationAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Nawigacja & Profile
            case DeckActionType.GoToProfile:
                key.IsPressed = false;
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                {
                    var targetProf = _profileService.GetAllProfiles().FirstOrDefault(p =>
                        p.Id.Equals(key.ActionParameter, StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals(key.ActionParameter, StringComparison.OrdinalIgnoreCase));
                    if (targetProf != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            DeselectAll();
                            _profileService.SetActiveProfile(targetProf.Id);
                            ApplyProfile(targetProf);
                        });
                    }
                }
                break;
            case DeckActionType.GoToPage:
                key.IsPressed = false;
                {
                    int targetPage = ParsePageIndex(key.ActionParameter);
                    Dispatcher.UIThread.Post(() =>
                    {
                        DeselectAll();
                        SwitchToPage(targetPage);
                    });
                }
                break;
            case DeckActionType.OpenFolder:
                key.IsPressed = false;
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        DeselectAll();
                        OpenFolder(key.ActionParameter);
                    });
                }
                break;
            case DeckActionType.GoBack:
                key.IsPressed = false;
                Dispatcher.UIThread.Post(() =>
                {
                    DeselectAll();
                    NavigateBack();
                });
                break;
            case DeckActionType.GoHome:
                key.IsPressed = false;
                Dispatcher.UIThread.Post(() =>
                {
                    DeselectAll();
                    NavigateHome();
                });
                break;
            case DeckActionType.NextPage:
                key.IsPressed = false;
                Dispatcher.UIThread.Post(() =>
                {
                    DeselectAll();
                    NextPage();
                });
                break;
            case DeckActionType.PreviousPage:
                key.IsPressed = false;
                Dispatcher.UIThread.Post(() =>
                {
                    DeselectAll();
                    PreviousPage();
                });
                break;
            case DeckActionType.HotkeySwitch:
                if (!string.IsNullOrWhiteSpace(key.ActionParameter))
                {
                    var parts = key.ActionParameter.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        key.IsActiveToggleState = !key.IsActiveToggleState;
                        string hk = key.IsActiveToggleState ? parts[1] : parts[0];
                        _systemControl.SendHotkey(hk);
                        RenderKeyVisual(key, sendToHardware: true);
                    }
                    else
                    {
                        _systemControl.SendHotkey(key.ActionParameter);
                    }
                }
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteMonitorAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Akcje monitora sprzętowego – intencjonalnie brak uruchamiania Menedżera Zadań:
            // Zgodnie z wymaganiem: "tak samo jak są kacje monitora to nei chce by defaultowo otwierało manager zadan"
            case DeckActionType.DisplayCpuUsage:
            case DeckActionType.DisplayRamUsage:
            case DeckActionType.DisplayGpuUsage:
            case DeckActionType.DisplayCpuTemp:
            case DeckActionType.DisplayGpuTemp:
            case DeckActionType.DisplayNetworkSpeed:
            case DeckActionType.DisplayDiskUsage:
            case DeckActionType.DisplayClock:
            case DeckActionType.DisplayDate:
            case DeckActionType.DisplayFps:
            case DeckActionType.NowPlaying:
                break;

            default:
                return false;
        }

        return true;
    }

    private void ExecuteConditionalAction(KeyItemViewModel key)
    {
        if (key == null) return;
        var model = ConditionalActionModel.Parse(key.ActionParameter);
        bool conditionMet = model.Evaluate(key.IsActiveToggleState);
        DeckActionType targetAction = conditionMet ? model.IfTrueAction : model.IfFalseAction;
        string targetParam = conditionMet ? model.IfTrueParameter : model.IfFalseParameter;

        if (targetAction == DeckActionType.None || targetAction == DeckActionType.ConditionalAction)
        {
            return;
        }

        using var tempKey = new KeyItemViewModel
        {
            Index = key.Index,
            AssignedAction = targetAction,
            ActionParameter = targetParam,
            IsActiveToggleState = key.IsActiveToggleState
        };

        ExecuteKeyAction(tempKey);
        if (tempKey.IsActiveToggleState != key.IsActiveToggleState)
        {
            key.IsActiveToggleState = tempKey.IsActiveToggleState;
            RenderKeyVisual(key, sendToHardware: true);
        }
    }
}
