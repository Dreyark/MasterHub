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

    [RelayCommand]
    public void ClearAction()
    {
        _assignedAction = DeckActionType.None;
        ActionParameter = "";
        _liveValueText = null;
        _streamDeckPluginUuid = "";
        _streamDeckActionUuid = "";
        _streamDeckSettingsJson = "";
        _pluginDynamicImageBytes = null;
        IsStreamDeckAlert = false;
        IsStreamDeckOk = false;
        ResetStyling();
        OnPropertyChanged(nameof(AssignedAction));
        OnPropertyChanged(nameof(ActionDisplayName));
        OnPropertyChanged(nameof(ActionCategory));
        OnPropertyChanged(nameof(ActionIcon));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplaySubtitle));
        OnPropertyChanged(nameof(HasAction));
        OnPropertyChanged(nameof(HasSubtitle));
        OnPropertyChanged(nameof(RequiresActionParameter));
        OnPropertyChanged(nameof(IsMonitoringAction));
        OnPropertyChanged(nameof(LiveValueText));
        OnPropertyChanged(nameof(IsVoicemodAnyAction));
        OnPropertyChanged(nameof(IsNavigationAction));
        OnPropertyChanged(nameof(IsGenericParameterAction));
        OnPropertyChanged(nameof(IsFileBrowseSupported));
        OnPropertyChanged(nameof(StreamDeckPluginUuid));
        OnPropertyChanged(nameof(StreamDeckActionUuid));
        OnPropertyChanged(nameof(StreamDeckSettingsJson));
        ActionChangedCallback?.Invoke(this);
    }

    public bool HasAction => AssignedAction != DeckActionType.None;
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(DisplaySubtitle);

    public string DisplayTitle
    {
        get
        {
            if (IsActiveToggleState && ActiveCustomTitle != null) return ActiveCustomTitle;
            if (CustomTitle != null) return CustomTitle;
            return GetOriginalActionTitle();
        }
    }

    public string GetOriginalActionTitle()
    {
        var loc = LocalizationService.Instance;
        return AssignedAction switch
        {
                DeckActionType.None => "",
                DeckActionType.MediaPlayPause => "PLAY/PAUSE",
                DeckActionType.MediaNext => loc.GetString("Deck_Next", "NASTĘPNY"),
                DeckActionType.MediaPrevious => loc.GetString("Deck_Prev", "POPRZEDNI"),
                DeckActionType.MediaStop => "STOP",
                DeckActionType.MasterMute => loc.GetString("Deck_Mute", "WYCISZ"),
                DeckActionType.MicMute => loc.GetString("Deck_Mic", "MIKROFON"),
                DeckActionType.AudioOutputSwitch => AudioSwitchTitle(),
                DeckActionType.LiveValue => loc.GetString("Deck_LiveValue", "LIVE"),
                DeckActionType.TimerCountdown => loc.GetString("Deck_Timer", "TIMER"),
                DeckActionType.TimerPomodoro => loc.GetString("Deck_Pomodoro", "POMODORO"),
                DeckActionType.TimerStopwatch => loc.GetString("Deck_Stopwatch", "STOPER"),
                DeckActionType.SoundPlay => loc.GetString("Deck_Sound", "SOUND"),
                DeckActionType.SoundStopAll => loc.GetString("Deck_SoundStop", "STOP"),
                DeckActionType.DeckBrightnessUp => loc.GetString("Deck_BrightUp", "JAŚNIEJ"),
                DeckActionType.DeckBrightnessDown => loc.GetString("Deck_BrightDown", "CIEMNIEJ"),
                DeckActionType.DeckBrightnessCycle => loc.GetString("Deck_BrightCycle", "JASNOŚĆ"),
                DeckActionType.MediaSeekForward => loc.GetString("Deck_SeekFwd", "+SEEK"),
                DeckActionType.MediaSeekBackward => loc.GetString("Deck_SeekBack", "-SEEK"),
                DeckActionType.TypeText => loc.GetString("Deck_TypeText", "TYPE"),
                DeckActionType.ForegroundAppVolumeUp => loc.GetString("Deck_AppVolUp", "APP +"),
                DeckActionType.ForegroundAppVolumeDown => loc.GetString("Deck_AppVolDown", "APP −"),
                DeckActionType.ForegroundAppMute => loc.GetString("Deck_AppMute", "APP MUTE"),
                DeckActionType.HttpRequest => loc.GetString("Deck_Http", "URL"),
                DeckActionType.VolumeUp => loc.GetString("Deck_VolUp", "GŁOŚNIEJ"),
                DeckActionType.VolumeDown => loc.GetString("Deck_VolDown", "CISZEJ"),
                DeckActionType.Screenshot => loc.GetString("Deck_Screenshot", "ZRZUT"),
                DeckActionType.TaskManager => loc.GetString("Deck_TaskMgr", "MENEDŻER"),
                DeckActionType.Calculator => loc.GetString("Deck_Calc", "KALKULATOR"),
                DeckActionType.Browser => loc.GetString("Deck_Browser", "INTERNET"),
                DeckActionType.OpenApplication => !string.IsNullOrWhiteSpace(ActionParameter) ? Path.GetFileNameWithoutExtension(ActionParameter).ToUpperInvariant() : "LAUNCH",
                DeckActionType.OpenWebsite => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter.Replace("https://", "").Replace("http://", "").Split('/')[0].ToUpperInvariant() : "URL",
                DeckActionType.OpenFile => !string.IsNullOrWhiteSpace(ActionParameter) ? Path.GetFileName(ActionParameter).ToUpperInvariant() : "FILE",
                DeckActionType.CloseApplication => !string.IsNullOrWhiteSpace(ActionParameter) ? $"X {Path.GetFileNameWithoutExtension(ActionParameter).ToUpperInvariant()}" : "CLOSE",
                DeckActionType.TextPaste => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter) : "PASTE",
                DeckActionType.Hotkey => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : "HOTKEY",
                DeckActionType.HotkeySwitch => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : "TOGGLE",
                DeckActionType.MultiAction => "MULTI",
                DeckActionType.Delay => !string.IsNullOrWhiteSpace(ActionParameter) ? $"{ActionParameter}MS" : "DELAY",
                DeckActionType.SystemSleep => "SLEEP",
                DeckActionType.SystemShutdown => "SHUTDOWN",
                DeckActionType.SystemRestart => "RESTART",
                DeckActionType.SystemLockScreen => "LOCK",
                DeckActionType.SystemScreensaver => "SCREEN",
                DeckActionType.SystemVirtualDesktopNext => "VD NEXT",
                DeckActionType.SystemVirtualDesktopPrev => "VD PREV",
                DeckActionType.SystemVirtualDesktopCreate => "VD NEW",
                DeckActionType.SystemVirtualDesktopClose => "VD CLOSE",
                DeckActionType.SystemTaskView => "TASK VIEW",
                DeckActionType.SystemShowDesktop => IsActiveToggleState ? "RESTORE" : "DESKTOP",
                DeckActionType.SystemClipboardHistory => "CLIPBOARD",
                DeckActionType.ConditionalAction => "IF / ELSE",
                DeckActionType.GoToPage => !string.IsNullOrWhiteSpace(ActionParameter) ? $"{loc.GetString("Deck_Page", "STRONA")} {ActionParameter}" : loc.GetString("Deck_Page", "STRONA"),
                DeckActionType.GoToProfile => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter.ToUpperInvariant() : loc.GetString("Deck_Profile", "PROFIL"),
                DeckActionType.OpenFolder => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter.ToUpperInvariant() : loc.GetString("Deck_Folder", "FOLDER"),
                DeckActionType.GoBack => loc.GetString("Deck_Back", "WRÓĆ"),
                DeckActionType.GoHome => loc.GetString("Deck_Home", "GŁÓWNA"),
                DeckActionType.NextPage => loc.GetString("Deck_Next", "NASTĘPNA"),
                DeckActionType.PreviousPage => loc.GetString("Deck_Prev", "POPRZEDNIA"),
                DeckActionType.DisplayCpuUsage => "CPU %",
                DeckActionType.DisplayRamUsage => "RAM",
                DeckActionType.DisplayGpuUsage => "GPU %",
                DeckActionType.DisplayCpuTemp => "CPU °C",
                DeckActionType.DisplayGpuTemp => "GPU °C",
                DeckActionType.DisplayNetworkSpeed => loc.GetString("Deck_Net", "SIEĆ"),
                DeckActionType.DisplayDiskUsage => string.IsNullOrWhiteSpace(ActionParameter)
                    ? loc.GetString("Deck_Disk", "DYSK")
                    : $"{loc.GetString("Deck_Disk", "DYSK")} {ActionParameter.TrimEnd('\\', '/')}",
                DeckActionType.DisplayClock => "CLOCK",
                DeckActionType.DisplayDate => "DATE",
                DeckActionType.NowPlaying => "MUSIC",
                DeckActionType.DisplayFps => "FPS",
                DeckActionType.ObsToggleStream => loc.GetString("Deck_Stream", "STREAM"),
                DeckActionType.ObsStartStream => $"{loc.GetString("Deck_Stream", "STREAM")} ON",
                DeckActionType.ObsStopStream => $"{loc.GetString("Deck_Stream", "STREAM")} OFF",
                DeckActionType.ObsToggleRecord => loc.GetString("Deck_Record", "REC"),
                DeckActionType.ObsStartRecord => $"{loc.GetString("Deck_Record", "REC")} ON",
                DeckActionType.ObsStopRecord => $"{loc.GetString("Deck_Record", "REC")} OFF",
                DeckActionType.ObsToggleVirtualCam => "VCAM",
                DeckActionType.ObsSaveReplayBuffer => "REPLAY",
                DeckActionType.ObsSceneSwitch => !string.IsNullOrWhiteSpace(ObsSceneName) ? ObsSceneName : (!string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : loc.GetString("Deck_Scene", "SCENA")),
                DeckActionType.ObsStudioModeToggle => "STUDIO",
                DeckActionType.ObsTriggerTransition => loc.GetString("Deck_Transition", "PRZEJŚCIE"),
                DeckActionType.TwitchChatEmoteOnly => "EMOTE",
                DeckActionType.TwitchChatSlowMode => "SLOW",
                DeckActionType.TwitchChatSubOnly => "SUBS",
                DeckActionType.TwitchClearChat => "CLEAR",
                DeckActionType.TwitchSendMessage => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter) : "CHAT",
                DeckActionType.TwitchCreateClip => "CLIP",
                DeckActionType.TwitchCreateMarker => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter) : "MARKER",
                DeckActionType.TwitchStartRaid => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter.ToUpperInvariant()) : "RAID",
                DeckActionType.TwitchCancelRaid => "UNRAID",
                DeckActionType.TwitchRunCommercial => !string.IsNullOrWhiteSpace(ActionParameter) ? $"{ActionParameter}S" : "ADS",
                DeckActionType.KickChatEmoteOnly => "EMOTE",
                DeckActionType.KickChatSlowMode => "SLOW",
                DeckActionType.KickChatSubOnly => "SUBS",
                DeckActionType.KickClearChat => "CLEAR",
                DeckActionType.KickSendMessage => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter) : "CHAT",
                DeckActionType.KickCreateClip => "CLIP",
                DeckActionType.SpotifyToggleShuffle => IsActiveToggleState ? "SHUF ON" : "SHUF OFF",
                DeckActionType.SpotifyToggleRepeat => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : (IsActiveToggleState ? "REPEAT" : "REP OFF"),
                DeckActionType.SpotifyLikeTrack => IsActiveToggleState ? "LIKED" : "LIKE",
                DeckActionType.SpotifyAddToQueue => "QUEUE",
                DeckActionType.SpotifyPlayUri => "PLAY",
                DeckActionType.VlcPlayPause => IsActiveToggleState ? "PAUSE" : "PLAY",
                DeckActionType.VlcStop => "STOP",
                DeckActionType.VlcNext => "NEXT",
                DeckActionType.VlcPrevious => "PREV",
                DeckActionType.VlcFullscreenToggle => IsActiveToggleState ? "WINDOW" : "FULL",
                DeckActionType.VoicemodVoiceSelect => GetVoicemodDisplayTitle(SelectedVoicemodVoice?.Name ?? ActionParameter, "VOICE"),
                DeckActionType.VoicemodVoiceToggle => IsActiveToggleState ? "VC ON" : "VC OFF",
                DeckActionType.VoicemodMuteVoice => IsActiveToggleState ? "VM MUTED" : "VM MIC",
                DeckActionType.VoicemodHearMyVoiceToggle => IsActiveToggleState ? "HEAR ON" : "HEAR OFF",
                DeckActionType.VoicemodBackgroundToggle => IsActiveToggleState ? "BG FX ON" : "BG FX OFF",
                DeckActionType.VoicemodPlaySound => GetVoicemodDisplayTitle(SelectedVoicemodSound?.Name ?? ActionParameter, "SOUND"),
                DeckActionType.VoicemodStopAllSounds => "STOP SOUND",
                DeckActionType.JetKvmPowerShort => IsActiveToggleState ? "SRV ON" : "SRV OFF",
                DeckActionType.JetKvmPowerLong => "PWR CUT",
                DeckActionType.JetKvmReset => "RESET",
                DeckActionType.JetKvmPowerStatus => IsActiveToggleState ? "SRV ON" : "SRV OFF",
                DeckActionType.YouTubeLiveViewers => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter.ToUpperInvariant()) : "YT LIVE",
                DeckActionType.StreamlabsSceneSwitch => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : "SL SCENE",
                DeckActionType.DiscordMuteToggle => "DC MUTE",
                DeckActionType.DiscordDeafenToggle => "DC DEAF",
                DeckActionType.DiscordCameraToggle => "DC CAM",
                DeckActionType.DiscordScreenShareToggle => "DC STREAM",
                DeckActionType.DiscordPushToTalk => "DC PTT",
                DeckActionType.DiscordJoinVoiceChannel => !string.IsNullOrWhiteSpace(ActionParameter) ? (ActionParameter.Length > 8 ? ActionParameter.Substring(0, 8) + ".." : ActionParameter.ToUpperInvariant()) : "DC VOICE",
                DeckActionType.TeamsMuteToggle => "TEAMS MUTE",
                DeckActionType.TeamsCameraToggle => "TEAMS CAM",
                DeckActionType.TeamsRaiseHand => "TEAMS HAND",
                DeckActionType.TeamsLeaveMeeting => "TEAMS LEAVE",
                DeckActionType.RazerDpiCycle => !string.IsNullOrWhiteSpace(ActionParameter) ? $"{ActionParameter}\nDPI" : "DPI CYCLE",
                DeckActionType.RazerDpiUp => "DPI +",
                DeckActionType.RazerDpiDown => "DPI -",
                DeckActionType.RazerDpi400 => "400 DPI",
                DeckActionType.RazerDpi800 => "800 DPI",
                DeckActionType.RazerDpi1600 => "1600 DPI",
                DeckActionType.RazerDpi3200 => "3200 DPI",
                DeckActionType.RazerDpi6400 => "6400 DPI",
                DeckActionType.RazerProfileNext => $"{loc.GetString("Deck_Profile", "PROFIL")} +",
                DeckActionType.RazerProfilePrev => $"{loc.GetString("Deck_Profile", "PROFIL")} -",
                DeckActionType.RazerProfile1 => $"{loc.GetString("Deck_Profile", "PROFIL")} #1",
                DeckActionType.RazerProfile2 => $"{loc.GetString("Deck_Profile", "PROFIL")} #2",
                DeckActionType.RazerProfile3 => $"{loc.GetString("Deck_Profile", "PROFIL")} #3",
                DeckActionType.RazerProfile4 => $"{loc.GetString("Deck_Profile", "PROFIL")} #4",
                DeckActionType.RazerProfile5 => $"{loc.GetString("Deck_Profile", "PROFIL")} #5",
                DeckActionType.RazerPollingRateCycle => !string.IsNullOrWhiteSpace(ActionParameter) ? $"{ActionParameter}\nHz" : "HZ CYCLE",
                DeckActionType.RazerPollingRateUp => "HZ +",
                DeckActionType.RazerPollingRateDown => "HZ -",
                DeckActionType.RazerPollingRate125 => "125 Hz",
                DeckActionType.RazerPollingRate500 => "500 Hz",
                DeckActionType.RazerPollingRate1000 => "1000 Hz",
                DeckActionType.RazerPollingRate2000 => "2000 Hz",
                DeckActionType.RazerPollingRate4000 => "4000 Hz",
                DeckActionType.RazerPollingRate8000 => "8000 Hz",
                DeckActionType.WootingProfileCycle => "WOOT CYCLE",
                DeckActionType.WootingProfileNext => "WOOT PROF+",
                DeckActionType.WootingProfilePrev => "WOOT PROF-",
                DeckActionType.WootingProfileDigital => "WOOT DIGI",
                DeckActionType.WootingProfile1 => "WOOT #1",
                DeckActionType.WootingProfile2 => "WOOT #2",
                DeckActionType.WootingProfile3 => "WOOT #3",
                DeckActionType.MacroPlay => MacroDisplayTitle(),
                DeckActionType.HomeAssistantToggle => !string.IsNullOrWhiteSpace(ActionParameter) ? GetHomeAssistantDisplayTitle(ActionParameter) : "HA TOGGLE",
                DeckActionType.HomeAssistantTurnOn => !string.IsNullOrWhiteSpace(ActionParameter) ? GetHomeAssistantDisplayTitle(ActionParameter) : "HA ON",
                DeckActionType.HomeAssistantTurnOff => !string.IsNullOrWhiteSpace(ActionParameter) ? GetHomeAssistantDisplayTitle(ActionParameter) : "HA OFF",
                DeckActionType.HomeAssistantCallService => !string.IsNullOrWhiteSpace(ActionParameter) ? GetHomeAssistantDisplayTitle(ActionParameter) : "HA SERVICE",
                _ => AssignedAction.ToString()
            };
    }

    private static string GetHomeAssistantDisplayTitle(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return "HOME ASSISTANT";
        var parts = entityId.Split('.');
        string name = parts.Length > 1 ? parts[1] : entityId;
        name = name.Replace('_', ' ');
        return name.Length > 10 ? name.Substring(0, 10).ToUpperInvariant() : name.ToUpperInvariant();
    }

    private static string GetVoicemodDisplayTitle(string? param, string fallback)
    {
        if (string.IsNullOrWhiteSpace(param)) return fallback;
        return param.Length > 8 ? param.Substring(0, 8) + ".." : param.ToUpperInvariant();
    }

    public string DisplaySubtitle
    {
        get
        {
            if (IsMonitoringAction && !string.IsNullOrWhiteSpace(_liveValueText)) return _liveValueText;
            if (IsActiveToggleState && ActiveCustomSubtitle != null) return ActiveCustomSubtitle;
            if (CustomSubtitle != null) return CustomSubtitle;
            if (!string.IsNullOrWhiteSpace(_liveValueText)) return _liveValueText;
            return GetOriginalActionSubtitle();
        }
    }

    public string GetOriginalActionSubtitle()
    {
        var loc = LocalizationService.Instance;
        return AssignedAction switch
        {
                DeckActionType.None => "",
                DeckActionType.MediaPlayPause => IsActiveToggleState ? loc.GetString("Sub_Paused", "● PAUZA") : loc.GetString("Sub_Playing", "ODTWARZANIE"),
                DeckActionType.MediaNext => loc.GetString("Sub_NextTrack", "Następny utwór"),
                DeckActionType.MediaPrevious => loc.GetString("Sub_PrevTrack", "Poprzedni utwór"),
                DeckActionType.MediaStop => loc.GetString("Sub_Stop", "Zatrzymaj"),
                DeckActionType.MasterMute => IsActiveToggleState ? loc.GetString("Sub_Muted", "● WYCISZONY") : loc.GetString("Sub_ToggleMute", "Głośność"),
                DeckActionType.MicMute => IsActiveToggleState ? loc.GetString("Sub_Muted", "● WYCISZONY") : loc.GetString("Sub_ToggleMic", "Mikrofon"),
                DeckActionType.AudioOutputSwitch => AudioSwitchSubtitle(),
                DeckActionType.VolumeUp => loc.GetString("Sub_VolUp", "Głośniej (+2%)"),
                DeckActionType.VolumeDown => loc.GetString("Sub_VolDown", "Ciszej (-2%)"),
                DeckActionType.Screenshot => "Win+Shift+S",
                DeckActionType.TaskManager => "Ctrl+Shift+Esc",
                DeckActionType.Calculator => "calc.exe",
                DeckActionType.Browser => loc.GetString("Sub_Browser", "Przeglądarka"),
                DeckActionType.OpenApplication => loc.GetString("Sub_LaunchApp", "Uruchom program"),
                DeckActionType.OpenWebsite => loc.GetString("Sub_Website", "Strona WWW"),
                DeckActionType.OpenFile => loc.GetString("Sub_FileFolder", "Plik lub folder"),
                DeckActionType.CloseApplication => loc.GetString("Sub_CloseApp", "Zamknij program"),
                DeckActionType.MultiAction => loc.GetString("Sub_MultiAction", "Sekwencja akcji"),
                DeckActionType.Delay => loc.GetString("Sub_Delay", "Pauza / Opóźnienie"),
                DeckActionType.TextPaste => loc.GetString("Sub_TextPaste", "Wklej tekst"),
                DeckActionType.Hotkey => loc.GetString("Sub_Hotkey", "Skrót klawiszy"),
                DeckActionType.HotkeySwitch => IsActiveToggleState ? loc.GetString("Sub_HotkeyState2", "Skrót #2") : loc.GetString("Sub_HotkeyState1", "Skrót #1"),
                DeckActionType.SystemSleep => loc.GetString("Sub_Sleep", "Uśpij komputer"),
                DeckActionType.SystemShutdown => loc.GetString("Sub_Shutdown", "Wyłącz komputer"),
                DeckActionType.SystemRestart => loc.GetString("Sub_Restart", "Restart komputera"),
                DeckActionType.SystemLockScreen => "Win + L",
                DeckActionType.SystemScreensaver => loc.GetString("Sub_Screensaver", "Wygaszacz"),
                DeckActionType.SystemVirtualDesktopNext => loc.GetString("Sub_VirtualDesktopNext", "Następny pulpit"),
                DeckActionType.SystemVirtualDesktopPrev => loc.GetString("Sub_VirtualDesktopPrev", "Poprzedni pulpit"),
                DeckActionType.SystemVirtualDesktopCreate => loc.GetString("Sub_VirtualDesktopCreate", "Nowy pulpit"),
                DeckActionType.SystemVirtualDesktopClose => loc.GetString("Sub_VirtualDesktopClose", "Zamknij pulpit"),
                DeckActionType.SystemTaskView => loc.GetString("Sub_TaskView", "Widok zadań"),
                DeckActionType.SystemShowDesktop => IsActiveToggleState ? loc.GetString("Sub_RestoreWindows", "Przywróć okna") : loc.GetString("Sub_ShowDesktop", "Pokaż pulpit"),
                DeckActionType.SystemClipboardHistory => loc.GetString("Sub_ClipboardHistory", "Historia schowka"),
                DeckActionType.ConditionalAction => loc.GetString("Sub_ConditionalAction", "Akcja warunkowa"),
                DeckActionType.GoToPage => loc.GetString("Sub_GoToPage", "Przejdź do strony"),
                DeckActionType.GoToProfile => loc.GetString("Sub_GoToProfile", "Zmień profil"),
                DeckActionType.OpenFolder => loc.GetString("Sub_OpenFolder", "Otwórz folder"),
                DeckActionType.GoBack => loc.GetString("Sub_GoBack", "Wróć poziom wyżej"),
                DeckActionType.GoHome => loc.GetString("Sub_GoHome", "Strona główna"),
                DeckActionType.NextPage => loc.GetString("Sub_NextPage", "Następna strona"),
                DeckActionType.PreviousPage => loc.GetString("Sub_PrevPage", "Poprzednia strona"),
                DeckActionType.DisplayCpuUsage => loc.GetString("Sub_CpuUsage", "Użycie CPU"),
                DeckActionType.DisplayRamUsage => loc.GetString("Sub_RamUsage", "Użycie RAM"),
                DeckActionType.DisplayGpuUsage => loc.GetString("Sub_GpuUsage", "Użycie GPU"),
                DeckActionType.DisplayCpuTemp => loc.GetString("Sub_CpuTemp", "Temperatura CPU"),
                DeckActionType.DisplayGpuTemp => loc.GetString("Sub_GpuTemp", "Temperatura GPU"),
                DeckActionType.DisplayNetworkSpeed => loc.GetString("Sub_NetSpeed", "Prędkość sieci"),
                DeckActionType.DisplayDiskUsage => loc.GetString("Sub_DiskUsage", "Zajętość dysku"),
                DeckActionType.DisplayClock => loc.GetString("Sub_Clock", "Czas systemowy"),
                DeckActionType.DisplayDate => loc.GetString("Sub_Date", "Data kalendarza"),
                DeckActionType.NowPlaying => loc.GetString("Sub_NowPlaying", "Teraz odtwarzane"),
                DeckActionType.DisplayFps => loc.GetString("Sub_Fps", "Licznik FPS"),
                DeckActionType.ObsToggleStream => IsActiveToggleState ? loc.GetString("Sub_ObsStreamingActive", "● NA ŻYWO") : loc.GetString("Sub_ObsToggleStream", "Przełącz stream"),
                DeckActionType.ObsStartStream => loc.GetString("Sub_ObsStartStream", "Włącz stream"),
                DeckActionType.ObsStopStream => loc.GetString("Sub_ObsStopStream", "Wyłącz stream"),
                DeckActionType.ObsToggleRecord => IsActiveToggleState ? loc.GetString("Sub_ObsRecordingActive", "● NAGRYWANIE") : loc.GetString("Sub_ObsToggleRecord", "Przełącz nagrywanie"),
                DeckActionType.ObsStartRecord => loc.GetString("Sub_ObsStartRecord", "Włącz nagrywanie"),
                DeckActionType.ObsStopRecord => loc.GetString("Sub_ObsStopRecord", "Wyłącz nagrywanie"),
                DeckActionType.ObsToggleVirtualCam => IsActiveToggleState ? loc.GetString("Sub_ObsVirtualCamActive", "● VCAM WŁ") : loc.GetString("Sub_ObsVirtualCam", "Kamera wirtualna"),
                DeckActionType.ObsSaveReplayBuffer => loc.GetString("Sub_ObsReplay", "Zapisz powtórkę"),
                DeckActionType.ObsSceneSwitch => loc.GetString("Sub_ObsScene", "Scena OBS"),
                DeckActionType.ObsStudioModeToggle => IsActiveToggleState ? loc.GetString("Sub_ObsStudioActive", "● STUDIO WŁ") : loc.GetString("Sub_ObsStudio", "Tryb studyjny"),
                DeckActionType.ObsTriggerTransition => loc.GetString("Sub_ObsTransition", "Wyzwalacz przejścia"),
                DeckActionType.TwitchChatEmoteOnly => IsActiveToggleState ? loc.GetString("Sub_TwitchEmoteActive", "● TYLKO EMOTKI") : loc.GetString("Sub_TwitchEmote", "Tylko emotki"),
                DeckActionType.TwitchChatSlowMode => IsActiveToggleState ? loc.GetString("Sub_TwitchSlowActive", "● TRYB POWOLNY") : loc.GetString("Sub_TwitchSlow", "Tryb powolny"),
                DeckActionType.TwitchChatSubOnly => IsActiveToggleState ? loc.GetString("Sub_TwitchSubActive", "● DLA SUBÓW") : loc.GetString("Sub_TwitchSub", "Dla subów"),
                DeckActionType.TwitchClearChat => loc.GetString("Sub_TwitchClear", "Wyczyść czat"),
                DeckActionType.TwitchSendMessage => loc.GetString("Sub_TwitchSend", "Wyślij wiadomość"),
                DeckActionType.TwitchCreateClip => loc.GetString("Sub_TwitchClip", "Utwórz klip"),
                DeckActionType.TwitchCreateMarker => loc.GetString("Sub_TwitchMarker", "Znacznik streamu"),
                DeckActionType.TwitchStartRaid => loc.GetString("Sub_TwitchRaid", "Rozpocznij Raid"),
                DeckActionType.TwitchCancelRaid => loc.GetString("Sub_TwitchCancelRaid", "Anuluj Raid"),
                DeckActionType.TwitchRunCommercial => loc.GetString("Sub_TwitchAd", "Przerwa reklamowa"),
                DeckActionType.KickChatEmoteOnly => IsActiveToggleState ? loc.GetString("Sub_KickEmoteActive", "● TYLKO EMOTKI") : loc.GetString("Sub_KickEmote", "Tylko emotki"),
                DeckActionType.KickChatSlowMode => IsActiveToggleState ? loc.GetString("Sub_KickSlowActive", "● TRYB POWOLNY") : loc.GetString("Sub_KickSlow", "Tryb powolny"),
                DeckActionType.KickChatSubOnly => IsActiveToggleState ? loc.GetString("Sub_KickSubActive", "● DLA SUBÓW") : loc.GetString("Sub_KickSub", "Dla subów"),
                DeckActionType.KickClearChat => loc.GetString("Sub_KickClear", "Wyczyść czat"),
                DeckActionType.KickSendMessage => loc.GetString("Sub_KickSend", "Wyślij wiadomość"),
                DeckActionType.KickCreateClip => loc.GetString("Sub_KickClip", "Utwórz klip"),
                DeckActionType.SpotifyToggleShuffle => IsActiveToggleState ? loc.GetString("Sub_SpotifyShuffleOn", "● Losowo") : loc.GetString("Sub_SpotifyShuffleOff", "Wyłączone"),
                DeckActionType.SpotifyToggleRepeat => IsActiveToggleState ? loc.GetString("Sub_SpotifyRepeatOn", "● Powtarzanie") : loc.GetString("Sub_SpotifyRepeatOff", "Wyłączone"),
                DeckActionType.SpotifyLikeTrack => IsActiveToggleState ? loc.GetString("Sub_SpotifyLiked", "💚 W ulubionych") : loc.GetString("Sub_SpotifyLike", "Polub utwór"),
                DeckActionType.SpotifyAddToQueue => loc.GetString("Sub_SpotifyAddToQueue", "Dodaj do kolejki"),
                DeckActionType.SpotifyPlayUri => loc.GetString("Sub_SpotifyPlayUri", "Uruchom playlistę"),
                DeckActionType.VlcPlayPause => IsActiveToggleState ? loc.GetString("Sub_VlcPlaying", "● Odtwarzanie") : loc.GetString("Sub_VlcPaused", "Wstrzymano"),
                DeckActionType.VlcStop => loc.GetString("Sub_VlcStop", "Zatrzymaj VLC"),
                DeckActionType.VlcNext => loc.GetString("Sub_VlcNext", "Następny utwór"),
                DeckActionType.VlcPrevious => loc.GetString("Sub_VlcPrev", "Poprzedni utwór"),
                DeckActionType.VlcFullscreenToggle => IsActiveToggleState ? loc.GetString("Sub_VlcFullscreenOn", "● Pełny ekran") : loc.GetString("Sub_VlcFullscreenOff", "Tryb okna"),
                DeckActionType.VoicemodVoiceSelect => !string.IsNullOrWhiteSpace(ActionParameter) ? (SelectedVoicemodVoice?.Name ?? ActionParameter) : loc.GetString("Sub_VoicemodSelect", "Wybierz głos"),
                DeckActionType.VoicemodVoiceToggle => IsActiveToggleState ? loc.GetString("Sub_VoicemodVcOn", "● Modulacja WŁ") : loc.GetString("Sub_VoicemodVcOff", "Modulacja WYŁ"),
                DeckActionType.VoicemodMuteVoice => IsActiveToggleState ? loc.GetString("Sub_VoicemodMuted", "● MIKROFON WYCISZONY") : loc.GetString("Sub_VoicemodUnmuted", "Mikrofon aktywny"),
                DeckActionType.VoicemodHearMyVoiceToggle => IsActiveToggleState ? loc.GetString("Sub_VoicemodHearOn", "● Odsłuch WŁ") : loc.GetString("Sub_VoicemodHearOff", "Odsłuch WYŁ"),
                DeckActionType.VoicemodBackgroundToggle => IsActiveToggleState ? loc.GetString("Sub_VoicemodBgOn", "● DŹWIĘK TŁA WŁ") : loc.GetString("Sub_VoicemodBgOff", "DŹWIĘK TŁA WYŁ"),
                DeckActionType.VoicemodPlaySound => !string.IsNullOrWhiteSpace(ActionParameter) ? (SelectedVoicemodSound?.Name ?? ActionParameter) : loc.GetString("Sub_VoicemodPlaySound", "Odtwórz dźwięk"),
                DeckActionType.VoicemodStopAllSounds => loc.GetString("Sub_VoicemodStopAllSounds", "Zatrzymaj dźwięki"),
                DeckActionType.JetKvmPowerShort => IsActiveToggleState ? loc.GetString("Sub_JetKvmPowerOn", "● SERWER WŁ") : loc.GetString("Sub_JetKvmPowerShort", "Włącz / ACPI"),
                DeckActionType.JetKvmPowerLong => loc.GetString("Sub_JetKvmPowerLong", "Wymuszone (5s)"),
                DeckActionType.JetKvmReset => loc.GetString("Sub_JetKvmReset", "Reset sprzętowy"),
                DeckActionType.JetKvmPowerStatus => IsActiveToggleState ? loc.GetString("Sub_JetKvmPowerOn", "● SERWER WŁ") : loc.GetString("Sub_JetKvmPowerOff", "● SERWER WYŁ"),
                DeckActionType.YouTubeLiveViewers => loc.GetString("Sub_YtViewers", "Widzowie YouTube"),
                DeckActionType.StreamlabsSceneSwitch => IsActiveToggleState ? loc.GetString("Sub_Active", "● AKTYWNA") : "Streamlabs",
                DeckActionType.DiscordMuteToggle => IsActiveToggleState ? loc.GetString("Sub_Muted", "● WYCISZONY") : "Discord Mic",
                DeckActionType.DiscordDeafenToggle => IsActiveToggleState ? loc.GetString("Sub_Deafened", "● GŁUCHY") : "Discord Audio",
                DeckActionType.DiscordCameraToggle => IsActiveToggleState ? loc.GetString("Sub_CamOn", "● KAMERA WŁ") : "Discord Video",
                DeckActionType.DiscordScreenShareToggle => IsActiveToggleState ? loc.GetString("Sub_GoLive", "● EKRAN WŁ") : "Discord Screen",
                DeckActionType.DiscordPushToTalk => IsActiveToggleState ? loc.GetString("Sub_Talk", "● NADAWANIE") : "Push-To-Talk",
                DeckActionType.DiscordJoinVoiceChannel => IsActiveToggleState ? loc.GetString("Sub_Connected", "● POŁĄCZONO") : loc.GetString("Sub_JoinVoice", "Dołącz do kanału"),
                DeckActionType.TeamsMuteToggle => IsActiveToggleState ? loc.GetString("Sub_Muted", "● WYCISZONY") : "Ctrl+Shift+M",
                DeckActionType.TeamsCameraToggle => IsActiveToggleState ? loc.GetString("Sub_CamOn", "● KAMERA WŁ") : "Ctrl+Shift+O",
                DeckActionType.TeamsRaiseHand => IsActiveToggleState ? loc.GetString("Sub_HandUp", "● RĘKA W GÓRZE") : "Ctrl+Shift+K",
                DeckActionType.TeamsLeaveMeeting => "Ctrl+Shift+H",
                DeckActionType.RazerDpiCycle => loc.GetString("Sub_RazerSensor", "Sensor Razer"),
                DeckActionType.RazerDpiUp => loc.GetString("Sub_RazerDpiUp", "Wyższy etap DPI"),
                DeckActionType.RazerDpiDown => loc.GetString("Sub_RazerDpiDown", "Niższy etap DPI"),
                DeckActionType.RazerProfileNext or DeckActionType.RazerProfilePrev => loc.GetString("Sub_MouseMemory", "Pamięć myszy"),
                DeckActionType.RazerPollingRateCycle => loc.GetString("Sub_PollingRate", "Częstotliwość"),
                DeckActionType.WootingProfileCycle or DeckActionType.WootingProfileNext or DeckActionType.WootingProfilePrev => loc.GetString("Sub_WootingKeyboard", "Klawiatura"),
                DeckActionType.WootingProfileDigital => loc.GetString("Sub_DigitalProfile", "Profil Cyfrowy"),
                DeckActionType.WootingProfile1 => loc.GetString("Sub_AnalogProfile1", "Profil Analogowy 1"),
                DeckActionType.WootingProfile2 => loc.GetString("Sub_AnalogProfile2", "Profil Analogowy 2"),
                DeckActionType.WootingProfile3 => loc.GetString("Sub_AnalogProfile3", "Profil Analogowy 3"),
                DeckActionType.MacroPlay => IsMacroRunning ? loc.GetString("Macro_Running", "● URUCHOMIONE") : loc.GetString("Sub_MacroPlay", "Makro"),
                DeckActionType.HomeAssistantToggle => !string.IsNullOrWhiteSpace(ActionParameter)
                    ? (IsActiveToggleState ? loc.GetString("Sub_HaOn", "● WŁĄCZONE") : loc.GetString("Sub_HaOff", "WYŁĄCZONE"))
                    : loc.GetString("Action_HomeAssistantToggle", "Przełącz stan"),
                DeckActionType.HomeAssistantTurnOn => loc.GetString("Action_HomeAssistantTurnOn", "Włącz urządzenie"),
                DeckActionType.HomeAssistantTurnOff => loc.GetString("Action_HomeAssistantTurnOff", "Wyłącz urządzenie"),
                DeckActionType.HomeAssistantCallService => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : loc.GetString("Action_HomeAssistantCallService", "Wywołaj usługę"),
                _ => ""
            };
    }

    public string ActionIcon => AssignedAction switch
    {
        DeckActionType.None => "⬜",
        DeckActionType.MediaPlayPause => IsActiveToggleState ? "⏸️" : "▶️",
        DeckActionType.MediaNext => "⏭️",
        DeckActionType.MediaPrevious => "⏮️",
        DeckActionType.MediaStop => "⏹️",
        DeckActionType.MasterMute => IsActiveToggleState ? "🔇" : "🔊",
        DeckActionType.MicMute => IsActiveToggleState ? "🔇" : "🎙️",
        DeckActionType.AudioOutputSwitch => "🎧",
        DeckActionType.LiveValue => "📈",
        DeckActionType.TimerCountdown => "⏱",
        DeckActionType.TimerPomodoro => "🍅",
        DeckActionType.TimerStopwatch => "⏲",
        DeckActionType.TimerStartPause => "⏯",
        DeckActionType.TimerReset => "⏮",
        DeckActionType.TimerAddMinute => "➕",
        DeckActionType.TimerSplit => "🏁",
        DeckActionType.SoundPlay => "🔊",
        DeckActionType.SoundStopAll => "🔇",
        DeckActionType.DeckBrightnessUp => "🔆",
        DeckActionType.DeckBrightnessDown => "🔅",
        DeckActionType.DeckBrightnessCycle => "🌓",
        DeckActionType.MediaSeekForward => "⏩",
        DeckActionType.MediaSeekBackward => "⏪",
        DeckActionType.TypeText => "⌨️",
        DeckActionType.ForegroundAppVolumeUp => "🔊",
        DeckActionType.ForegroundAppVolumeDown => "🔉",
        DeckActionType.ForegroundAppMute => "🔇",
        DeckActionType.HttpRequest => "🌐",
        DeckActionType.VolumeUp => "🔊",
        DeckActionType.VolumeDown => "🔉",
        DeckActionType.Screenshot => "📸",
        DeckActionType.TaskManager => "📊",
        DeckActionType.Calculator => "🧮",
        DeckActionType.Browser => "🌐",
        DeckActionType.OpenApplication => "🚀",
        DeckActionType.OpenWebsite => "🌐",
        DeckActionType.OpenFile => "📂",
        DeckActionType.CloseApplication => "❌",
        DeckActionType.MultiAction => "⚡",
        DeckActionType.Delay => "⏳",
        DeckActionType.TextPaste => "📋",
        DeckActionType.Hotkey => "⌨️",
        DeckActionType.HotkeySwitch => IsActiveToggleState ? "2️⃣" : "1️⃣",
        DeckActionType.SystemSleep => "💤",
        DeckActionType.SystemShutdown => "⏻",
        DeckActionType.SystemRestart => "🔄",
        DeckActionType.SystemLockScreen => "🔒",
        DeckActionType.SystemScreensaver => "🖥️",
        DeckActionType.SystemVirtualDesktopNext => "🗔",
        DeckActionType.SystemVirtualDesktopPrev => "🖥️",
        DeckActionType.SystemVirtualDesktopCreate => "➕",
        DeckActionType.SystemVirtualDesktopClose => "✖️",
        DeckActionType.SystemTaskView => "🗂️",
        DeckActionType.SystemShowDesktop => IsActiveToggleState ? "🗗" : "🖥️",
        DeckActionType.SystemClipboardHistory => "📋",
        DeckActionType.ConditionalAction => "🔀",
        DeckActionType.GoToPage => "📄",
        DeckActionType.GoToProfile => "🔀",
        DeckActionType.OpenFolder => "📁",
        DeckActionType.GoBack => "⬅️",
        DeckActionType.GoHome => "🏠",
        DeckActionType.NextPage => "▶",
        DeckActionType.PreviousPage => "◀",
        DeckActionType.DisplayCpuUsage => "📊",
        DeckActionType.DisplayRamUsage => "🧠",
        DeckActionType.DisplayGpuUsage => "🎮",
        DeckActionType.DisplayCpuTemp => "🌡️",
        DeckActionType.DisplayGpuTemp => "🌡️",
        DeckActionType.DisplayNetworkSpeed => "📡",
        DeckActionType.DisplayDiskUsage => "💾",
        DeckActionType.DisplayClock => "🕐",
        DeckActionType.DisplayDate => "📅",
        DeckActionType.NowPlaying => "🎵",
        DeckActionType.DisplayFps => "🎯",
        DeckActionType.ObsToggleStream or DeckActionType.ObsStartStream or DeckActionType.ObsStopStream => IsActiveToggleState ? "🔴" : "📡",
        DeckActionType.ObsToggleRecord or DeckActionType.ObsStartRecord or DeckActionType.ObsStopRecord => IsActiveToggleState ? "⏹️" : "🔴",
        DeckActionType.ObsToggleVirtualCam => IsActiveToggleState ? "🟢" : "📹",
        DeckActionType.ObsSaveReplayBuffer => "💾",
        DeckActionType.ObsSceneSwitch => "🎬",
        DeckActionType.ObsStudioModeToggle => IsActiveToggleState ? "🎬" : "🎭",
        DeckActionType.ObsTriggerTransition => "🔀",
        DeckActionType.TwitchChatEmoteOnly => IsActiveToggleState ? "🔒" : "😀",
        DeckActionType.TwitchChatSlowMode => IsActiveToggleState ? "⏱️" : "🐢",
        DeckActionType.TwitchChatSubOnly => IsActiveToggleState ? "🌟" : "⭐",
        DeckActionType.TwitchClearChat => "🧹",
        DeckActionType.TwitchSendMessage => "💬",
        DeckActionType.TwitchCreateClip => "🎬",
        DeckActionType.TwitchCreateMarker => "📍",
        DeckActionType.TwitchStartRaid => "🚀",
        DeckActionType.TwitchCancelRaid => "🛑",
        DeckActionType.TwitchRunCommercial => "📺",
        DeckActionType.KickChatEmoteOnly => IsActiveToggleState ? "🔒" : "😀",
        DeckActionType.KickChatSlowMode => IsActiveToggleState ? "⏱️" : "🐢",
        DeckActionType.KickChatSubOnly => IsActiveToggleState ? "🌟" : "⭐",
        DeckActionType.KickClearChat => "🧹",
        DeckActionType.KickSendMessage => "💬",
        DeckActionType.KickCreateClip => "🎬",
        DeckActionType.SpotifyToggleShuffle => "🔀",
        DeckActionType.SpotifyToggleRepeat => "🔁",
        DeckActionType.SpotifyLikeTrack => IsActiveToggleState ? "💚" : "🤍",
        DeckActionType.SpotifyAddToQueue => "➕",
        DeckActionType.SpotifyPlayUri => "▶️",
        DeckActionType.VlcPlayPause => IsActiveToggleState ? "⏸️" : "▶️",
        DeckActionType.VlcStop => "⏹️",
        DeckActionType.VlcNext => "⏭️",
        DeckActionType.VlcPrevious => "⏮️",
        DeckActionType.VlcFullscreenToggle => IsActiveToggleState ? "🗗" : "⛶",
        DeckActionType.VoicemodVoiceSelect => "🤖",
        DeckActionType.VoicemodVoiceToggle => IsActiveToggleState ? "🎙️" : "🗣️",
        DeckActionType.VoicemodMuteVoice => IsActiveToggleState ? "🔇" : "🎙️",
        DeckActionType.VoicemodHearMyVoiceToggle => IsActiveToggleState ? "🎧" : "🔕",
        DeckActionType.VoicemodBackgroundToggle => IsActiveToggleState ? "🎧" : "🔇",
        DeckActionType.VoicemodPlaySound => "🔊",
        DeckActionType.VoicemodStopAllSounds => "⏹️",
        DeckActionType.JetKvmPowerShort => "🔌",
        DeckActionType.JetKvmPowerLong => "⏻",
        DeckActionType.JetKvmReset => "🔄",
        DeckActionType.JetKvmPowerStatus => IsActiveToggleState ? "💡" : "🔌",
        DeckActionType.YouTubeLiveViewers => "▶️",
        DeckActionType.StreamlabsSceneSwitch => "🎥",
        DeckActionType.DiscordMuteToggle => IsActiveToggleState ? "🔇" : "🎙️",
        DeckActionType.DiscordDeafenToggle => IsActiveToggleState ? "🔕" : "🎧",
        DeckActionType.DiscordCameraToggle => IsActiveToggleState ? "🚫" : "📷",
        DeckActionType.DiscordScreenShareToggle => IsActiveToggleState ? "📡" : "🖥️",
        DeckActionType.DiscordPushToTalk => IsActiveToggleState ? "🗣️" : "🎙️",
        DeckActionType.DiscordJoinVoiceChannel => "🔊",
        DeckActionType.TeamsMuteToggle => IsActiveToggleState ? "🔇" : "🎙️",
        DeckActionType.TeamsCameraToggle => IsActiveToggleState ? "🚫" : "📷",
        DeckActionType.TeamsRaiseHand => IsActiveToggleState ? "🙋" : "✋",
        DeckActionType.TeamsLeaveMeeting => "📞",
        DeckActionType.RazerDpiCycle or DeckActionType.RazerDpiUp or DeckActionType.RazerDpiDown or DeckActionType.RazerDpi400 or DeckActionType.RazerDpi800 or DeckActionType.RazerDpi1600 or DeckActionType.RazerDpi3200 or DeckActionType.RazerDpi6400 => "🎯",
        DeckActionType.RazerProfileNext or DeckActionType.RazerProfilePrev or DeckActionType.RazerProfile1 or DeckActionType.RazerProfile2 or DeckActionType.RazerProfile3 or DeckActionType.RazerProfile4 or DeckActionType.RazerProfile5 => "🐍",
        DeckActionType.RazerPollingRateCycle or DeckActionType.RazerPollingRateUp or DeckActionType.RazerPollingRateDown or DeckActionType.RazerPollingRate125 or DeckActionType.RazerPollingRate500 or DeckActionType.RazerPollingRate1000 or DeckActionType.RazerPollingRate2000 or DeckActionType.RazerPollingRate4000 or DeckActionType.RazerPollingRate8000 => "⚡",
        DeckActionType.WootingProfileCycle or DeckActionType.WootingProfileNext or DeckActionType.WootingProfilePrev or DeckActionType.WootingProfileDigital or DeckActionType.WootingProfile1 or DeckActionType.WootingProfile2 or DeckActionType.WootingProfile3 => "⌨️",
        DeckActionType.MacroPlay => IsMacroRunning ? "⏹" : "🎬",
        DeckActionType.HomeAssistantToggle => IsActiveToggleState ? "💡" : "⚪",
        DeckActionType.HomeAssistantTurnOn => "💡",
        DeckActionType.HomeAssistantTurnOff => "🔌",
        DeckActionType.HomeAssistantCallService => "⚙️",
        DeckActionType.StreamDeckPluginAction => "🔌",
        _ => "⚡"
    };

    public string ActionDisplayName
    {
        get
        {
            if (AssignedAction == DeckActionType.StreamDeckPluginAction)
            {
                var host = GetService<IStreamDeckPluginHost>();
                var actionManifest = host?.GetActionManifest(StreamDeckPluginUuid, StreamDeckActionUuid);
                var plugin = host?.GetPlugin(StreamDeckPluginUuid);
                string actName = actionManifest?.Name ?? StreamDeckActionUuid;
                string plgName = plugin?.Name ?? StreamDeckPluginUuid;
                return string.IsNullOrEmpty(actName) ? "Stream Deck Plugin" : $"{plgName}: {actName}";
            }

            string baseName = LocalizationService.Instance.GetString($"Action_{AssignedAction}", AssignedAction.ToString());
            if (!string.IsNullOrWhiteSpace(ActionParameter))
            {
                return AssignedAction switch
                {
                    DeckActionType.OpenApplication or DeckActionType.OpenFile or DeckActionType.CloseApplication =>
                        $"{baseName}: {Path.GetFileName(ActionParameter)}",
                    DeckActionType.OpenWebsite or DeckActionType.Hotkey or DeckActionType.HotkeySwitch or
                    DeckActionType.MultiAction or DeckActionType.GoToPage or DeckActionType.GoToProfile or
                    DeckActionType.OpenFolder =>
                        $"{baseName}: {ActionParameter}",
                    DeckActionType.Delay =>
                        $"{baseName}: {ActionParameter} ms",
                    DeckActionType.RazerDpiCycle =>
                        $"{baseName} ({ActionParameter} DPI)",
                    DeckActionType.RazerPollingRateCycle =>
                        $"{baseName} ({ActionParameter} Hz)",
                    _ => $"{baseName}: {ActionParameter}"
                };
            }
            if (AssignedAction == DeckActionType.ObsSceneSwitch && !string.IsNullOrWhiteSpace(ObsSceneName))
            {
                return $"{baseName}: {ObsSceneName}";
            }
            return baseName;
        }
    }

    public string ActionCategory => AssignedAction switch
    {
        DeckActionType.StreamDeckPluginAction => "Stream Deck",
        DeckActionType.MacroPlay => LocalizationService.Instance.GetString("Cat_Macros", "Makra"),
        DeckActionType.None => LocalizationService.Instance.GetString("Action_None", "Puste"),
        DeckActionType.MediaPlayPause or DeckActionType.MediaNext or DeckActionType.MediaPrevious or DeckActionType.MediaStop => LocalizationService.Instance.GetString("Cat_Media", "Multimedia"),
        DeckActionType.MasterMute or DeckActionType.MicMute or DeckActionType.VolumeUp or DeckActionType.VolumeDown or DeckActionType.AudioOutputSwitch => LocalizationService.Instance.GetString("Cat_Audio", "Audio"),
        DeckActionType.GoToPage or DeckActionType.GoToProfile or DeckActionType.OpenFolder or DeckActionType.GoBack or DeckActionType.GoHome or DeckActionType.NextPage or DeckActionType.PreviousPage => LocalizationService.Instance.GetString("Cat_Nav", "Nawigacja"),
        DeckActionType.ObsToggleStream or DeckActionType.ObsStartStream or DeckActionType.ObsStopStream or
        DeckActionType.ObsToggleRecord or DeckActionType.ObsStartRecord or DeckActionType.ObsStopRecord or
        DeckActionType.ObsToggleVirtualCam or DeckActionType.ObsSaveReplayBuffer or DeckActionType.ObsSceneSwitch or
        DeckActionType.ObsStudioModeToggle or DeckActionType.ObsTriggerTransition or
        DeckActionType.TwitchChatEmoteOnly or DeckActionType.TwitchChatSlowMode or
        DeckActionType.TwitchChatSubOnly or DeckActionType.TwitchClearChat or
        DeckActionType.TwitchSendMessage or DeckActionType.TwitchCreateClip or
        DeckActionType.TwitchCreateMarker or DeckActionType.TwitchStartRaid or
        DeckActionType.TwitchCancelRaid or DeckActionType.TwitchRunCommercial or
        DeckActionType.KickChatEmoteOnly or DeckActionType.KickChatSlowMode or
        DeckActionType.KickChatSubOnly or DeckActionType.KickClearChat or
        DeckActionType.KickSendMessage or DeckActionType.KickCreateClip or
        DeckActionType.YouTubeLiveViewers or DeckActionType.StreamlabsSceneSwitch or
        DeckActionType.DiscordMuteToggle or DeckActionType.DiscordDeafenToggle or
        DeckActionType.DiscordCameraToggle or DeckActionType.DiscordScreenShareToggle or
        DeckActionType.DiscordPushToTalk or
        DeckActionType.DiscordJoinVoiceChannel or
        DeckActionType.TeamsMuteToggle or DeckActionType.TeamsCameraToggle or
        DeckActionType.TeamsRaiseHand or DeckActionType.TeamsLeaveMeeting => LocalizationService.Instance.GetString("Cat_Streaming", "Streaming"),
        DeckActionType.DisplayCpuUsage or DeckActionType.DisplayRamUsage or DeckActionType.DisplayGpuUsage or DeckActionType.DisplayCpuTemp or
        DeckActionType.DisplayGpuTemp or DeckActionType.DisplayNetworkSpeed or DeckActionType.DisplayDiskUsage or DeckActionType.DisplayClock or
        DeckActionType.DisplayDate or DeckActionType.NowPlaying or DeckActionType.DisplayFps => LocalizationService.Instance.GetString("Cat_Monitor", "Monitor"),
        DeckActionType.Screenshot or DeckActionType.TaskManager or DeckActionType.Calculator or DeckActionType.Browser or
        DeckActionType.OpenApplication or DeckActionType.OpenWebsite or DeckActionType.OpenFile or DeckActionType.CloseApplication or DeckActionType.TextPaste or
        DeckActionType.Hotkey or DeckActionType.HotkeySwitch or DeckActionType.MultiAction or DeckActionType.Delay or DeckActionType.SystemSleep or DeckActionType.SystemShutdown or
        DeckActionType.SystemRestart or DeckActionType.SystemLockScreen or DeckActionType.SystemScreensaver or
        DeckActionType.SystemVirtualDesktopNext or DeckActionType.SystemVirtualDesktopPrev or
        DeckActionType.SystemVirtualDesktopCreate or DeckActionType.SystemVirtualDesktopClose or
        DeckActionType.SystemTaskView or DeckActionType.SystemShowDesktop or
        DeckActionType.SystemClipboardHistory or DeckActionType.ConditionalAction => LocalizationService.Instance.GetString("Cat_System", "System"),
        _ when AssignedAction.ToString().StartsWith("JetKvm") => LocalizationService.Instance.GetString("Cat_JetKvm", "JetKVM"),
        _ when AssignedAction.ToString().StartsWith("Razer") => LocalizationService.Instance.GetString("Cat_Razer", "Razer"),
        _ when AssignedAction.ToString().StartsWith("Wooting") => LocalizationService.Instance.GetString("Cat_Wooting", "Wooting"),
        _ when AssignedAction.ToString().StartsWith("Spotify") => LocalizationService.Instance.GetString("Cat_Spotify", "Spotify"),
        _ when AssignedAction.ToString().StartsWith("Vlc") => LocalizationService.Instance.GetString("Cat_Vlc", "VLC"),
        _ when AssignedAction.ToString().StartsWith("Voicemod") => LocalizationService.Instance.GetString("Cat_Voicemod", "Voicemod"),
        _ when AssignedAction.ToString().StartsWith("HomeAssistant") => LocalizationService.Instance.GetString("Cat_SmartHome", "Smart Home"),
        _ => LocalizationService.Instance.GetString("Cat_All", "Inne")
    };

    public bool RequiresActionParameter => AssignedAction is
        DeckActionType.TypeText or DeckActionType.HttpRequest or
        DeckActionType.MediaSeekForward or DeckActionType.MediaSeekBackward or
        DeckActionType.ForegroundAppVolumeUp or DeckActionType.ForegroundAppVolumeDown or
        DeckActionType.OpenApplication or
        DeckActionType.OpenWebsite or
        DeckActionType.OpenFile or
        DeckActionType.CloseApplication or
        DeckActionType.TextPaste or
        DeckActionType.Hotkey or
        DeckActionType.HotkeySwitch or
        DeckActionType.MultiAction or
        DeckActionType.Delay or
        DeckActionType.GoToPage or
        DeckActionType.GoToProfile or
        DeckActionType.OpenFolder or
        DeckActionType.ObsSceneSwitch or
        DeckActionType.TwitchSendMessage or
        DeckActionType.TwitchCreateMarker or
        DeckActionType.TwitchStartRaid or
        DeckActionType.TwitchRunCommercial or
        DeckActionType.KickSendMessage or
        DeckActionType.YouTubeLiveViewers or
        DeckActionType.StreamlabsSceneSwitch or
        DeckActionType.DiscordJoinVoiceChannel or
        DeckActionType.SpotifyAddToQueue or
        DeckActionType.SpotifyPlayUri or
        DeckActionType.VoicemodVoiceSelect or
        DeckActionType.VoicemodPlaySound or
        DeckActionType.HomeAssistantToggle or
        DeckActionType.HomeAssistantTurnOn or
        DeckActionType.HomeAssistantTurnOff or
        DeckActionType.HomeAssistantCallService or
        DeckActionType.ConditionalAction;

    public bool IsObsSceneAction => AssignedAction == DeckActionType.ObsSceneSwitch;
    public bool IsNotObsSceneAction => !IsObsSceneAction;

    public bool IsFileBrowseSupported => AssignedAction is
        DeckActionType.OpenApplication or
        DeckActionType.OpenFile or
        DeckActionType.CloseApplication;

    public string ActionParameterLabel
    {
        get
        {
            var loc = LocalizationService.Instance;
            return AssignedAction switch
            {
                DeckActionType.OpenApplication => loc.GetString("ParamLabel_OpenApp", "Ścieżka do aplikacji (.exe):"),
                DeckActionType.OpenWebsite => loc.GetString("ParamLabel_OpenWebsite", "Adres URL strony internetowej:"),
                DeckActionType.OpenFile => loc.GetString("ParamLabel_OpenFile", "Ścieżka do pliku lub folderu:"),
                DeckActionType.CloseApplication => loc.GetString("ParamLabel_CloseApp", "Nazwa procesu / pliku (lub puste dla aktywnego okna):"),
                DeckActionType.TextPaste => loc.GetString("ParamLabel_TextPaste", "Treść tekstu do wklejenia:"),
                DeckActionType.Hotkey => loc.GetString("ParamLabel_Hotkey", "Skrót klawiszowy (np. Ctrl+Shift+S):"),
                DeckActionType.HotkeySwitch => loc.GetString("ParamLabel_HotkeySwitch", "Przełączane skróty (np. Ctrl+M|Ctrl+Shift+M):"),
                DeckActionType.MultiAction => loc.GetString("ParamLabel_MultiAction", "Kroki sekwencji (format: akcja|parametr;delay:ms):"),
                DeckActionType.Delay => loc.GetString("ParamLabel_Delay", "Czas opóźnienia w milisekundach (ms):"),
                DeckActionType.TypeText => loc.GetString("ParamLabel_TypeText", "Text to type (the clipboard is not used):"),
                DeckActionType.HttpRequest => loc.GetString("ParamLabel_Http", "[METHOD] URL [| body]:"),
                DeckActionType.MediaSeekForward or DeckActionType.MediaSeekBackward => loc.GetString("ParamLabel_Seek", "Player (empty = the program on top; e.g. vlc-short):"),
                DeckActionType.ForegroundAppVolumeUp or DeckActionType.ForegroundAppVolumeDown => loc.GetString("ParamLabel_AppVolStep", "Step in % (empty = 5):"),
                DeckActionType.GoToPage => loc.GetString("ParamLabel_GoToPage", "Numer strony (np. 1, 2, 3):"),
                DeckActionType.GoToProfile => loc.GetString("ParamLabel_GoToProfile", "Nazwa lub identyfikator profilu:"),
                DeckActionType.OpenFolder => loc.GetString("ParamLabel_OpenFolder", "Nazwa folderu (podmenu):"),
                DeckActionType.ObsSceneSwitch => loc.GetString("ParamLabel_ObsScene", "Nazwa sceny OBS (program):"),
                DeckActionType.TwitchSendMessage => loc.GetString("ParamLabel_TwitchMessage", "Treść wiadomości do wysłania na czat:"),
                DeckActionType.TwitchCreateMarker => loc.GetString("ParamLabel_TwitchMarker", "Opis znacznika transmisji (opcjonalny, max 140 znaków):"),
                DeckActionType.TwitchStartRaid => loc.GetString("ParamLabel_TwitchRaid", "Docelowy kanał Twitch do raidu:"),
                DeckActionType.TwitchRunCommercial => loc.GetString("ParamLabel_TwitchCommercial", "Długość reklamy w sekundach (30, 60, 90, 120, 150, 180):"),
                DeckActionType.KickSendMessage => loc.GetString("ParamLabel_KickMessage", "Treść wiadomości do wysłania na czat Kick:"),
                DeckActionType.YouTubeLiveViewers => loc.GetString("ParamLabel_YouTubeViewers", "ID kanału (UC...), @handle lub ID/URL transmisji YouTube (opcjonalnie):"),
                DeckActionType.StreamlabsSceneSwitch => loc.GetString("ParamLabel_StreamlabsScene", "Nazwa sceny w Streamlabs Desktop:"),
                DeckActionType.DiscordJoinVoiceChannel => loc.GetString("ParamLabel_DiscordVoice", "Link do kanału lub ID kanału Discord:"),
                DeckActionType.SpotifyAddToQueue => loc.GetString("ParamLabel_SpotifyQueue", "URI lub URL utworu Spotify (spotify:track:...):"),
                DeckActionType.SpotifyPlayUri => loc.GetString("ParamLabel_SpotifyPlayUri", "URI lub URL playlisty / albumu / utworu Spotify:"),
                DeckActionType.VoicemodVoiceSelect => loc.GetString("ParamLabel_VoicemodVoice", "ID lub nazwa głosu Voicemod (np. clean, robot, cave):"),
                DeckActionType.VoicemodPlaySound => loc.GetString("ParamLabel_VoicemodSound", "Nazwa pliku lub ID mema/dźwięku Voicemod (FileName):"),
                DeckActionType.HomeAssistantToggle => loc.GetString("ParamLabel_HomeAssistantToggle", "Identyfikator encji (np. light.living_room, switch.fan):"),
                DeckActionType.HomeAssistantTurnOn => loc.GetString("ParamLabel_HomeAssistantTurnOn", "Identyfikator encji do włączenia:"),
                DeckActionType.HomeAssistantTurnOff => loc.GetString("ParamLabel_HomeAssistantTurnOff", "Identyfikator encji do wyłączenia:"),
                DeckActionType.HomeAssistantCallService => loc.GetString("ParamLabel_HomeAssistantCallService", "Usługa (np. light.turn_on lub scene.turn_on):"),
                DeckActionType.ConditionalAction => loc.GetString("ParamLabel_ConditionalAction", "Reguła warunkowa (format: Cel?AkcjaTrue:AkcjaFalse lub JSON):"),
                _ => loc.GetString("ActionParam_Label", "Parametr akcji:")
            };
        }
    }

    public string ActionParameterWatermark
    {
        get
        {
            var loc = LocalizationService.Instance;
            return AssignedAction switch
            {
                DeckActionType.OpenApplication => loc.GetString("ParamWatermark_OpenApp", "np. C:\\Program Files\\App\\app.exe"),
                DeckActionType.OpenWebsite => loc.GetString("ParamWatermark_OpenWebsite", "np. https://youtube.com"),
                DeckActionType.OpenFile => loc.GetString("ParamWatermark_OpenFile", "np. D:\\Dokumenty\\plik.pdf"),
                DeckActionType.CloseApplication => loc.GetString("ParamWatermark_CloseApp", "np. notepad.exe, chrome lub puste dla bieżącego okna"),
                DeckActionType.TextPaste => loc.GetString("ParamWatermark_TextPaste", "Wpisz tekst do automatycznego wklejenia..."),
                DeckActionType.Hotkey => loc.GetString("ParamWatermark_Hotkey", "np. Ctrl+Shift+S"),
                DeckActionType.HotkeySwitch => loc.GetString("ParamWatermark_HotkeySwitch", "np. Ctrl+M|Ctrl+Shift+M"),
                DeckActionType.MultiAction => loc.GetString("ParamWatermark_MultiAction", "np. calc.exe;delay:500;notepad.exe"),
                DeckActionType.Delay => loc.GetString("ParamWatermark_Delay", "np. 500 lub 1000"),
                DeckActionType.TypeText => loc.GetString("ParamWatermark_TypeText", "e.g. Hello, thanks for watching!"),
                DeckActionType.HttpRequest => loc.GetString("ParamWatermark_Http", "e.g. POST http://127.0.0.1:8080/api | {\"on\":true}"),
                DeckActionType.MediaSeekForward or DeckActionType.MediaSeekBackward => loc.GetString("ParamWatermark_Seek", "empty, vlc, vlc-short, youtube, spotify…"),
                DeckActionType.ForegroundAppVolumeUp or DeckActionType.ForegroundAppVolumeDown => "5",
                DeckActionType.GoToPage => loc.GetString("ParamWatermark_GoToPage", "np. 2"),
                DeckActionType.GoToProfile => loc.GetString("ParamWatermark_GoToProfile", "np. Gaming lub default"),
                DeckActionType.OpenFolder => loc.GetString("ParamWatermark_OpenFolder", "np. OBS Studio, Narzędzia, Gry"),
                DeckActionType.ObsSceneSwitch => loc.GetString("ParamWatermark_ObsScene", "np. Główna, Gra, Przerwa, BRB"),
                DeckActionType.TwitchSendMessage => loc.GetString("ParamWatermark_TwitchMessage", "np. GG WP!, !discord, Witajcie na streamie!"),
                DeckActionType.TwitchCreateMarker => loc.GetString("ParamWatermark_TwitchMarker", "np. Epicki clutch, Śmieszna sytuacja, Boss pokonany..."),
                DeckActionType.TwitchStartRaid => loc.GetString("ParamWatermark_TwitchRaid", "np. streamer_pl, shroud, ninja"),
                DeckActionType.TwitchRunCommercial => loc.GetString("ParamWatermark_TwitchCommercial", "np. 30, 60, 90, 180"),
                DeckActionType.KickSendMessage => loc.GetString("ParamWatermark_KickMessage", "np. Siema czat!, !sociale, GG!"),
                DeckActionType.YouTubeLiveViewers => loc.GetString("ParamWatermark_YouTubeViewers", "np. @KanałYouTube, UC..., dQw4w9WgXcQ lub puste dla domyślnego"),
                DeckActionType.StreamlabsSceneSwitch => loc.GetString("ParamWatermark_StreamlabsScene", "np. Starting Soon, In Game, Be Right Back"),
                DeckActionType.DiscordJoinVoiceChannel => loc.GetString("ParamWatermark_DiscordVoice", "np. https://discord.com/channels/... lub ID kanału"),
                DeckActionType.SpotifyAddToQueue => loc.GetString("ParamWatermark_SpotifyQueue", "np. spotify:track:4cOdK2wGLETKBW3PvgPWqT lub link open.spotify.com"),
                DeckActionType.SpotifyPlayUri => loc.GetString("ParamWatermark_SpotifyPlayUri", "np. spotify:playlist:37i9dQZF1DXcBWIGoYBM5M lub link open.spotify.com"),
                DeckActionType.VoicemodVoiceSelect => loc.GetString("ParamWatermark_VoicemodVoice", "np. clean, robot, magic-chords, astronaut"),
                DeckActionType.VoicemodPlaySound => loc.GetString("ParamWatermark_VoicemodSound", "np. Airhorn, Bruh lub wybierz z listy w Ustawieniach..."),
                DeckActionType.HomeAssistantToggle => loc.GetString("ParamWatermark_HomeAssistantToggle", "np. light.salon, switch.gniazdko"),
                DeckActionType.HomeAssistantTurnOn => loc.GetString("ParamWatermark_HomeAssistantTurnOn", "np. light.biurko"),
                DeckActionType.HomeAssistantTurnOff => loc.GetString("ParamWatermark_HomeAssistantTurnOff", "np. light.wszystkie"),
                DeckActionType.HomeAssistantCallService => loc.GetString("ParamWatermark_HomeAssistantCallService", "np. light.turn_on, climate.set_temperature"),
                DeckActionType.ConditionalAction => loc.GetString("ParamWatermark_ConditionalAction", "np. obs64?ObsStopStream:ObsStartStream"),
                _ => loc.GetString("ParamWatermark_Default", "Wartość parametru...")
            };
        }
    }

    public void NotifyLanguageChanged()
    {
        OnLanguageChanged(LocalizationService.Instance.CurrentLanguageCode);
    }
}
