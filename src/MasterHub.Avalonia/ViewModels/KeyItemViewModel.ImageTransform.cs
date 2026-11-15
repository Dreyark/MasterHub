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

    public string CurrentEditImageFileName => SelectedEditState == 1 ? ActiveImageFileName : ImageFileName;
    public bool HasCurrentEditImage => SelectedEditState == 1 ? HasActiveImage : HasCustomImage;

    public string ImageFitMode
    {
        get => _imageFitMode;
        set
        {
            if (SetField(ref _imageFitMode, value))
            {
                OnPropertyChanged(nameof(CurrentEditImageFitMode));
                OnPropertyChanged(nameof(IsFitCover));
                OnPropertyChanged(nameof(IsFitContain));
                OnPropertyChanged(nameof(IsFitStretch));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public int ImageScale
    {
        get => _imageScale;
        set
        {
            int clamped = Math.Clamp(value, 20, 400);
            if (SetField(ref _imageScale, clamped))
            {
                OnPropertyChanged(nameof(CurrentEditImageScale));
                OnPropertyChanged(nameof(CurrentEditImageScaleDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public int ImageOffsetX
    {
        get => _imageOffsetX;
        set
        {
            int clamped = Math.Clamp(value, -100, 100);
            if (SetField(ref _imageOffsetX, clamped))
            {
                OnPropertyChanged(nameof(CurrentEditImageOffsetX));
                OnPropertyChanged(nameof(CurrentEditImageOffsetXDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public int ImageOffsetY
    {
        get => _imageOffsetY;
        set
        {
            int clamped = Math.Clamp(value, -100, 100);
            if (SetField(ref _imageOffsetY, clamped))
            {
                OnPropertyChanged(nameof(CurrentEditImageOffsetY));
                OnPropertyChanged(nameof(CurrentEditImageOffsetYDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string ActiveImageFitMode
    {
        get => _activeImageFitMode;
        set
        {
            if (SetField(ref _activeImageFitMode, value))
            {
                OnPropertyChanged(nameof(CurrentEditImageFitMode));
                OnPropertyChanged(nameof(IsFitCover));
                OnPropertyChanged(nameof(IsFitContain));
                OnPropertyChanged(nameof(IsFitStretch));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public int ActiveImageScale
    {
        get => _activeImageScale;
        set
        {
            int clamped = Math.Clamp(value, 20, 400);
            if (SetField(ref _activeImageScale, clamped))
            {
                OnPropertyChanged(nameof(CurrentEditImageScale));
                OnPropertyChanged(nameof(CurrentEditImageScaleDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public int ActiveImageOffsetX
    {
        get => _activeImageOffsetX;
        set
        {
            int clamped = Math.Clamp(value, -100, 100);
            if (SetField(ref _activeImageOffsetX, clamped))
            {
                OnPropertyChanged(nameof(CurrentEditImageOffsetX));
                OnPropertyChanged(nameof(CurrentEditImageOffsetXDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public int ActiveImageOffsetY
    {
        get => _activeImageOffsetY;
        set
        {
            int clamped = Math.Clamp(value, -100, 100);
            if (SetField(ref _activeImageOffsetY, clamped))
            {
                OnPropertyChanged(nameof(CurrentEditImageOffsetY));
                OnPropertyChanged(nameof(CurrentEditImageOffsetYDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string CurrentEditImageFitMode
    {
        get => SelectedEditState == 1 ? ActiveImageFitMode : ImageFitMode;
        set
        {
            if (SelectedEditState == 1)
                ActiveImageFitMode = value;
            else
                ImageFitMode = value;

            OnPropertyChanged(nameof(CurrentEditImageFitMode));
            OnPropertyChanged(nameof(IsFitCover));
            OnPropertyChanged(nameof(IsFitContain));
            OnPropertyChanged(nameof(IsFitStretch));
        }
    }

    public bool IsFitCover => string.Equals(CurrentEditImageFitMode, "Cover", StringComparison.OrdinalIgnoreCase);
    public bool IsFitContain => string.Equals(CurrentEditImageFitMode, "Contain", StringComparison.OrdinalIgnoreCase);
    public bool IsFitStretch => string.Equals(CurrentEditImageFitMode, "Stretch", StringComparison.OrdinalIgnoreCase);

    public int CurrentEditImageScale
    {
        get => SelectedEditState == 1 ? ActiveImageScale : ImageScale;
        set
        {
            int clamped = Math.Clamp(value, 20, 400);
            if (SelectedEditState == 1)
            {
                if (ActiveImageScale != clamped)
                {
                    ActiveImageScale = clamped;
                    OnPropertyChanged(nameof(CurrentEditImageScale));
                    OnPropertyChanged(nameof(CurrentEditImageScaleDisplay));
                }
            }
            else
            {
                if (ImageScale != clamped)
                {
                    ImageScale = clamped;
                    OnPropertyChanged(nameof(CurrentEditImageScale));
                    OnPropertyChanged(nameof(CurrentEditImageScaleDisplay));
                }
            }
        }
    }

    public string CurrentEditImageScaleDisplay => $"{CurrentEditImageScale}%";

    public int CurrentEditImageOffsetX
    {
        get => SelectedEditState == 1 ? ActiveImageOffsetX : ImageOffsetX;
        set
        {
            int clamped = Math.Clamp(value, -100, 100);
            if (SelectedEditState == 1)
            {
                if (ActiveImageOffsetX != clamped)
                {
                    ActiveImageOffsetX = clamped;
                    OnPropertyChanged(nameof(CurrentEditImageOffsetX));
                    OnPropertyChanged(nameof(CurrentEditImageOffsetXDisplay));
                }
            }
            else
            {
                if (ImageOffsetX != clamped)
                {
                    ImageOffsetX = clamped;
                    OnPropertyChanged(nameof(CurrentEditImageOffsetX));
                    OnPropertyChanged(nameof(CurrentEditImageOffsetXDisplay));
                }
            }
        }
    }

    public string CurrentEditImageOffsetXDisplay => CurrentEditImageOffsetX > 0 ? $"+{CurrentEditImageOffsetX}%" : $"{CurrentEditImageOffsetX}%";

    public int CurrentEditImageOffsetY
    {
        get => SelectedEditState == 1 ? ActiveImageOffsetY : ImageOffsetY;
        set
        {
            int clamped = Math.Clamp(value, -100, 100);
            if (SelectedEditState == 1)
            {
                if (ActiveImageOffsetY != clamped)
                {
                    ActiveImageOffsetY = clamped;
                    OnPropertyChanged(nameof(CurrentEditImageOffsetY));
                    OnPropertyChanged(nameof(CurrentEditImageOffsetYDisplay));
                }
            }
            else
            {
                if (ImageOffsetY != clamped)
                {
                    ImageOffsetY = clamped;
                    OnPropertyChanged(nameof(CurrentEditImageOffsetY));
                    OnPropertyChanged(nameof(CurrentEditImageOffsetYDisplay));
                }
            }
        }
    }

    public string CurrentEditImageOffsetYDisplay => CurrentEditImageOffsetY > 0 ? $"+{CurrentEditImageOffsetY}%" : $"{CurrentEditImageOffsetY}%";

    [RelayCommand]
    public void SetFitMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return;
        CurrentEditImageFitMode = mode;
    }

    [RelayCommand]
    public void ZoomIn()
    {
        CurrentEditImageScale = Math.Min(400, CurrentEditImageScale + 10);
    }

    [RelayCommand]
    public void ZoomOut()
    {
        CurrentEditImageScale = Math.Max(20, CurrentEditImageScale - 10);
    }

    [RelayCommand]
    public void Pan(string? direction)
    {
        if (string.IsNullOrWhiteSpace(direction)) return;
        switch (direction.ToLowerInvariant())
        {
            case "up":
                CurrentEditImageOffsetY = Math.Min(100, CurrentEditImageOffsetY + 5);
                break;
            case "down":
                CurrentEditImageOffsetY = Math.Max(-100, CurrentEditImageOffsetY - 5);
                break;
            case "left":
                CurrentEditImageOffsetX = Math.Max(-100, CurrentEditImageOffsetX - 5);
                break;
            case "right":
                CurrentEditImageOffsetX = Math.Min(100, CurrentEditImageOffsetX + 5);
                break;
        }
    }

    [RelayCommand]
    public void ResetImageTransform()
    {
        if (SelectedEditState == 1)
        {
            ActiveImageFitMode = "Cover";
            ActiveImageScale = 100;
            ActiveImageOffsetX = 0;
            ActiveImageOffsetY = 0;
        }
        else
        {
            ImageFitMode = "Cover";
            ImageScale = 100;
            ImageOffsetX = 0;
            ImageOffsetY = 0;
        }
        NotifyCurrentEditProperties();
    }

    private Bitmap? _renderedBitmap;
    private byte[]? _renderedJpegBytes;
    private byte[]? _pluginDynamicImageBytes;

    public byte[]? RenderedJpegBytes => _renderedJpegBytes;
    public byte[]? PluginDynamicImageBytes => _pluginDynamicImageBytes;
    public bool IsStreamDeckAlert { get; set; }
    public bool IsStreamDeckOk { get; set; }

    public void SetPluginDynamicImage(byte[]? bytes)
    {
        _pluginDynamicImageBytes = bytes;
    }

    /// <summary>T-31 d: the state of a multi-state plugin action (0, 1, 2 …) and that state's manifest picture.</summary>
    public int PluginStateIndex { get; set; }

    /// <summary>The picture of <see cref="PluginStateIndex"/> from the action's manifest; used when the plugin sent none of its own.</summary>
    public string? PluginStateImagePath { get; set; }

    public void SetRenderedJpeg(byte[]? jpeg)
    {
        _renderedJpegBytes = jpeg;
        _renderedBitmap?.Dispose();
        _renderedBitmap = null;
        if (jpeg != null && jpeg.Length >= 4 && ((jpeg[0] == 0xFF && jpeg[1] == 0xD8) || (jpeg[0] == 0x89 && jpeg[1] == 0x50)))
        {
            try
            {
                using var ms = new MemoryStream(jpeg);
                _renderedBitmap = new Bitmap(ms);
            }
            catch
            {
                _renderedBitmap = null;
            }
        }
        OnPropertyChanged(nameof(CurrentDisplayBitmap));
        OnPropertyChanged(nameof(CachedBitmap));
        OnPropertyChanged(nameof(HasAnyImage));
    }

    public Bitmap? CurrentDisplayBitmap
    {
        get
        {
            if (_renderedBitmap != null)
            {
                return _renderedBitmap;
            }
            if ((IsActiveToggleState || IsPressed) && HasActiveImage)
            {
                return ActiveImageBitmap;
            }
            if (HasCustomImage)
            {
                return CustomImageBitmap;
            }
            return null;
        }
    }
}
