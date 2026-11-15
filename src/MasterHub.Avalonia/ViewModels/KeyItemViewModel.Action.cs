using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using MasterHub.Core.Actions;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Localization;
using MasterHub.Core.Models;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Rendering;

namespace MasterHub.Avalonia.ViewModels;

public partial class KeyItemViewModel
{

    public DeckActionType AssignedAction
    {
        get => _assignedAction;
        set
        {
            if (SetField(ref _assignedAction, value))
            {
                NotifyActiveSlotChanged();
                if (value != DeckActionType.StreamDeckPluginAction)
                {
                    _streamDeckPluginUuid = "";
                    _streamDeckActionUuid = "";
                    _streamDeckSettingsJson = "";
                    _pluginDynamicImageBytes = null;
                    IsStreamDeckAlert = false;
                    IsStreamDeckOk = false;
                    if (value == DeckActionType.None)
                    {
                        ResetStyling();
                    }
                }
                _liveValueText = null;
                ActionParameter = GetDefaultActionParameter(value);
                if (value != DeckActionType.None)
                {
                    CustomTitle = GetOriginalActionTitle();
                    CustomSubtitle = GetOriginalActionSubtitle();
                }
                OnPropertyChanged(nameof(IsMonitoringAction));
                OnPropertyChanged(nameof(IsPowerConfirmAction));
                OnPropertyChanged(nameof(IsMuteAction));
                NotifyAudioSwitchChanged();
                OnPropertyChanged(nameof(IsLiveValueAction));
                NotifyTimerKindChanged();
                OnPropertyChanged(nameof(IsSoundAction));
                OnPropertyChanged(nameof(IsHotkeyAction));
                OnPropertyChanged(nameof(SubtitleWatermark));
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(ActionCategory));
                OnPropertyChanged(nameof(ActionIcon));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                OnPropertyChanged(nameof(HasAction));
                OnPropertyChanged(nameof(HasSubtitle));
                OnPropertyChanged(nameof(RequiresActionParameter));
                OnPropertyChanged(nameof(IsObsSceneAction));
                OnPropertyChanged(nameof(IsNotObsSceneAction));
                OnPropertyChanged(nameof(IsFileBrowseSupported));
                OnPropertyChanged(nameof(IsGpuMonitorAction));
                OnPropertyChanged(nameof(IsGpuAction));
                OnPropertyChanged(nameof(IsRazerDpiAction));
                OnPropertyChanged(nameof(IsRazerPollingAction));
                OnPropertyChanged(nameof(IsRazerProfileAction));
                OnPropertyChanged(nameof(IsNetworkMonitorAction));
                OnPropertyChanged(nameof(IsDiskAction));
                OnPropertyChanged(nameof(AvailableDrives));
                OnPropertyChanged(nameof(SelectedDrive));
                OnPropertyChanged(nameof(IsDiscordJoinAction));
                OnPropertyChanged(nameof(SelectedNetworkUnit));
                OnPropertyChanged(nameof(IsGenericParameterAction));
                OnPropertyChanged(nameof(IsGoToProfileAction));
                OnPropertyChanged(nameof(IsGoToPageAction));
                OnPropertyChanged(nameof(IsOpenFolderAction));
                OnPropertyChanged(nameof(IsNavigationAction));
                OnPropertyChanged(nameof(IsVoicemodVoiceAction));
                OnPropertyChanged(nameof(IsVoicemodSoundAction));
                OnPropertyChanged(nameof(IsVoicemodAnyAction));
                OnPropertyChanged(nameof(IsVoicemodConnected));
                OnPropertyChanged(nameof(VoicemodConnectionStatusDisplay));
                OnPropertyChanged(nameof(AvailableVoicemodVoices));
                OnPropertyChanged(nameof(SelectedVoicemodVoice));
                OnPropertyChanged(nameof(AvailableVoicemodSounds));
                OnPropertyChanged(nameof(SelectedVoicemodSound));
                OnPropertyChanged(nameof(AvailableProfiles));
                OnPropertyChanged(nameof(SelectedProfile));
                OnPropertyChanged(nameof(AvailablePages));
                OnPropertyChanged(nameof(SelectedPage));
                OnPropertyChanged(nameof(AvailableFolders));
                OnPropertyChanged(nameof(SelectedFolder));
                OnPropertyChanged(nameof(IsHomeAssistantAction));
                OnPropertyChanged(nameof(IsMacroAction));
                OnPropertyChanged(nameof(SelectedMacro));
                OnPropertyChanged(nameof(IsHomeAssistantConnected));
                OnPropertyChanged(nameof(HomeAssistantConnectionStatusDisplay));
                OnPropertyChanged(nameof(AvailableHomeAssistantEntities));
                OnPropertyChanged(nameof(SelectedHomeAssistantEntity));
                OnPropertyChanged(nameof(IsStreamDeckAction));
                OnPropertyChanged(nameof(HasStreamDeckPlugin));
                OnPropertyChanged(nameof(StreamDeckPluginName));
                OnPropertyChanged(nameof(StreamDeckActionName));
                OnPropertyChanged(nameof(StreamDeckActionTooltip));
                OnPropertyChanged(nameof(HasPropertyInspector));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public static string GetDefaultActionParameter(DeckActionType action) => action switch
    {
        DeckActionType.DisplayNetworkSpeed => "MB/s",
        DeckActionType.DisplayDiskUsage => "C:\\",
        DeckActionType.DisplayGpuUsage or DeckActionType.DisplayGpuTemp => "",
        DeckActionType.RazerDpiCycle or DeckActionType.RazerDpiUp or DeckActionType.RazerDpiDown or
        DeckActionType.RazerDpi400 or DeckActionType.RazerDpi800 or DeckActionType.RazerDpi1600 or
        DeckActionType.RazerDpi3200 or DeckActionType.RazerDpi6400 => "1600",
        DeckActionType.RazerPollingRateCycle or DeckActionType.RazerPollingRateUp or DeckActionType.RazerPollingRateDown or
        DeckActionType.RazerPollingRate125 or DeckActionType.RazerPollingRate500 or DeckActionType.RazerPollingRate1000 or
        DeckActionType.RazerPollingRate2000 or DeckActionType.RazerPollingRate4000 or DeckActionType.RazerPollingRate8000 => "1000 Hz",
        DeckActionType.RazerProfile1 or DeckActionType.RazerProfile2 or DeckActionType.RazerProfile3 or
        DeckActionType.RazerProfile4 or DeckActionType.RazerProfile5 or DeckActionType.RazerProfileNext or DeckActionType.RazerProfilePrev => "Profil #1",
        DeckActionType.WootingProfile1 or DeckActionType.WootingProfile2 or DeckActionType.WootingProfile3 or
        DeckActionType.WootingProfileCycle or DeckActionType.WootingProfileNext or DeckActionType.WootingProfilePrev => "1",
        DeckActionType.Delay => "1000",
        DeckActionType.GoToPage => "",
        DeckActionType.GoToProfile => "",
        DeckActionType.OpenFolder => "",
        DeckActionType.NextPage => "",
        DeckActionType.PreviousPage => "",
        DeckActionType.VoicemodVoiceSelect => "nofx",
        DeckActionType.VoicemodPlaySound => "vineboom",
        DeckActionType.HomeAssistantToggle => "light.living_room",
        DeckActionType.HomeAssistantTurnOn => "light.living_room",
        DeckActionType.HomeAssistantTurnOff => "light.living_room",
        DeckActionType.HomeAssistantCallService => "light.toggle",
        _ => ""
    };

    public bool IsMonitoringAction =>
        AssignedAction is DeckActionType.DisplayCpuUsage or
                          DeckActionType.DisplayRamUsage or
                          DeckActionType.DisplayGpuUsage or
                          DeckActionType.DisplayCpuTemp or
                          DeckActionType.DisplayGpuTemp or
                          DeckActionType.DisplayNetworkSpeed or
                          DeckActionType.DisplayDiskUsage or
                          DeckActionType.DisplayClock or
                          DeckActionType.DisplayDate or
                          DeckActionType.DisplayFps or
                          DeckActionType.NowPlaying or
                          DeckActionType.YouTubeLiveViewers or
                          DeckActionType.LiveValue or
                          DeckActionType.TimerCountdown or
                          DeckActionType.TimerPomodoro or
                          DeckActionType.TimerStopwatch;

    public string SubtitleWatermark =>
        IsMonitoringAction && !string.IsNullOrWhiteSpace(_liveValueText)
            ? $"{LocalizationService.Instance.GetString("Key_LiveValue", "Wartość na żywo")}: {_liveValueText}"
            : LocalizationService.Instance.GetString("Key_SubtitlePrompt", "Wpisz podtytuł...");

    public string GetEffectiveSubtitle()
    {
        if (IsMonitoringAction)
        {
            if (!string.IsNullOrWhiteSpace(_liveValueText))
            {
                return _liveValueText;
            }
            if (!string.IsNullOrWhiteSpace(CustomSubtitle))
            {
                return CustomSubtitle;
            }
            return "";
        }

        if (IsActiveToggleState && !string.IsNullOrWhiteSpace(ActiveCustomSubtitle))
        {
            return ActiveCustomSubtitle;
        }

        return CustomSubtitle;
    }
}
