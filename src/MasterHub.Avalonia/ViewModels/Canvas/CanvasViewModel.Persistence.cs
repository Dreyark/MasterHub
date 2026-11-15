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
    /// The profile the canvas is showing. When the active profile was changed (a click on the Profiles page, auto-switching) and the
    /// canvas has not been given the new one yet, the canvas still holds the <i>previous</i> profile: its keys must be saved there,
    /// never into the profile that just became active. <c>null</c> when that profile is gone (deleted, or a draft that was thrown away).
    /// </summary>
    private ProfileModel? ResolveProfileOnCanvas()
    {
        var active = _profileService.GetActiveProfile();
        if (active == null || _loadedProfileId == null || active.Id == _loadedProfileId) return active;

        return _profileService.GetAllProfiles().FirstOrDefault(p => p.Id == _loadedProfileId);
    }

    public void SaveCurrentStateToActiveProfile()
    {
        try
        {
            var profile = ResolveProfileOnCanvas();
            if (profile == null) return;

            EnsurePagesInitialized(profile);
            profile.Folders ??= new List<KeyFolderModel>();

            // Zapisz klawisze do bieżącego widoku (folder lub strona)
            var currentKeyAssignments = BuildKeyAssignments();

            StoreViewKeys(profile, currentKeyAssignments);

            // Zapisz suwaki
            profile.Sliders.Clear();
            foreach (var slider in Sliders)
            {
                profile.Sliders.Add(new SliderAssignmentModel
                {
                    Index = slider.Index,
                    TargetType = slider.TargetType,
                    TargetProcessName = slider.TargetProcessName,
                    DisplayTarget = slider.SelectedTarget,
                    ColorHex = slider.ColorHex,
                    Brightness = slider.Brightness
                });
            }

            // Zapisz rolki
            profile.Rollers.Clear();
            foreach (var roller in Rollers)
            {
                profile.Rollers.Add(new RollerAssignmentModel
                {
                    Index = roller.Index,
                    Action = roller.AssignedAction,
                    ColorHex = roller.ColorHex,
                    Brightness = roller.Brightness
                });
            }

            // Zapisz pokrętła 3 Knobs
            profile.Knobs ??= new List<KnobAssignmentModel>();
            profile.Knobs.Clear();
            foreach (var knob in Knobs)
            {
                profile.Knobs.Add(new KnobAssignmentModel
                {
                    Index = knob.Index,
                    RotateAction = knob.RotateAction,
                    RotateModes = knob.RotateModes,
                    PressAction = knob.PressAction,
                    PressActionParameter = knob.PressActionParameter,
                    LedMode = knob.LedMode,
                    ColorHex = knob.ColorHex,
                    Brightness = knob.Brightness,
                    Speed = knob.Speed
                });
            }

            // Zapisz Dial IPS
            if (Dial != null)
            {
                profile.Dial = new DialAssignmentModel
                {
                    RotateAction = Dial.RotateAction,
                    RotateModes = Dial.RotateModes,
                    PressAction = Dial.PressAction,
                    PressActionParameter = Dial.PressActionParameter,
                    Brightness = Dial.Brightness,
                    ColorHex = Dial.ColorHex,
                    ImagePath = Dial.ImagePath,
                    DisplayWidget = Dial.DisplayWidget
                };
            }

            // Zapisz stację dokującą i podświetlenie
            profile.LedColorHex = BaseStation.ColorHex;
            profile.BaseBrightness = BaseStation.Brightness;
            profile.BaseLedMode = BaseStation.EdgeEffect;
            profile.BaseLedSpeed = BaseStation.EdgeSpeed;
            profile.KeypadBrightness = KeypadBrightness;

            // Zapisz rotację i orientację paneli
            profile.RotationMode = RotationMode;
            profile.ManualRotationAngle = ManualRotationAngle;

            _profileService.SaveProfile(profile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to auto-save profile: {ex.Message}");
        }
    }

    /// <summary>The keys of the current view (page or folder) in the form they are saved in a profile.</summary>
    public List<KeyAssignmentModel> BuildKeyAssignments()
    {
            var currentKeyAssignments = new List<KeyAssignmentModel>();
            foreach (var key in Keys)
            {
                currentKeyAssignments.Add(new KeyAssignmentModel
                {
                    Index = key.Index,
                    Action = key.AssignedAction,
                    PressMode = key.PressMode,
                    SkipPowerConfirmation = key.SkipPowerConfirmation,
                    MutePressMode = key.MutePressMode,
                    Gestures = key.ToGesturesModel(),
                    LiveValue = key.ToLiveModel(),
                    Timer = key.ToTimerModel(),
                    Sound = key.ToSoundModel(),
                    HotkeySendMode = key.HotkeySendMode,
                    HotkeyHoldMs = key.HotkeyHoldMs,
                    LookRules = key.ToLookModel(),
                    CustomTitle = key.CustomTitle,
                    CustomSubtitle = key.CustomSubtitle,
                    ActiveCustomTitle = key.ActiveCustomTitle,
                    ActiveCustomSubtitle = key.ActiveCustomSubtitle,
                    BackgroundColorHex = key.BackgroundColorHex,
                    TextColorHex = key.TextColorHex,
                    SubtitleColorHex = key.SubtitleColorHex,
                    ActiveBackgroundColorHex = key.ActiveBackgroundColorHex,
                    ActiveTextColorHex = key.ActiveTextColorHex,
                    ActiveSubtitleColorHex = key.ActiveSubtitleColorHex,
                    ImagePath = key.ImagePath,
                    ActiveImagePath = key.ActiveImagePath,
                    FontFamily = key.FontFamily,
                    FontSize = key.FontSize,
                    IsBold = key.IsBold,
                    IsItalic = key.IsItalic,
                    IsStrikeThrough = key.IsStrikeThrough,
                    IsTextWrappingEnabled = key.IsTextWrappingEnabled,
                    TitleFontFamily = key.TitleFontFamily,
                    TitleFontSize = key.TitleFontSize,
                    TitleIsBold = key.TitleIsBold,
                    TitleIsItalic = key.TitleIsItalic,
                    TitleIsStrikeThrough = key.TitleIsStrikeThrough,
                    TitleIsTextWrappingEnabled = key.TitleIsTextWrappingEnabled,
                    TitleAlignment = key.TitleAlignment,
                    TitleVerticalPosition = key.TitleVerticalPosition,
                    TitleOffsetX = key.TitleOffsetX,
                    TitleOffsetY = key.TitleOffsetY,
                    SubtitleFontFamily = key.SubtitleFontFamily,
                    SubtitleFontSize = key.SubtitleFontSize,
                    SubtitleIsBold = key.SubtitleIsBold,
                    SubtitleIsItalic = key.SubtitleIsItalic,
                    SubtitleIsStrikeThrough = key.SubtitleIsStrikeThrough,
                    SubtitleIsTextWrappingEnabled = key.SubtitleIsTextWrappingEnabled,
                    SubtitleAlignment = key.SubtitleAlignment,
                    SubtitleVerticalPosition = key.SubtitleVerticalPosition,
                    SubtitleOffsetX = key.SubtitleOffsetX,
                    SubtitleOffsetY = key.SubtitleOffsetY,
                    ActionParameter = key.ActionParameter,
                    ConditionalAction = key.AssignedAction == DeckActionType.ConditionalAction ? ConditionalActionModel.Parse(key.ActionParameter) : null,
                    StateMode = key.StateMode,
                    ObsSceneName = key.ObsSceneName,
                    ImageFitMode = key.ImageFitMode,
                    ImageScale = key.ImageScale,
                    ImageOffsetX = key.ImageOffsetX,
                    ImageOffsetY = key.ImageOffsetY,
                    ActiveImageFitMode = key.ActiveImageFitMode,
                    ActiveImageScale = key.ActiveImageScale,
                    ActiveImageOffsetX = key.ActiveImageOffsetX,
                    ActiveImageOffsetY = key.ActiveImageOffsetY,
                    StreamDeckPluginUuid = key.AssignedAction == DeckActionType.StreamDeckPluginAction ? key.StreamDeckPluginUuid : null,
                    StreamDeckActionUuid = key.AssignedAction == DeckActionType.StreamDeckPluginAction ? key.StreamDeckActionUuid : null,
                    StreamDeckSettingsJson = key.AssignedAction == DeckActionType.StreamDeckPluginAction ? key.StreamDeckSettingsJson : null
                });
            }
            return currentKeyAssignments;
    }

    /// <summary>Stores keys as the content of the current view (folder, or page; page 1 also in the top-level list).</summary>
    private void StoreViewKeys(ProfileModel profile, List<KeyAssignmentModel> currentKeyAssignments)
    {
            if (IsInsideFolder && CurrentFolderId != null)
            {
                var folder = profile.Folders.FirstOrDefault(f => f.Id == CurrentFolderId);
                if (folder != null)
                {
                    folder.Keys = currentKeyAssignments;
                }
            }
            else
            {
                if (CurrentPageIndex >= 0 && CurrentPageIndex < profile.Pages.Count)
                {
                    profile.Pages[CurrentPageIndex].Keys = currentKeyAssignments;
                    profile.Pages[CurrentPageIndex].WallpaperMode = WallpaperMode;
                    profile.Pages[CurrentPageIndex].PanoramicWallpaperPath = PanoramicWallpaperPath;
                    profile.Pages[CurrentPageIndex].PanoramicWallpaperFitMode = PanoramicWallpaperFitMode;
                    profile.Pages[CurrentPageIndex].PanoramicWallpaperScale = PanoramicWallpaperScale;
                    profile.Pages[CurrentPageIndex].PanoramicWallpaperOffsetX = PanoramicWallpaperOffsetX;
                    profile.Pages[CurrentPageIndex].PanoramicWallpaperOffsetY = PanoramicWallpaperOffsetY;
                }
                if (CurrentPageIndex == 0)
                {
                    profile.Keys = new List<KeyAssignmentModel>(currentKeyAssignments);
                    profile.WallpaperMode = WallpaperMode;
                    profile.PanoramicWallpaperPath = PanoramicWallpaperPath;
                    profile.PanoramicWallpaperFitMode = PanoramicWallpaperFitMode;
                    profile.PanoramicWallpaperScale = PanoramicWallpaperScale;
                    profile.PanoramicWallpaperOffsetX = PanoramicWallpaperOffsetX;
                    profile.PanoramicWallpaperOffsetY = PanoramicWallpaperOffsetY;
                }
            }
    }
}
