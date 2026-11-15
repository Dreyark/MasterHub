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

public partial class KeyItemViewModel : ObservableObject, IDisposable
{
    private static T? GetService<T>() where T : class
    {
        try { return App.Services?.GetService<T>(); }
        catch (ObjectDisposedException) { return null; }
    }
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        return SetProperty(ref field, value, propertyName);
    }
    private bool _isPressed;
    private bool _isSelected;
    private string _title = "";
    private string _customTitle = "";
    private string _customSubtitle = "";
    private string _backgroundColorHex = "#000000";
    private string _textColorHex = "#FFFFFF";
    private string _subtitleColorHex = "#A1A1AA";
    private string _imagePath = "";
    private string _activeImagePath = "";
    private string _activeCustomTitle = "";
    private string _activeCustomSubtitle = "";
    private string _activeBackgroundColorHex = "";
    private string _activeTextColorHex = "";
    private string _activeSubtitleColorHex = "";
    private string _imageFitMode = "Cover";
    private int _imageScale = 100;
    private int _imageOffsetX;
    private int _imageOffsetY;
    private string _activeImageFitMode = "Cover";
    private int _activeImageScale = 100;
    private int _activeImageOffsetX;
    private int _activeImageOffsetY;
    private int _selectedEditState; // 0 = Stan 1 (Wyłączony/Domyślny), 1 = Stan 2 (Włączony/Aktywny)
    private string _fontFamily = "Segoe UI";
    private int _fontSize = 0; // 0 = Auto
    private bool _isBold = true;
    private bool _isItalic;
    private bool _isStrikeThrough;

    // Typografia i pozycja tytułu
    private string _titleFontFamily = "Segoe UI";
    private int _titleFontSize = 0;
    private bool _titleIsBold = true;
    private bool _titleIsItalic;
    private bool _titleIsStrikeThrough;
    private bool _titleIsTextWrappingEnabled = true;
    private string _titleAlignment = "Center";
    private string _titleVerticalPosition = "Center";
    private int _titleOffsetX = 0;
    private int _titleOffsetY = 0;

    // Typografia i pozycja podtytułu
    private string _subtitleFontFamily = "Segoe UI";
    private int _subtitleFontSize = 0;
    private bool _subtitleIsBold;
    private bool _subtitleIsItalic;
    private bool _subtitleIsStrikeThrough;
    private bool _subtitleIsTextWrappingEnabled = true;
    private string _subtitleAlignment = "Center";
    private string _subtitleVerticalPosition = "Bottom";
    private int _subtitleOffsetX = 0;
    private int _subtitleOffsetY = 0;
    private DeckActionType _assignedAction = DeckActionType.None;
    private KeyPressMode _pressMode = KeyPressMode.OnPress;
    private string _actionParameter = "";
    private string _obsSceneName = "";
    private Bitmap? _cachedBitmap;
    private Bitmap? _cachedActiveBitmap;
    private bool _isActiveToggleState;
    private string _streamDeckPluginUuid = "";
    private string _streamDeckActionUuid = "";
    private string _streamDeckSettingsJson = "";

    [ObservableProperty]
    private bool _isPanoramicWallpaperActive;

    [ObservableProperty]
    private string? _panoramicWallpaperPath;

    [ObservableProperty]
    private string _panoramicWallpaperFitMode = "Cover";

    /// <summary>Zoom (20–400 %) and move (−100…100 %) of the whole-deck wallpaper. The canvas owns them; these mirror them for the inspector.</summary>
    [ObservableProperty]
    private int _panoramicWallpaperScale = 100;

    [ObservableProperty]
    private int _panoramicWallpaperOffsetX;

    [ObservableProperty]
    private int _panoramicWallpaperOffsetY;

    private bool _syncingPanoramic;

    /// <summary>Raised when the user moves a wallpaper slider: <c>(scale, offsetX, offsetY)</c>.</summary>
    public Action<int, int, int>? PanoramicTransformRequested { get; set; }

    /// <summary>Takes over the canvas's wallpaper state without asking the canvas to redraw (it is the canvas that changed).</summary>
    public void SetPanoramicState(bool active, string? path, string fitMode, int scale, int offsetX, int offsetY)
    {
        _syncingPanoramic = true;
        try
        {
            IsPanoramicWallpaperActive = active;
            PanoramicWallpaperPath = path;
            PanoramicWallpaperFitMode = fitMode;
            PanoramicWallpaperScale = scale;
            PanoramicWallpaperOffsetX = offsetX;
            PanoramicWallpaperOffsetY = offsetY;
        }
        finally
        {
            _syncingPanoramic = false;
        }
    }

    public string PanoramicScaleLabel => $"{PanoramicWallpaperScale}%";
    public string PanoramicOffsetXLabel => $"{PanoramicWallpaperOffsetX:+0;-0;0}%";
    public string PanoramicOffsetYLabel => $"{PanoramicWallpaperOffsetY:+0;-0;0}%";

    private void OnPanoramicTransformEdited()
    {
        OnPropertyChanged(nameof(PanoramicScaleLabel));
        OnPropertyChanged(nameof(PanoramicOffsetXLabel));
        OnPropertyChanged(nameof(PanoramicOffsetYLabel));
        if (!_syncingPanoramic && IsPanoramicWallpaperActive)
            PanoramicTransformRequested?.Invoke(PanoramicWallpaperScale, PanoramicWallpaperOffsetX, PanoramicWallpaperOffsetY);
    }

    partial void OnPanoramicWallpaperScaleChanged(int value) => OnPanoramicTransformEdited();
    partial void OnPanoramicWallpaperOffsetXChanged(int value) => OnPanoramicTransformEdited();
    partial void OnPanoramicWallpaperOffsetYChanged(int value) => OnPanoramicTransformEdited();

    /// <summary>Back to the middle at the size that fits.</summary>
    [RelayCommand]
    private void ResetPanoramicTransform()
    {
        PanoramicTransformRequested?.Invoke(100, 0, 0);
        _syncingPanoramic = true;
        try { PanoramicWallpaperScale = 100; PanoramicWallpaperOffsetX = 0; PanoramicWallpaperOffsetY = 0; }
        finally { _syncingPanoramic = false; }
    }

    public string PanoramicFileName => string.IsNullOrEmpty(PanoramicWallpaperPath)
        ? LocalizationService.Instance.GetString("Panoramic_NoFile", "Brak tapety")
        : Path.GetFileName(PanoramicWallpaperPath);

    public Action<string, string>? ApplyPanoramicWallpaperRequested { get; set; }
    public Action? ClearPanoramicWallpaperRequested { get; set; }

    public void RequestApplyPanoramicWallpaper(string path, string fitMode = "Cover")
    {
        ApplyPanoramicWallpaperRequested?.Invoke(path, fitMode);
    }

    public void RequestClearPanoramicWallpaper()
    {
        ClearPanoramicWallpaperRequested?.Invoke();
    }

    public bool IsPanoramicFitCover => string.Equals(PanoramicWallpaperFitMode, "Cover", StringComparison.OrdinalIgnoreCase);
    public bool IsPanoramicFitContain => string.Equals(PanoramicWallpaperFitMode, "Contain", StringComparison.OrdinalIgnoreCase);
    public bool IsPanoramicFitStretch => string.Equals(PanoramicWallpaperFitMode, "Stretch", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    public void SetPanoramicFitMode(string fitMode)
    {
        PanoramicWallpaperFitMode = fitMode;
        OnPropertyChanged(nameof(IsPanoramicFitCover));
        OnPropertyChanged(nameof(IsPanoramicFitContain));
        OnPropertyChanged(nameof(IsPanoramicFitStretch));
        if (IsPanoramicWallpaperActive && !string.IsNullOrWhiteSpace(PanoramicWallpaperPath))
        {
            RequestApplyPanoramicWallpaper(PanoramicWallpaperPath, fitMode);
        }
    }

    partial void OnPanoramicWallpaperPathChanged(string? value)
    {
        OnPropertyChanged(nameof(PanoramicFileName));
    }

    partial void OnPanoramicWallpaperFitModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsPanoramicFitCover));
        OnPropertyChanged(nameof(IsPanoramicFitContain));
        OnPropertyChanged(nameof(IsPanoramicFitStretch));
        if (!_syncingPanoramic && IsPanoramicWallpaperActive && !string.IsNullOrWhiteSpace(PanoramicWallpaperPath))
        {
            RequestApplyPanoramicWallpaper(PanoramicWallpaperPath, value);
        }
    }

    public int Index { get; set; }
    public int Number => Index + 1;
    public string KeyNumberDisplay => string.Format(LocalizationService.Instance.GetString("Key_NumberFormat", "Klawisz #{0}"), Number);
    public Action<KeyItemViewModel>? ActionChangedCallback { get; set; }

    public KeyItemViewModel()
    {
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        var vmService = GetService<IVoicemodService>();
        if (vmService != null)
        {
            vmService.ConnectionStateChanged += OnVoicemodConnectionChanged;
            vmService.VoicesLoaded += OnVoicemodVoicesLoaded;
            vmService.SoundsLoaded += OnVoicemodSoundsLoaded;
        }

        var haService = GetService<IHomeAssistantService>();
        if (haService != null)
        {
            haService.ConnectionStateChanged += OnHomeAssistantConnectionChanged;
            haService.EntitiesUpdated += OnHomeAssistantEntitiesUpdated;
            haService.EntityStateChanged += OnHomeAssistantEntityStateChanged;
        }
    }

    public void Dispose()
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        var vmService = GetService<IVoicemodService>();
        if (vmService != null)
        {
            vmService.ConnectionStateChanged -= OnVoicemodConnectionChanged;
            vmService.VoicesLoaded -= OnVoicemodVoicesLoaded;
            vmService.SoundsLoaded -= OnVoicemodSoundsLoaded;
        }

        var haService = GetService<IHomeAssistantService>();
        if (haService != null)
        {
            haService.ConnectionStateChanged -= OnHomeAssistantConnectionChanged;
            haService.EntitiesUpdated -= OnHomeAssistantEntitiesUpdated;
            haService.EntityStateChanged -= OnHomeAssistantEntityStateChanged;
        }

        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
        _cachedActiveBitmap?.Dispose();
        _cachedActiveBitmap = null;
        _renderedBitmap?.Dispose();
        _renderedBitmap = null;
    }

    private void OnHomeAssistantConnectionChanged(object? sender, bool connected)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsHomeAssistantConnected));
            OnPropertyChanged(nameof(HomeAssistantConnectionStatusDisplay));
        });
    }

    private void OnHomeAssistantEntitiesUpdated(object? sender, IReadOnlyList<HomeAssistantEntityModel> entities)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(AvailableHomeAssistantEntities));
            OnPropertyChanged(nameof(SelectedHomeAssistantEntity));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(DisplaySubtitle));
        });
    }

    private void OnHomeAssistantEntityStateChanged(object? sender, HomeAssistantEntityModel entity)
    {
        if (IsHomeAssistantAction && !string.IsNullOrWhiteSpace(ActionParameter) &&
            string.Equals(entity.EntityId, ActionParameter, StringComparison.OrdinalIgnoreCase))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                ActionChangedCallback?.Invoke(this);
            });
        }
    }

    private void OnVoicemodConnectionChanged(bool connected)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsVoicemodConnected));
            OnPropertyChanged(nameof(VoicemodConnectionStatusDisplay));
        });
    }

    private void OnVoicemodVoicesLoaded(IReadOnlyList<VoicemodVoiceItem> voices)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(AvailableVoicemodVoices));
            OnPropertyChanged(nameof(SelectedVoicemodVoice));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(DisplaySubtitle));
        });
    }

    private void OnVoicemodSoundsLoaded(IReadOnlyList<VoicemodSoundItem> sounds)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(AvailableVoicemodSounds));
            OnPropertyChanged(nameof(SelectedVoicemodSound));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(DisplaySubtitle));
        });
    }

    private void OnLanguageChanged(string lang)
    {
        OnPropertyChanged(nameof(KeyNumberDisplay));
        OnPropertyChanged(nameof(ActionDisplayName));
        OnPropertyChanged(nameof(ActionCategory));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(DisplaySubtitle));
        OnPropertyChanged(nameof(PressModeDisplay));
        OnPropertyChanged(nameof(AvailablePressModes));
        OnPropertyChanged(nameof(SelectedPressModeOption));
        OnPropertyChanged(nameof(StateModeDisplay));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ActionParameterLabel));
        OnPropertyChanged(nameof(ActionParameterWatermark));
        OnPropertyChanged(nameof(SubtitleWatermark));
        OnPropertyChanged(nameof(PanoramicFileName));
        ActionChangedCallback?.Invoke(this);
    }
}
