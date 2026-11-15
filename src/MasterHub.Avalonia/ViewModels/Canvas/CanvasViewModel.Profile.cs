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

    public void LoadCurrentViewKeys(ProfileModel profile)
    {
        List<KeyAssignmentModel>? sourceKeys = null;
        if (IsInsideFolder && CurrentFolderId != null)
        {
            var folder = profile.Folders?.FirstOrDefault(f => f.Id == CurrentFolderId);
            if (folder != null)
            {
                EnsureFolderKeysInitialized(folder);
                sourceKeys = folder.Keys;
            }
        }
        else
        {
            EnsurePagesInitialized(profile);
            if (CurrentPageIndex >= 0 && CurrentPageIndex < profile.Pages.Count)
            {
                sourceKeys = profile.Pages[CurrentPageIndex].Keys;
            }
        }

        if (sourceKeys == null)
        {
            if (IsInsideFolder)
            {
                sourceKeys = new List<KeyAssignmentModel>
                {
                    new()
                    {
                        Index = 0,
                        Action = DeckActionType.GoBack,
                        CustomTitle = "POWRÓT",
                        BackgroundColorHex = "#1E293B",
                        TextColorHex = "#FFFFFF"
                    }
                };
                for (int i = 1; i < 15; i++)
                {
                    sourceKeys.Add(new KeyAssignmentModel { Index = i, Action = DeckActionType.None });
                }
            }
            else
            {
                sourceKeys = profile.Keys;
            }
        }

        _autoSaveTimer.Stop();
        _isApplyingProfile = true;

        _animatedKeyRenderer.StopAllAnimations();
        Interlocked.Increment(ref _pageRenderVersion);

        foreach (var key in Keys)
        {
            if (key.AssignedAction == DeckActionType.StreamDeckPluginAction && !string.IsNullOrEmpty(key.StreamDeckPluginUuid))
            {
                _ = _streamDeckPluginHost?.SendWillDisappearAsync(key.Index, key.StreamDeckPluginUuid, key.StreamDeckActionUuid);
            }
        }

        try
        {
            for (int i = 0; i < Keys.Count; i++)
            {
                var targetKey = Keys[i];
                targetKey.IsPressed = false;
                targetKey.ClearActionResult(); // the failure mark belongs to a profile + control
                targetKey.IsSelected = false;
                targetKey.SetRenderedJpeg(null);
                var k = sourceKeys.FirstOrDefault(x => x.Index == i);
                if (k != null)
                {
                    targetKey.AssignedAction = k.Action;
                    targetKey.ActionParameter = k.ActionParameter ?? "";
                    if (k.Action == DeckActionType.ConditionalAction && k.ConditionalAction != null && string.IsNullOrWhiteSpace(targetKey.ActionParameter))
                    {
                        targetKey.ActionParameter = k.ConditionalAction.ToParameterString();
                    }
                    targetKey.PressMode = k.PressMode;
                    targetKey.SkipPowerConfirmation = k.SkipPowerConfirmation;
                    targetKey.MutePressMode = k.MutePressMode;
                    targetKey.LoadGestures(k.Gestures);
                    targetKey.LoadLiveValue(k.LiveValue);
                    targetKey.LoadTimer(k.Timer);
                    targetKey.LoadSound(k.Sound);
                    targetKey.LoadHotkeyMode(k.HotkeySendMode, k.HotkeyHoldMs);
                    targetKey.LoadLookRules(k.LookRules);
                    targetKey.StateMode = k.StateMode;
                    targetKey.CustomTitle = k.CustomTitle ?? "";
                    targetKey.CustomSubtitle = k.CustomSubtitle ?? "";
                    targetKey.ActiveCustomTitle = k.ActiveCustomTitle ?? "";
                    targetKey.ActiveCustomSubtitle = k.ActiveCustomSubtitle ?? "";
                    targetKey.BackgroundColorHex = !string.IsNullOrEmpty(k.BackgroundColorHex) ? k.BackgroundColorHex : "#000000";
                    targetKey.TextColorHex = !string.IsNullOrEmpty(k.TextColorHex) ? k.TextColorHex : "#FFFFFF";
                    targetKey.SubtitleColorHex = !string.IsNullOrWhiteSpace(k.SubtitleColorHex) ? k.SubtitleColorHex : "#A1A1AA";
                    targetKey.ActiveBackgroundColorHex = k.ActiveBackgroundColorHex ?? "";
                    targetKey.ActiveTextColorHex = k.ActiveTextColorHex ?? "";
                    targetKey.ActiveSubtitleColorHex = k.ActiveSubtitleColorHex ?? "";
                    targetKey.ImagePath = k.ImagePath ?? "";
                    targetKey.ActiveImagePath = k.ActiveImagePath ?? "";
                    targetKey.ImageFitMode = !string.IsNullOrWhiteSpace(k.ImageFitMode) ? k.ImageFitMode : "Cover";
                    targetKey.ImageScale = k.ImageScale > 0 ? k.ImageScale : 100;
                    targetKey.ImageOffsetX = k.ImageOffsetX;
                    targetKey.ImageOffsetY = k.ImageOffsetY;
                    targetKey.ActiveImageFitMode = !string.IsNullOrWhiteSpace(k.ActiveImageFitMode) ? k.ActiveImageFitMode : "Cover";
                    targetKey.ActiveImageScale = k.ActiveImageScale > 0 ? k.ActiveImageScale : 100;
                    targetKey.ActiveImageOffsetX = k.ActiveImageOffsetX;
                    targetKey.ActiveImageOffsetY = k.ActiveImageOffsetY;
                    targetKey.FontFamily = string.IsNullOrWhiteSpace(k.FontFamily) ? "Segoe UI" : k.FontFamily;
                    targetKey.FontSize = k.FontSize;
                    targetKey.IsBold = k.IsBold;
                    targetKey.IsItalic = k.IsItalic;
                    targetKey.IsStrikeThrough = k.IsStrikeThrough;
                    targetKey.IsTextWrappingEnabled = k.IsTextWrappingEnabled;
                    targetKey.TitleFontFamily = !string.IsNullOrWhiteSpace(k.TitleFontFamily) ? k.TitleFontFamily : (string.IsNullOrWhiteSpace(k.FontFamily) ? "Segoe UI" : k.FontFamily);
                    targetKey.TitleFontSize = k.TitleFontSize > 0 ? k.TitleFontSize : k.FontSize;
                    targetKey.TitleIsBold = k.TitleIsBold;
                    targetKey.TitleIsItalic = k.TitleIsItalic;
                    targetKey.TitleIsStrikeThrough = k.TitleIsStrikeThrough;
                    targetKey.TitleIsTextWrappingEnabled = k.TitleIsTextWrappingEnabled;
                    targetKey.TitleAlignment = !string.IsNullOrWhiteSpace(k.TitleAlignment) ? k.TitleAlignment : "Center";
                    targetKey.TitleVerticalPosition = !string.IsNullOrWhiteSpace(k.TitleVerticalPosition) ? k.TitleVerticalPosition : "Center";
                    targetKey.TitleOffsetX = k.TitleOffsetX;
                    targetKey.TitleOffsetY = k.TitleOffsetY;

                    targetKey.SubtitleFontFamily = !string.IsNullOrWhiteSpace(k.SubtitleFontFamily) ? k.SubtitleFontFamily : (string.IsNullOrWhiteSpace(k.FontFamily) ? "Segoe UI" : k.FontFamily);
                    targetKey.SubtitleFontSize = k.SubtitleFontSize;
                    targetKey.SubtitleIsBold = k.SubtitleIsBold;
                    targetKey.SubtitleIsItalic = k.SubtitleIsItalic;
                    targetKey.SubtitleIsStrikeThrough = k.SubtitleIsStrikeThrough;
                    targetKey.SubtitleIsTextWrappingEnabled = k.SubtitleIsTextWrappingEnabled;
                    targetKey.SubtitleAlignment = !string.IsNullOrWhiteSpace(k.SubtitleAlignment) ? k.SubtitleAlignment : "Center";
                    targetKey.SubtitleVerticalPosition = !string.IsNullOrWhiteSpace(k.SubtitleVerticalPosition) ? k.SubtitleVerticalPosition : "Bottom";
                    targetKey.SubtitleOffsetX = k.SubtitleOffsetX;
                    targetKey.SubtitleOffsetY = k.SubtitleOffsetY;
                    targetKey.ObsSceneName = k.ObsSceneName ?? "";
                    targetKey.StreamDeckPluginUuid = k.StreamDeckPluginUuid ?? "";
                    targetKey.StreamDeckActionUuid = k.StreamDeckActionUuid ?? "";
                    targetKey.StreamDeckSettingsJson = k.StreamDeckSettingsJson ?? "";

                    if (targetKey.AssignedAction == DeckActionType.StreamDeckPluginAction && !string.IsNullOrEmpty(targetKey.StreamDeckPluginUuid))
                    {
                        _ = SendStreamDeckWillAppearAsync(targetKey);
                    }
                    if (targetKey.AssignedAction == DeckActionType.JetKvmPowerStatus ||
                        targetKey.AssignedAction == DeckActionType.JetKvmPowerShort)
                    {
                        targetKey.IsActiveToggleState = _jetKvmService.LastPowerState;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.SpotifyToggleShuffle)
                    {
                        targetKey.IsActiveToggleState = _spotifyService.CurrentState.IsShuffle;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.SpotifyToggleRepeat)
                    {
                        targetKey.IsActiveToggleState = _spotifyService.CurrentState.RepeatMode != SpotifyRepeatMode.Off;
                        targetKey.ActionParameter = _spotifyService.CurrentState.RepeatMode switch
                        {
                            SpotifyRepeatMode.Track => "REP 1",
                            SpotifyRepeatMode.Context => "REP ALL",
                            _ => "REP OFF"
                        };
                    }
                    else if (targetKey.AssignedAction == DeckActionType.SpotifyLikeTrack)
                    {
                        targetKey.IsActiveToggleState = _spotifyService.CurrentState.IsLiked;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VlcPlayPause)
                    {
                        targetKey.IsActiveToggleState = _vlcService.IsPlaying;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VlcFullscreenToggle)
                    {
                        targetKey.IsActiveToggleState = _vlcService.IsFullscreen;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VoicemodVoiceToggle)
                    {
                        targetKey.IsActiveToggleState = _voicemodService.IsVoiceChangerEnabled;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VoicemodMuteVoice)
                    {
                        targetKey.IsActiveToggleState = _voicemodService.IsMicMuted;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VoicemodHearMyVoiceToggle)
                    {
                        targetKey.IsActiveToggleState = _voicemodService.IsHearMyVoiceEnabled;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VoicemodBackgroundToggle)
                    {
                        targetKey.IsActiveToggleState = _voicemodService.IsBackgroundEffectsEnabled;
                    }
                    else if (targetKey.AssignedAction == DeckActionType.VoicemodVoiceSelect)
                    {
                        bool isCurrent = !string.IsNullOrWhiteSpace(targetKey.ActionParameter) &&
                                         (string.Equals(targetKey.ActionParameter, _voicemodService.CurrentVoiceId, StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(targetKey.ActionParameter, _voicemodService.CurrentVoiceName, StringComparison.OrdinalIgnoreCase));
                        targetKey.IsActiveToggleState = isCurrent;
                    }
                    else if (targetKey.AssignedAction is DeckActionType.HomeAssistantToggle or
                                                         DeckActionType.HomeAssistantTurnOn or
                                                         DeckActionType.HomeAssistantTurnOff)
                    {
                        var entity = _homeAssistantService?.GetEntity(targetKey.ActionParameter);
                        if (entity != null)
                        {
                            targetKey.IsActiveToggleState = entity.IsOn;
                        }
                    }
                }
                else
                {
                    targetKey.ClearAction();
                }
            }

            var page = (CurrentPageIndex >= 0 && profile.Pages != null && CurrentPageIndex < profile.Pages.Count)
                ? profile.Pages[CurrentPageIndex]
                : null;
            var effectiveWallpaperMode = !IsInsideFolder
                ? (page?.WallpaperMode ?? profile.WallpaperMode)
                : DeckWallpaperMode.PerKey;
            var effectiveWallpaperPath = !IsInsideFolder
                ? (page?.PanoramicWallpaperPath ?? profile.PanoramicWallpaperPath)
                : null;
            var effectiveFitMode = !IsInsideFolder
                ? (page?.PanoramicWallpaperFitMode ?? profile.PanoramicWallpaperFitMode ?? "Cover")
                : "Cover";

            bool onPage = !IsInsideFolder && page != null;
            PanoramicWallpaperScale = Math.Clamp(!IsInsideFolder ? (onPage ? page!.PanoramicWallpaperScale : profile.PanoramicWallpaperScale) : 100, 20, 400);
            PanoramicWallpaperOffsetX = Math.Clamp(!IsInsideFolder ? (onPage ? page!.PanoramicWallpaperOffsetX : profile.PanoramicWallpaperOffsetX) : 0, -100, 100);
            PanoramicWallpaperOffsetY = Math.Clamp(!IsInsideFolder ? (onPage ? page!.PanoramicWallpaperOffsetY : profile.PanoramicWallpaperOffsetY) : 0, -100, 100);
            WallpaperMode = effectiveWallpaperMode;
            PanoramicWallpaperPath = effectiveWallpaperPath;
            PanoramicWallpaperFitMode = effectiveFitMode;
            SyncPanoramicWallpaperStateToKeys();

            bool isPanoramic = effectiveWallpaperMode == DeckWallpaperMode.PanoramicDeck &&
                               !string.IsNullOrWhiteSpace(effectiveWallpaperPath) &&
                               System.IO.File.Exists(effectiveWallpaperPath);

            if (isPanoramic)
            {
                RestartPanorama();   // the wallpaper (still or moving) with the keys' text laid over it
            }
            else
            {
                StopPanorama();
                for (int i = 0; i < Keys.Count; i++)
                {
                    RenderKeyVisual(Keys[i], sendToHardware: true);
                }
            }

            OnPropertyChanged(nameof(SelectedKey));
        }
        finally
        {
            _isApplyingProfile = false;
        }

        UpdateMonitoringDemand();   // nowy widok decka = nowy zestaw wyświetlanych metryk
    }

    public void ApplyProfile(ProfileModel? profile, bool saveCurrent = true)
    {
        if (profile == null) return;

        ClearEditHistory(); // undo does not cross a profile switch
        _lightsOverride = false; // lights set from the CLI last until the profile changes

        if (saveCurrent && _hasLoadedProfile)
        {
            SaveCurrentStateToActiveProfile();
        }

        _navStack.Clear();
        _currentPageIndex = 0;
        _currentFolderId = null;
        _currentFolderName = "";

        EnsurePagesInitialized(profile);
        profile.Folders ??= new List<KeyFolderModel>();

        _autoSaveTimer.Stop();
        _isApplyingProfile = true;

        try
        {
            LoadCurrentViewKeys(profile);

            foreach (var s in profile.Sliders)
            {
                if (s.Index >= 0 && s.Index < Sliders.Count)
                {
                    var targetSlider = Sliders[s.Index];
                    targetSlider.SelectedTarget = s.DisplayTarget;
                    targetSlider.ColorHex = string.IsNullOrEmpty(s.ColorHex) ? "#38BDF8" : s.ColorHex;
                    targetSlider.Brightness = (byte)Math.Clamp(s.Brightness, 0, 100);
                }
            }

            foreach (var r in profile.Rollers)
            {
                if (r.Index >= 0 && r.Index < Rollers.Count)
                {
                    var targetRoller = Rollers[r.Index];
                    targetRoller.AssignedAction = r.Action;
                    if (!string.IsNullOrEmpty(r.ColorHex)) targetRoller.ColorHex = r.ColorHex;
                    targetRoller.Brightness = (byte)Math.Clamp(r.Brightness, 0, 100);
                }
            }

            if (profile.Knobs != null && profile.Knobs.Count > 0)
            {
                foreach (var k in profile.Knobs)
                {
                    if (k.Index >= 0 && k.Index < Knobs.Count)
                    {
                        var targetKnob = Knobs[k.Index];
                        targetKnob.RotateAction = k.RotateAction;
                        targetKnob.RotateModes = k.RotateModes ?? "";
                        targetKnob.PressAction = k.PressAction;
                        targetKnob.PressActionParameter = k.PressActionParameter;
                        targetKnob.LedMode = k.LedMode;
                        if (!string.IsNullOrEmpty(k.ColorHex)) targetKnob.ColorHex = k.ColorHex;
                        targetKnob.Brightness = (byte)Math.Clamp(k.Brightness, 0, 100);
                        targetKnob.Speed = (byte)Math.Clamp(k.Speed, 0, 100);
                    }
                }
            }

            if (profile.Dial != null && Dial != null)
            {
                Dial.RotateAction = profile.Dial.RotateAction;
                Dial.RotateModes = profile.Dial.RotateModes ?? "";
                Dial.PressAction = profile.Dial.PressAction;
                Dial.PressActionParameter = profile.Dial.PressActionParameter;
                if (!string.IsNullOrEmpty(profile.Dial.ColorHex)) Dial.ColorHex = profile.Dial.ColorHex;
                Dial.Brightness = (byte)Math.Clamp((int)profile.Dial.Brightness, 0, 100);
                Dial.ImagePath = profile.Dial.ImagePath;
                Dial.DisplayWidget = profile.Dial.DisplayWidget;
            }

            if (!string.IsNullOrEmpty(profile.LedColorHex))
            {
                BaseStation.ColorHex = profile.LedColorHex;
            }
            BaseStation.LoadEdge(profile.BaseLedMode, profile.BaseLedSpeed);
            if (profile.BaseBrightness >= 0)
            {
                BaseStation.Brightness = (byte)Math.Clamp(profile.BaseBrightness, 0, 100);
            }
            if (profile.KeypadBrightness >= 0)
            {
                KeypadBrightness = (byte)Math.Clamp(profile.KeypadBrightness, 0, 100);
            }

            RotationMode = profile.RotationMode;
            ManualRotationAngle = profile.ManualRotationAngle;
            _deviceService.RotationMode = profile.RotationMode;
            _deviceService.ManualRotationAngle = profile.ManualRotationAngle;
            UpdateRotationDisplayState(_deviceService.EffectiveRotationAngle);

            _hasLoadedProfile = true;
            _loadedProfileId = profile.Id;
            PushAllHardwareState();
            NotifyNavProperties();
        }
        finally
        {
            _isApplyingProfile = false;
        }
    }
}
