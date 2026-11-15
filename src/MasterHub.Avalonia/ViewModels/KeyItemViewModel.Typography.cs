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

    public Bitmap? CachedBitmap => CurrentDisplayBitmap;

    public bool HasAnyImage => CurrentDisplayBitmap != null;

    private FontFamily _cachedFontFamily = new("Segoe UI");

    public string FontFamily
    {
        get => _fontFamily;
        set
        {
            if (SetField(ref _fontFamily, value))
            {
                _cachedFontFamily = new FontFamily(!string.IsNullOrWhiteSpace(_fontFamily) ? _fontFamily : "Segoe UI");
                OnPropertyChanged(nameof(SelectedFontFamily));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public FontFamily SelectedFontFamily => _cachedFontFamily;

    public int FontSize
    {
        get => _fontSize;
        set
        {
            int normalized = (value > 0 && value < 6) ? 6 : Math.Clamp(value, 0, 96);
            if (SetField(ref _fontSize, normalized))
            {
                OnPropertyChanged(nameof(FontSizeLabel));
                OnPropertyChanged(nameof(DisplayFontSize));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string FontSizeLabel => FontSize > 0 ? $"{FontSize} pt" : "Auto";

    public bool IsBold
    {
        get => _isBold;
        set
        {
            if (SetField(ref _isBold, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool IsItalic
    {
        get => _isItalic;
        set
        {
            if (SetField(ref _isItalic, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool IsStrikeThrough
    {
        get => _isStrikeThrough;
        set
        {
            if (SetField(ref _isStrikeThrough, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    private bool _isTextWrappingEnabled = true;
    public bool IsTextWrappingEnabled
    {
        get => _isTextWrappingEnabled;
        set
        {
            if (SetField(ref _isTextWrappingEnabled, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public double DisplayFontSize
    {
        get
        {
            if (_fontSize > 0) return _fontSize;
            string text = DisplayTitle;
            if (text.Contains('\n'))
            {
                var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                return lines.Length >= 3 ? 9.5 : 11.5;
            }
            return text.Length <= 3 ? 16.0 : (text.Length <= 6 ? 12.0 : 10.0);
        }
    }

    // --- TYPOGRAFIA I POZYCJONOWANIE TYTUŁU ---
    private FontFamily _cachedTitleFontFamily = new("Segoe UI");

    public string TitleFontFamily
    {
        get => _titleFontFamily;
        set
        {
            if (SetField(ref _titleFontFamily, value))
            {
                _cachedTitleFontFamily = new FontFamily(!string.IsNullOrWhiteSpace(_titleFontFamily) ? _titleFontFamily : "Segoe UI");
                OnPropertyChanged(nameof(SelectedTitleFontFamily));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public FontFamily SelectedTitleFontFamily => _cachedTitleFontFamily;

    public int TitleFontSize
    {
        get => _titleFontSize;
        set
        {
            int normalized = (value > 0 && value < 6) ? 6 : Math.Clamp(value, 0, 96);
            if (SetField(ref _titleFontSize, normalized))
            {
                OnPropertyChanged(nameof(TitleFontSizeLabel));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string TitleFontSizeLabel => TitleFontSize > 0 ? $"{TitleFontSize} pt" : "Auto";

    public bool TitleIsBold
    {
        get => _titleIsBold;
        set
        {
            if (SetField(ref _titleIsBold, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool TitleIsItalic
    {
        get => _titleIsItalic;
        set
        {
            if (SetField(ref _titleIsItalic, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool TitleIsStrikeThrough
    {
        get => _titleIsStrikeThrough;
        set
        {
            if (SetField(ref _titleIsStrikeThrough, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool TitleIsTextWrappingEnabled
    {
        get => _titleIsTextWrappingEnabled;
        set
        {
            if (SetField(ref _titleIsTextWrappingEnabled, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string TitleAlignment
    {
        get => _titleAlignment;
        set
        {
            if (SetField(ref _titleAlignment, value))
            {
                OnPropertyChanged(nameof(IsTitleAlignLeft));
                OnPropertyChanged(nameof(IsTitleAlignCenter));
                OnPropertyChanged(nameof(IsTitleAlignRight));
                OnPropertyChanged(nameof(IsTitleCenterBoth));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool IsTitleAlignLeft => string.Equals(TitleAlignment, "Left", StringComparison.OrdinalIgnoreCase);
    public bool IsTitleAlignCenter => string.Equals(TitleAlignment, "Center", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(TitleAlignment);
    public bool IsTitleAlignRight => string.Equals(TitleAlignment, "Right", StringComparison.OrdinalIgnoreCase);
    public bool IsTitleCenterBoth => IsTitleAlignCenter && IsTitleVerticalCenter;

    [RelayCommand]
    public void SetTitleAlignment(string? align)
    {
        if (string.IsNullOrWhiteSpace(align)) return;
        TitleAlignment = string.Equals(TitleAlignment, align, StringComparison.OrdinalIgnoreCase) ? "Center" : align;
    }

    [RelayCommand]
    public void ResetTitlePosition()
    {
        TitleAlignment = "Center";
        TitleVerticalPosition = "Center";
        TitleOffsetX = 0;
        TitleOffsetY = 0;
    }

    public string TitleVerticalPosition
    {
        get => _titleVerticalPosition;
        set
        {
            if (SetField(ref _titleVerticalPosition, value))
            {
                OnPropertyChanged(nameof(IsTitleVerticalTop));
                OnPropertyChanged(nameof(IsTitleVerticalCenter));
                OnPropertyChanged(nameof(IsTitleVerticalBottom));
                OnPropertyChanged(nameof(IsTitleCenterBoth));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool IsTitleVerticalTop => string.Equals(TitleVerticalPosition, "Top", StringComparison.OrdinalIgnoreCase);
    public bool IsTitleVerticalCenter => string.Equals(TitleVerticalPosition, "Center", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(TitleVerticalPosition);
    public bool IsTitleVerticalBottom => string.Equals(TitleVerticalPosition, "Bottom", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    public void SetTitleVerticalPosition(string? pos)
    {
        if (string.IsNullOrWhiteSpace(pos)) return;
        TitleVerticalPosition = string.Equals(TitleVerticalPosition, pos, StringComparison.OrdinalIgnoreCase) ? "Center" : pos;
    }

    public int TitleOffsetX
    {
        get => _titleOffsetX;
        set
        {
            int clamped = Math.Clamp(value, -50, 50);
            if (SetField(ref _titleOffsetX, clamped))
            {
                OnPropertyChanged(nameof(TitleOffsetXDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string TitleOffsetXDisplay => $"{TitleOffsetX} px";

    public int TitleOffsetY
    {
        get => _titleOffsetY;
        set
        {
            int clamped = Math.Clamp(value, -50, 50);
            if (SetField(ref _titleOffsetY, clamped))
            {
                OnPropertyChanged(nameof(TitleOffsetYDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string TitleOffsetYDisplay => $"{TitleOffsetY} px";

    // --- TYPOGRAFIA I POZYCJONOWANIE PODTYTUŁU ---
    private FontFamily _cachedSubtitleFontFamily = new("Segoe UI");

    public string SubtitleFontFamily
    {
        get => _subtitleFontFamily;
        set
        {
            if (SetField(ref _subtitleFontFamily, value))
            {
                _cachedSubtitleFontFamily = new FontFamily(!string.IsNullOrWhiteSpace(_subtitleFontFamily) ? _subtitleFontFamily : "Segoe UI");
                OnPropertyChanged(nameof(SelectedSubtitleFontFamily));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public FontFamily SelectedSubtitleFontFamily => _cachedSubtitleFontFamily;

    public int SubtitleFontSize
    {
        get => _subtitleFontSize;
        set
        {
            int normalized = (value > 0 && value < 6) ? 6 : Math.Clamp(value, 0, 96);
            if (SetField(ref _subtitleFontSize, normalized))
            {
                OnPropertyChanged(nameof(SubtitleFontSizeLabel));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string SubtitleFontSizeLabel => SubtitleFontSize > 0 ? $"{SubtitleFontSize} pt" : "Auto";

    public bool SubtitleIsBold
    {
        get => _subtitleIsBold;
        set
        {
            if (SetField(ref _subtitleIsBold, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool SubtitleIsItalic
    {
        get => _subtitleIsItalic;
        set
        {
            if (SetField(ref _subtitleIsItalic, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool SubtitleIsStrikeThrough
    {
        get => _subtitleIsStrikeThrough;
        set
        {
            if (SetField(ref _subtitleIsStrikeThrough, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool SubtitleIsTextWrappingEnabled
    {
        get => _subtitleIsTextWrappingEnabled;
        set
        {
            if (SetField(ref _subtitleIsTextWrappingEnabled, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string SubtitleAlignment
    {
        get => _subtitleAlignment;
        set
        {
            if (SetField(ref _subtitleAlignment, value))
            {
                OnPropertyChanged(nameof(IsSubtitleAlignLeft));
                OnPropertyChanged(nameof(IsSubtitleAlignCenter));
                OnPropertyChanged(nameof(IsSubtitleAlignRight));
                OnPropertyChanged(nameof(IsSubtitleCenterBoth));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool IsSubtitleAlignLeft => string.Equals(SubtitleAlignment, "Left", StringComparison.OrdinalIgnoreCase);
    public bool IsSubtitleAlignCenter => string.Equals(SubtitleAlignment, "Center", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(SubtitleAlignment);
    public bool IsSubtitleAlignRight => string.Equals(SubtitleAlignment, "Right", StringComparison.OrdinalIgnoreCase);
    public bool IsSubtitleCenterBoth => IsSubtitleAlignCenter && IsSubtitleVerticalCenter;

    [RelayCommand]
    public void SetSubtitleAlignment(string? align)
    {
        if (string.IsNullOrWhiteSpace(align)) return;
        SubtitleAlignment = string.Equals(SubtitleAlignment, align, StringComparison.OrdinalIgnoreCase) ? "Center" : align;
    }

    [RelayCommand]
    public void ResetSubtitlePosition()
    {
        SubtitleAlignment = "Center";
        SubtitleVerticalPosition = "Center";
        SubtitleOffsetX = 0;
        SubtitleOffsetY = 0;
    }

    public string SubtitleVerticalPosition
    {
        get => _subtitleVerticalPosition;
        set
        {
            if (SetField(ref _subtitleVerticalPosition, value))
            {
                OnPropertyChanged(nameof(IsSubtitleVerticalTop));
                OnPropertyChanged(nameof(IsSubtitleVerticalCenter));
                OnPropertyChanged(nameof(IsSubtitleVerticalBottom));
                OnPropertyChanged(nameof(IsSubtitleCenterBoth));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool IsSubtitleVerticalTop => string.Equals(SubtitleVerticalPosition, "Top", StringComparison.OrdinalIgnoreCase);
    public bool IsSubtitleVerticalCenter => string.Equals(SubtitleVerticalPosition, "Center", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(SubtitleVerticalPosition);
    public bool IsSubtitleVerticalBottom => string.Equals(SubtitleVerticalPosition, "Bottom", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    public void SetSubtitleVerticalPosition(string? pos)
    {
        if (string.IsNullOrWhiteSpace(pos)) return;
        SubtitleVerticalPosition = string.Equals(SubtitleVerticalPosition, pos, StringComparison.OrdinalIgnoreCase) ? "Center" : pos;
    }

    public int SubtitleOffsetX
    {
        get => _subtitleOffsetX;
        set
        {
            int clamped = Math.Clamp(value, -50, 50);
            if (SetField(ref _subtitleOffsetX, clamped))
            {
                OnPropertyChanged(nameof(SubtitleOffsetXDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string SubtitleOffsetXDisplay => $"{SubtitleOffsetX} px";

    public int SubtitleOffsetY
    {
        get => _subtitleOffsetY;
        set
        {
            int clamped = Math.Clamp(value, -50, 50);
            if (SetField(ref _subtitleOffsetY, clamped))
            {
                OnPropertyChanged(nameof(SubtitleOffsetYDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string SubtitleOffsetYDisplay => $"{SubtitleOffsetY} px";

    [RelayCommand]
    public void RestoreOriginalTitle()
    {
        string orig = GetOriginalActionTitle();
        if (SelectedEditState == 1)
        {
            ActiveCustomTitle = orig;
        }
        else
        {
            CustomTitle = orig;
        }
        OnPropertyChanged(nameof(CurrentEditTitle));
        ActionChangedCallback?.Invoke(this);
    }

    [RelayCommand]
    public void RestoreOriginalSubtitle()
    {
        string orig = GetOriginalActionSubtitle();
        if (SelectedEditState == 1)
        {
            ActiveCustomSubtitle = orig;
        }
        else
        {
            CustomSubtitle = orig;
        }
        OnPropertyChanged(nameof(CurrentEditSubtitle));
        ActionChangedCallback?.Invoke(this);
    }
}
