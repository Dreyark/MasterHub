using System;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>
/// Model reprezentujący stację bazową (podstawkę Cooler Master MasterHUB Base M0064).
/// Odpowiada za kontrolę sprzętowej listwy podświetlenia RGB LED docka.
/// </summary>
public partial class BaseStationItemViewModel : ObservableObject
{
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        return SetProperty(ref field, value, propertyName);
    }
    private bool _isSelected;
    private bool _isConnected = true;
    private string _colorHex = "#00F0B5";
    private byte _brightness = 100;

    public string Name => LocalizationService.Instance.GetString("BaseStation_Name", "Podstawka MasterHub (Dock Base)");
    public string ModelName => "MasterHUB Base M0064";

    public BaseStationItemViewModel()
    {
        LocalizationService.Instance.LanguageChanged += _ => OnPropertyChanged(nameof(Name));
    }
    public static System.Collections.Generic.IReadOnlyList<MasterHub.Core.Rendering.ColorStudioHelper.ColorItem> PresetColors =>
        MasterHub.Core.Rendering.ColorStudioHelper.CuratedPalette;

    public void SelectColor(string hex)
    {
        ColorHex = hex;
        if (Brightness == 0 && hex != "#000000")
        {
            Brightness = 100;
        }
    }

    [RelayCommand]
    public void TurnOffLed()
    {
        Brightness = 0;
        ColorHex = "#000000";
    }

    public Action<BaseStationItemViewModel, string>? ColorChangedCallback { get; set; }
    public Action<BaseStationItemViewModel, byte>? BrightnessChangedCallback { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => SetField(ref _isConnected, value);
    }

    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (SetField(ref _colorHex, value))
            {
                OnPropertyChanged(nameof(ColorBrush));
                OnPropertyChanged(nameof(GlowBrush));
                OnPropertyChanged(nameof(Color));
                if (Brightness == 0 && value != "#000000" && Color.TryParse(value, out var c) && (c.R > 0 || c.G > 0 || c.B > 0))
                {
                    Brightness = 100;
                }
                ColorChangedCallback?.Invoke(this, value);
            }
        }
    }

    public Color Color
    {
        get => Color.TryParse(ColorHex, out var c) && (c.R > 0 || c.G > 0 || c.B > 0)
            ? c
            : Color.FromRgb(0, 240, 181);
        set
        {
            ColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            if (Brightness == 0 && (value.R > 0 || value.G > 0 || value.B > 0))
            {
                Brightness = 100;
            }
        }
    }

    public IBrush ColorBrush
    {
        get
        {
            if (Color.TryParse(ColorHex, out var col))
            {
                return new SolidColorBrush(col);
            }
            return new SolidColorBrush(Color.FromRgb(0, 0, 0));
        }
    }

    public IBrush GlowBrush
    {
        get
        {
            if (Color.TryParse(ColorHex, out var col))
            {
                // Kolor z przezroczystością do poświaty LED
                return new SolidColorBrush(Color.FromArgb(120, col.R, col.G, col.B));
            }
            return new SolidColorBrush(Color.FromArgb(120, 0, 255, 204));
        }
    }

    public byte Brightness
    {
        get => _brightness;
        set
        {
            if (SetField(ref _brightness, value))
            {
                OnPropertyChanged(nameof(BrightnessDisplay));
                BrightnessChangedCallback?.Invoke(this, value);
            }
        }
    }

    public string BrightnessDisplay => $"{Brightness}%";
}
