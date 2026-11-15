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

    public bool IsPressed
    {
        get => _isPressed;
        set
        {
            if (SetField(ref _isPressed, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(CurrentDisplayBitmap));
                OnPropertyChanged(nameof(HasAnyImage));
            }
        }
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string CustomTitle
    {
        get => _customTitle;
        set
        {
            if (SetField(ref _customTitle, value))
            {
                OnPropertyChanged(nameof(CurrentEditTitle));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(HasSubtitle));
                OnPropertyChanged(nameof(HasAction));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string CustomSubtitle
    {
        get => _customSubtitle;
        set
        {
            if (SetField(ref _customSubtitle, value))
            {
                OnPropertyChanged(nameof(CurrentEditSubtitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                OnPropertyChanged(nameof(HasSubtitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    private string? _liveValueText;
    public string? LiveValueText
    {
        get => _liveValueText;
        set
        {
            if (SetField(ref _liveValueText, value))
            {
                OnPropertyChanged(nameof(DisplaySubtitle));
                OnPropertyChanged(nameof(HasSubtitle));
                OnPropertyChanged(nameof(SubtitleWatermark));
            }
        }
    }

    public string BackgroundColorHex
    {
        get => _backgroundColorHex;
        set
        {
            if (SetField(ref _backgroundColorHex, value))
            {
                OnPropertyChanged(nameof(BackgroundColorBrush));
                OnPropertyChanged(nameof(BackgroundColor));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public IBrush BackgroundColorBrush
    {
        get
        {
            try
            {
                if (Color.TryParse(_backgroundColorHex, out var col))
                {
                    return new SolidColorBrush(col);
                }
            }
            catch { }
            return new SolidColorBrush(Color.FromRgb(0, 0, 0));
        }
    }

    public Color BackgroundColor
    {
        get => Color.TryParse(_backgroundColorHex, out var c) ? c : Color.FromRgb(0, 0, 0);
        set => BackgroundColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public string TextColorHex
    {
        get => _textColorHex;
        set
        {
            if (SetField(ref _textColorHex, value))
            {
                OnPropertyChanged(nameof(TextColorBrush));
                OnPropertyChanged(nameof(TitleColorBrush));
                OnPropertyChanged(nameof(TitleColorHex));
                OnPropertyChanged(nameof(TitleColor));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public IBrush TextColorBrush
    {
        get
        {
            try
            {
                if (Color.TryParse(_textColorHex, out var col))
                {
                    return new SolidColorBrush(col);
                }
            }
            catch { }
            return new SolidColorBrush(Colors.White);
        }
    }

    public string TitleColorHex
    {
        get => TextColorHex;
        set => TextColorHex = value;
    }

    public IBrush TitleColorBrush => TextColorBrush;

    public Color TitleColor
    {
        get => Color.TryParse(_textColorHex, out var c) ? c : Colors.White;
        set => TextColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public string SubtitleColorHex
    {
        get => _subtitleColorHex;
        set
        {
            if (SetField(ref _subtitleColorHex, value))
            {
                OnPropertyChanged(nameof(SubtitleColorBrush));
                OnPropertyChanged(nameof(SubtitleColor));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public IBrush SubtitleColorBrush
    {
        get
        {
            try
            {
                if (Color.TryParse(_subtitleColorHex, out var col))
                {
                    return new SolidColorBrush(col);
                }
            }
            catch { }
            return new SolidColorBrush(Color.FromRgb(161, 161, 170));
        }
    }

    public Color SubtitleColor
    {
        get => Color.TryParse(_subtitleColorHex, out var c) ? c : Color.FromRgb(161, 161, 170);
        set => SubtitleColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public static Bitmap? LoadMediaBitmap(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            if (MediaValidationService.IsAnimatedOrVideo(path))
            {
                byte[]? firstFrameJpeg = MediaFrameExtractor.GetFirstFrameJpeg(path);
                if (firstFrameJpeg != null && firstFrameJpeg.Length > 0)
                {
                    using var ms = new MemoryStream(firstFrameJpeg);
                    return new Bitmap(ms);
                }
            }
            else if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                byte[]? pngBytes = SvgRasterizer.RasterizeSvgToPngBytes(path, 144, 144);
                if (pngBytes != null && pngBytes.Length > 0)
                {
                    using var ms = new MemoryStream(pngBytes);
                    return new Bitmap(ms);
                }
            }

            try
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, 144);
            }
            catch
            {
                return new Bitmap(path);
            }
        }
        catch
        {
            return null;
        }
    }

    public string ImagePath
    {
        get => _imagePath;
        set
        {
            if (SetField(ref _imagePath, value))
            {
                SetRenderedJpeg(null);
                _cachedBitmap?.Dispose();
                _cachedBitmap = LoadMediaBitmap(_imagePath);

                OnPropertyChanged(nameof(HasCustomImage));
                OnPropertyChanged(nameof(ImageFileName));
                OnPropertyChanged(nameof(CustomImageBitmap));
                OnPropertyChanged(nameof(CurrentDisplayBitmap));
                OnPropertyChanged(nameof(HasAnyImage));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool HasCustomImage => !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath);
    public string ImageFileName => HasCustomImage ? Path.GetFileName(ImagePath) : "Brak grafiki";
    public Bitmap? CustomImageBitmap => _cachedBitmap;

    public string ActiveImagePath
    {
        get => _activeImagePath;
        set
        {
            if (SetField(ref _activeImagePath, value))
            {
                SetRenderedJpeg(null);
                _cachedActiveBitmap?.Dispose();
                _cachedActiveBitmap = LoadMediaBitmap(_activeImagePath);

                OnPropertyChanged(nameof(HasActiveImage));
                OnPropertyChanged(nameof(ActiveImageFileName));
                OnPropertyChanged(nameof(ActiveImageBitmap));
                OnPropertyChanged(nameof(CurrentDisplayBitmap));
                OnPropertyChanged(nameof(HasAnyImage));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool HasActiveImage => !string.IsNullOrWhiteSpace(ActiveImagePath) && File.Exists(ActiveImagePath);
    public string ActiveImageFileName => HasActiveImage ? Path.GetFileName(ActiveImagePath) : "Brak grafiki";
    public Bitmap? ActiveImageBitmap => _cachedActiveBitmap;

    private string? _mediaValidationMessage;
    public string? MediaValidationMessage
    {
        get => _mediaValidationMessage;
        set
        {
            if (SetField(ref _mediaValidationMessage, value))
            {
                OnPropertyChanged(nameof(HasMediaValidationMessage));
            }
        }
    }

    public bool HasMediaValidationMessage => !string.IsNullOrEmpty(_mediaValidationMessage);

    public bool ApplyMediaFile(string? path, bool isActiveState)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            MediaValidationMessage = null;
            if (isActiveState) ActiveImagePath = "";
            else ImagePath = "";
            return true;
        }

        var result = MediaValidationService.Instance.Validate(path);
        if (!result.IsValid)
        {
            string errKey = result.ErrorKey ?? "Media_Error_CorruptOrUnsupported";
            string template = LocalizationService.Instance.GetString(errKey, errKey);
            try
            {
                MediaValidationMessage = result.ErrorArgs != null && result.ErrorArgs.Length > 0
                    ? string.Format(template, result.ErrorArgs)
                    : template;
            }
            catch (FormatException)
            {
                MediaValidationMessage = template;
            }
            return false;
        }

        MediaValidationMessage = null;
        // A new picture starts filling the whole key: the fit, zoom and offset of the previous picture (e.g. "Contain" left by a
        // library icon) would otherwise leave black bars around a photo.
        if (isActiveState)
        {
            ActiveImageFitMode = "Cover";
            ActiveImageScale = 100;
            ActiveImageOffsetX = 0;
            ActiveImageOffsetY = 0;
            ActiveImagePath = MasterHub.Avalonia.Services.MediaPaths.ToStore(path);
        }
        else
        {
            ImageFitMode = "Cover";
            ImageScale = 100;
            ImageOffsetX = 0;
            ImageOffsetY = 0;
            ImagePath = MasterHub.Avalonia.Services.MediaPaths.ToStore(path);
        }
        return true;
    }

    public bool IsActiveToggleState
    {
        get => _isActiveToggleState;
        set
        {
            if (SetField(ref _isActiveToggleState, value))
            {
                OnPropertyChanged(nameof(CurrentDisplayBitmap));
                OnPropertyChanged(nameof(HasAnyImage));
                OnPropertyChanged(nameof(ToggleStateLabel));
                OnPropertyChanged(nameof(ActionIcon));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                NotifyCurrentEditProperties();
            }
        }
    }

    public string ToggleStateLabel => IsActiveToggleState ? "Stan 2 (Aktywny)" : "Stan 1 (Domyślny)";

    public string ActiveCustomTitle
    {
        get => _activeCustomTitle;
        set
        {
            if (SetField(ref _activeCustomTitle, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(CurrentEditTitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string ActiveCustomSubtitle
    {
        get => _activeCustomSubtitle;
        set
        {
            if (SetField(ref _activeCustomSubtitle, value))
            {
                OnPropertyChanged(nameof(DisplaySubtitle));
                OnPropertyChanged(nameof(CurrentEditSubtitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string ActiveBackgroundColorHex
    {
        get => _activeBackgroundColorHex;
        set
        {
            if (SetField(ref _activeBackgroundColorHex, value))
            {
                OnPropertyChanged(nameof(ActiveBackgroundColor));
                OnPropertyChanged(nameof(CurrentEditBgColor));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public Color ActiveBackgroundColor
    {
        get => Color.TryParse(_activeBackgroundColorHex, out var c) ? c : Color.FromRgb(31, 22, 51);
        set => ActiveBackgroundColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public string ActiveTextColorHex
    {
        get => _activeTextColorHex;
        set
        {
            if (SetField(ref _activeTextColorHex, value))
            {
                OnPropertyChanged(nameof(ActiveTitleColor));
                OnPropertyChanged(nameof(CurrentEditTitleColor));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public Color ActiveTitleColor
    {
        get => Color.TryParse(_activeTextColorHex, out var c) ? c : Color.FromRgb(0, 255, 204);
        set => ActiveTextColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public string ActiveSubtitleColorHex
    {
        get => _activeSubtitleColorHex;
        set
        {
            if (SetField(ref _activeSubtitleColorHex, value))
            {
                OnPropertyChanged(nameof(ActiveSubtitleColor));
                OnPropertyChanged(nameof(CurrentEditSubtitleColor));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public Color ActiveSubtitleColor
    {
        get => Color.TryParse(_activeSubtitleColorHex, out var c) ? c : Color.FromRgb(255, 255, 255);
        set => ActiveSubtitleColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    public int SelectedEditState
    {
        get => _selectedEditState;
        set
        {
            if (SetField(ref _selectedEditState, value))
            {
                OnPropertyChanged(nameof(IsEditStateDefault));
                OnPropertyChanged(nameof(IsEditStateActive));
                NotifyCurrentEditProperties();
                IsActiveToggleState = (value == 1);
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool IsEditStateDefault => SelectedEditState == 0;
    public bool IsEditStateActive => SelectedEditState == 1;

    [RelayCommand]
    public void SetEditState(string? stateStr)
    {
        if (int.TryParse(stateStr, out int st))
        {
            SelectedEditState = st;
        }
    }

    public void NotifyCurrentEditProperties()
    {
        OnPropertyChanged(nameof(CurrentEditTitle));
        OnPropertyChanged(nameof(CurrentEditSubtitle));
        OnPropertyChanged(nameof(CurrentEditBgColor));
        OnPropertyChanged(nameof(CurrentEditTitleColor));
        OnPropertyChanged(nameof(CurrentEditSubtitleColor));
        OnPropertyChanged(nameof(CurrentEditImageFileName));
        OnPropertyChanged(nameof(HasCurrentEditImage));
        OnPropertyChanged(nameof(CurrentEditImageFitMode));
        OnPropertyChanged(nameof(IsFitCover));
        OnPropertyChanged(nameof(IsFitContain));
        OnPropertyChanged(nameof(IsFitStretch));
        OnPropertyChanged(nameof(CurrentEditImageScale));
        OnPropertyChanged(nameof(CurrentEditImageScaleDisplay));
        OnPropertyChanged(nameof(CurrentEditImageOffsetX));
        OnPropertyChanged(nameof(CurrentEditImageOffsetY));
        OnPropertyChanged(nameof(CurrentEditImageOffsetXDisplay));
        OnPropertyChanged(nameof(CurrentEditImageOffsetYDisplay));
        OnPropertyChanged(nameof(TitleFontFamily));
        OnPropertyChanged(nameof(SelectedTitleFontFamily));
        OnPropertyChanged(nameof(TitleFontSize));
        OnPropertyChanged(nameof(TitleFontSizeLabel));
        OnPropertyChanged(nameof(TitleIsBold));
        OnPropertyChanged(nameof(TitleIsItalic));
        OnPropertyChanged(nameof(TitleIsStrikeThrough));
        OnPropertyChanged(nameof(TitleIsTextWrappingEnabled));
        OnPropertyChanged(nameof(TitleAlignment));
        OnPropertyChanged(nameof(IsTitleAlignLeft));
        OnPropertyChanged(nameof(IsTitleAlignCenter));
        OnPropertyChanged(nameof(IsTitleAlignRight));
        OnPropertyChanged(nameof(TitleVerticalPosition));
        OnPropertyChanged(nameof(IsTitleVerticalTop));
        OnPropertyChanged(nameof(IsTitleVerticalCenter));
        OnPropertyChanged(nameof(IsTitleVerticalBottom));
        OnPropertyChanged(nameof(TitleOffsetX));
        OnPropertyChanged(nameof(TitleOffsetXDisplay));
        OnPropertyChanged(nameof(TitleOffsetY));
        OnPropertyChanged(nameof(TitleOffsetYDisplay));

        OnPropertyChanged(nameof(SubtitleFontFamily));
        OnPropertyChanged(nameof(SelectedSubtitleFontFamily));
        OnPropertyChanged(nameof(SubtitleFontSize));
        OnPropertyChanged(nameof(SubtitleFontSizeLabel));
        OnPropertyChanged(nameof(SubtitleIsBold));
        OnPropertyChanged(nameof(SubtitleIsItalic));
        OnPropertyChanged(nameof(SubtitleIsStrikeThrough));
        OnPropertyChanged(nameof(SubtitleIsTextWrappingEnabled));
        OnPropertyChanged(nameof(SubtitleAlignment));
        OnPropertyChanged(nameof(IsSubtitleAlignLeft));
        OnPropertyChanged(nameof(IsSubtitleAlignCenter));
        OnPropertyChanged(nameof(IsSubtitleAlignRight));
        OnPropertyChanged(nameof(SubtitleVerticalPosition));
        OnPropertyChanged(nameof(IsSubtitleVerticalTop));
        OnPropertyChanged(nameof(IsSubtitleVerticalCenter));
        OnPropertyChanged(nameof(IsSubtitleVerticalBottom));
        OnPropertyChanged(nameof(SubtitleOffsetX));
        OnPropertyChanged(nameof(SubtitleOffsetXDisplay));
        OnPropertyChanged(nameof(SubtitleOffsetY));
        OnPropertyChanged(nameof(SubtitleOffsetYDisplay));
    }

    public string CurrentEditTitle
    {
        get => SelectedEditState == 1 ? ActiveCustomTitle : CustomTitle;
        set
        {
            if (SelectedEditState == 1)
                ActiveCustomTitle = value;
            else
                CustomTitle = value;
            OnPropertyChanged(nameof(CurrentEditTitle));
        }
    }

    public string CurrentEditSubtitle
    {
        get => SelectedEditState == 1 ? ActiveCustomSubtitle : CustomSubtitle;
        set
        {
            if (SelectedEditState == 1)
                ActiveCustomSubtitle = value;
            else
                CustomSubtitle = value;
            OnPropertyChanged(nameof(CurrentEditSubtitle));
        }
    }

    public Color CurrentEditBgColor
    {
        get => SelectedEditState == 1 ? ActiveBackgroundColor : BackgroundColor;
        set
        {
            if (SelectedEditState == 1)
                ActiveBackgroundColor = value;
            else
                BackgroundColor = value;
            OnPropertyChanged(nameof(CurrentEditBgColor));
        }
    }

    public Color CurrentEditTitleColor
    {
        get => SelectedEditState == 1 ? ActiveTitleColor : TitleColor;
        set
        {
            if (SelectedEditState == 1)
                ActiveTitleColor = value;
            else
                TitleColor = value;
            OnPropertyChanged(nameof(CurrentEditTitleColor));
        }
    }

    public Color CurrentEditSubtitleColor
    {
        get => SelectedEditState == 1 ? ActiveSubtitleColor : SubtitleColor;
        set
        {
            if (SelectedEditState == 1)
                ActiveSubtitleColor = value;
            else
                SubtitleColor = value;
            OnPropertyChanged(nameof(CurrentEditSubtitleColor));
        }
    }
}
