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

    public string StatusText => IsPressed
        ? LocalizationService.Instance.GetString("Key_Pressed", "Wciśnięty")
        : LocalizationService.Instance.GetString("Key_Released", "Zwolniony");

    public void ResetStyling()
    {
        _backgroundColorHex = "#000000";
        _textColorHex = "#FFFFFF";
        _subtitleColorHex = "#A1A1AA";
        _activeBackgroundColorHex = "";
        _activeTextColorHex = "";
        _activeSubtitleColorHex = "";
        _imagePath = "";
        _activeImagePath = "";
        _imageFitMode = "Cover";
        _imageScale = 100;
        _imageOffsetX = 0;
        _imageOffsetY = 0;
        _activeImageFitMode = "Cover";
        _activeImageScale = 100;
        _activeImageOffsetX = 0;
        _activeImageOffsetY = 0;
        _isActiveToggleState = false;
        _customTitle = "";
        _customSubtitle = "";
        _activeCustomTitle = "";
        _activeCustomSubtitle = "";
        _selectedEditState = 0;
        _fontFamily = "Segoe UI";
        _fontSize = 0;
        _isBold = true;
        _isItalic = false;
        _isStrikeThrough = false;
        _isTextWrappingEnabled = true;

        _titleFontFamily = "Segoe UI";
        _titleFontSize = 0;
        _titleIsBold = true;
        _titleIsItalic = false;
        _titleIsStrikeThrough = false;
        _titleIsTextWrappingEnabled = true;
        _titleAlignment = "Center";
        _titleVerticalPosition = "Center";
        _titleOffsetX = 0;
        _titleOffsetY = 0;

        _subtitleFontFamily = "Segoe UI";
        _subtitleFontSize = 0;
        _subtitleIsBold = false;
        _subtitleIsItalic = false;
        _subtitleIsStrikeThrough = false;
        _subtitleIsTextWrappingEnabled = true;
        _subtitleAlignment = "Center";
        _subtitleVerticalPosition = "Bottom";
        _subtitleOffsetX = 0;
        _subtitleOffsetY = 0;
        _pressMode = KeyPressMode.OnPress;
        _skipPowerConfirmation = false;
        _mutePressMode = MasterHub.Core.Audio.MutePressMode.Toggle;
        ResetGestures();
        ResetLiveValue();
        ResetTimer();
        ResetSound();
        ResetHotkeyMode();
        ResetLooks();
        _stateMode = KeyStateMode.Static;
        _obsSceneName = "";
        _liveValueText = null;
        _mediaValidationMessage = null;
        _streamDeckPluginUuid = "";
        _streamDeckActionUuid = "";
        _streamDeckSettingsJson = "";
        _pluginDynamicImageBytes = null;
        IsStreamDeckAlert = false;
        IsStreamDeckOk = false;

        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
        _cachedActiveBitmap?.Dispose();
        _cachedActiveBitmap = null;
        SetRenderedJpeg(null);

        OnPropertyChanged(nameof(BackgroundColorHex));
        OnPropertyChanged(nameof(BackgroundColorBrush));
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(TextColorHex));
        OnPropertyChanged(nameof(TextColorBrush));
        OnPropertyChanged(nameof(TitleColorHex));
        OnPropertyChanged(nameof(TitleColorBrush));
        OnPropertyChanged(nameof(TitleColor));
        OnPropertyChanged(nameof(SubtitleColorHex));
        OnPropertyChanged(nameof(SubtitleColorBrush));
        OnPropertyChanged(nameof(ActiveBackgroundColorHex));
        OnPropertyChanged(nameof(ActiveTextColorHex));
        OnPropertyChanged(nameof(ActiveSubtitleColorHex));
        OnPropertyChanged(nameof(StreamDeckPluginUuid));
        OnPropertyChanged(nameof(StreamDeckActionUuid));
        OnPropertyChanged(nameof(StreamDeckSettingsJson));
        OnPropertyChanged(nameof(ImagePath));
        OnPropertyChanged(nameof(ActiveImagePath));
        OnPropertyChanged(nameof(HasActiveImage));
        OnPropertyChanged(nameof(HasCustomImage));
        OnPropertyChanged(nameof(HasAnyImage));
        OnPropertyChanged(nameof(CustomImageBitmap));
        OnPropertyChanged(nameof(ActiveImageBitmap));
        OnPropertyChanged(nameof(ImageFitMode));
        OnPropertyChanged(nameof(ImageScale));
        OnPropertyChanged(nameof(ImageOffsetX));
        OnPropertyChanged(nameof(ImageOffsetY));
        OnPropertyChanged(nameof(ActiveImageFitMode));
        OnPropertyChanged(nameof(ActiveImageScale));
        OnPropertyChanged(nameof(ActiveImageOffsetX));
        OnPropertyChanged(nameof(ActiveImageOffsetY));
        OnPropertyChanged(nameof(CustomTitle));
        OnPropertyChanged(nameof(CustomSubtitle));
        OnPropertyChanged(nameof(ActiveCustomTitle));
        OnPropertyChanged(nameof(ActiveCustomSubtitle));
        OnPropertyChanged(nameof(FontFamily));
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(IsBold));
        OnPropertyChanged(nameof(IsItalic));
        OnPropertyChanged(nameof(IsStrikeThrough));
        OnPropertyChanged(nameof(IsTextWrappingEnabled));
        OnPropertyChanged(nameof(PressMode));
        OnPropertyChanged(nameof(PressModeDisplay));
        OnPropertyChanged(nameof(SelectedPressModeOption));
        OnPropertyChanged(nameof(StateMode));
        OnPropertyChanged(nameof(StateModeDisplay));
        OnPropertyChanged(nameof(ObsSceneName));
        OnPropertyChanged(nameof(IsEditStateDefault));
        OnPropertyChanged(nameof(IsEditStateActive));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplaySubtitle));
        OnPropertyChanged(nameof(HasSubtitle));
        OnPropertyChanged(nameof(MediaValidationMessage));
        OnPropertyChanged(nameof(HasMediaValidationMessage));
        NotifyCurrentEditProperties();
    }

    public void LoadAssignment(KeyAssignmentModel? model)
    {
        if (model == null)
        {
            AssignedAction = DeckActionType.None;
            ActionParameter = "";
            ResetStyling();
            return;
        }

        bool isPlugin = model.Action == DeckActionType.StreamDeckPluginAction ||
            (!string.IsNullOrEmpty(model.StreamDeckPluginUuid) && (model.ImagePath?.Contains(".sdPlugin", StringComparison.OrdinalIgnoreCase) == true));

        AssignedAction = isPlugin ? DeckActionType.StreamDeckPluginAction : model.Action;
        PressMode = model.PressMode;
        SkipPowerConfirmation = model.SkipPowerConfirmation;
        MutePressMode = model.MutePressMode;
        LoadGestures(model.Gestures);
        LoadLiveValue(model.LiveValue);
        LoadTimer(model.Timer);
        LoadSound(model.Sound);
        LoadHotkeyMode(model.HotkeySendMode, model.HotkeyHoldMs);
        LoadLookRules(model.LookRules);
        StateMode = model.StateMode;
        ObsSceneName = !string.IsNullOrWhiteSpace(model.ObsSceneName) 
            ? model.ObsSceneName 
            : (model.Action == DeckActionType.ObsSceneSwitch && !string.IsNullOrWhiteSpace(model.ActionParameter) ? model.ActionParameter : "");
        ActionParameter = isPlugin ? "" : (!string.IsNullOrWhiteSpace(model.ActionParameter)
            ? model.ActionParameter
            : (model.Action == DeckActionType.ObsSceneSwitch && !string.IsNullOrWhiteSpace(model.ObsSceneName) 
                ? model.ObsSceneName 
                : (model.ApplicationPath ?? model.Url ?? model.TextContent ?? (model.HotkeyKeys != null && model.HotkeyKeys.Count > 0 ? string.Join("+", model.HotkeyKeys) : ""))));
        CustomTitle = model.CustomTitle ?? (model.LabelText ?? "");
        CustomSubtitle = model.CustomSubtitle ?? "";
        ActiveCustomTitle = model.ActiveCustomTitle ?? "";
        ActiveCustomSubtitle = model.ActiveCustomSubtitle ?? "";
        BackgroundColorHex = string.IsNullOrWhiteSpace(model.BackgroundColorHex) ? "#000000" : model.BackgroundColorHex;
        TextColorHex = !string.IsNullOrWhiteSpace(model.TextColorHex) ? model.TextColorHex : (!string.IsNullOrWhiteSpace(model.LabelColor) ? model.LabelColor : "#FFFFFF");
        SubtitleColorHex = !string.IsNullOrWhiteSpace(model.SubtitleColorHex) ? model.SubtitleColorHex : "#A1A1AA";
        ActiveBackgroundColorHex = model.ActiveBackgroundColorHex ?? "";
        ActiveTextColorHex = model.ActiveTextColorHex ?? "";
        ActiveSubtitleColorHex = model.ActiveSubtitleColorHex ?? "";
        ImagePath = !string.IsNullOrWhiteSpace(model.ImagePath) ? model.ImagePath : (model.CustomIconPath ?? "");
        ActiveImagePath = model.ActiveImagePath ?? "";
        ImageFitMode = !string.IsNullOrWhiteSpace(model.ImageFitMode) ? model.ImageFitMode : "Cover";
        ImageScale = model.ImageScale > 0 ? model.ImageScale : 100;
        ImageOffsetX = model.ImageOffsetX;
        ImageOffsetY = model.ImageOffsetY;
        ActiveImageFitMode = !string.IsNullOrWhiteSpace(model.ActiveImageFitMode) ? model.ActiveImageFitMode : "Cover";
        ActiveImageScale = model.ActiveImageScale > 0 ? model.ActiveImageScale : 100;
        ActiveImageOffsetX = model.ActiveImageOffsetX;
        ActiveImageOffsetY = model.ActiveImageOffsetY;
        IsActiveToggleState = false;
        SelectedEditState = 0;
        FontFamily = string.IsNullOrWhiteSpace(model.FontFamily) ? "Segoe UI" : model.FontFamily;
        FontSize = model.FontSize;
        IsBold = model.IsBold;
        IsItalic = model.IsItalic;
        IsStrikeThrough = model.IsStrikeThrough;
        IsTextWrappingEnabled = model.IsTextWrappingEnabled;

        TitleFontFamily = !string.IsNullOrWhiteSpace(model.TitleFontFamily) ? model.TitleFontFamily : (string.IsNullOrWhiteSpace(model.FontFamily) ? "Segoe UI" : model.FontFamily);
        TitleFontSize = model.TitleFontSize > 0 ? model.TitleFontSize : model.FontSize;
        TitleIsBold = model.TitleIsBold;
        TitleIsItalic = model.TitleIsItalic;
        TitleIsStrikeThrough = model.TitleIsStrikeThrough;
        TitleIsTextWrappingEnabled = model.TitleIsTextWrappingEnabled;
        TitleAlignment = !string.IsNullOrWhiteSpace(model.TitleAlignment) ? model.TitleAlignment : "Center";
        TitleVerticalPosition = !string.IsNullOrWhiteSpace(model.TitleVerticalPosition) ? model.TitleVerticalPosition : "Center";
        TitleOffsetX = model.TitleOffsetX;
        TitleOffsetY = model.TitleOffsetY;

        SubtitleFontFamily = !string.IsNullOrWhiteSpace(model.SubtitleFontFamily) ? model.SubtitleFontFamily : (string.IsNullOrWhiteSpace(model.FontFamily) ? "Segoe UI" : model.FontFamily);
        SubtitleFontSize = model.SubtitleFontSize;
        SubtitleIsBold = model.SubtitleIsBold;
        SubtitleIsItalic = model.SubtitleIsItalic;
        SubtitleIsStrikeThrough = model.SubtitleIsStrikeThrough;
        SubtitleIsTextWrappingEnabled = model.SubtitleIsTextWrappingEnabled;
        SubtitleAlignment = !string.IsNullOrWhiteSpace(model.SubtitleAlignment) ? model.SubtitleAlignment : "Center";
        SubtitleVerticalPosition = !string.IsNullOrWhiteSpace(model.SubtitleVerticalPosition) ? model.SubtitleVerticalPosition : "Bottom";
        SubtitleOffsetX = model.SubtitleOffsetX;
        SubtitleOffsetY = model.SubtitleOffsetY;
        StreamDeckPluginUuid = isPlugin ? (model.StreamDeckPluginUuid ?? "") : "";
        StreamDeckActionUuid = isPlugin ? (model.StreamDeckActionUuid ?? "") : "";
        StreamDeckSettingsJson = isPlugin ? (model.StreamDeckSettingsJson ?? "") : "";
    }
}
