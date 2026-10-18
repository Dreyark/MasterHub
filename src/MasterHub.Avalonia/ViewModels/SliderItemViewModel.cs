using System;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Audio;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels;

public partial class SliderItemViewModel : ObservableObject
{
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        return SetProperty(ref field, value, propertyName);
    }
    private byte _rawValue;
    private int _percent;
    private bool _isSelected;
    private string _selectedTarget = "";
    private AudioTargetType _targetType = AudioTargetType.None;
    private string _targetProcessName = "";
    private string _colorHex = "#38BDF8";
    private byte _brightness = 100;

    public SliderItemViewModel()
    {
        LocalizationService.Instance.LanguageChanged += _ => NotifyLanguageChanged();
    }

    public int Index { get; set; }
    public int Number => Index + 1;
    public string Name => string.Format(MasterHub.Core.Localization.LocalizationService.Instance.GetString("Slider_NumberFormat", "Suwak {0}"), Number);
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

    public Action<SliderItemViewModel, string>? TargetChangedCallback { get; set; }
    public Action<SliderItemViewModel, string>? ColorChangedCallback { get; set; }
    public Action<SliderItemViewModel, byte>? BrightnessChangedCallback { get; set; }

    public ObservableCollection<string> AvailableTargets { get; } = new();

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public byte RawValue
    {
        get => _rawValue;
        set => SetField(ref _rawValue, value);
    }

    public int Percent
    {
        get => _percent;
        set
        {
            if (SetField(ref _percent, value))
            {
                OnPropertyChanged(nameof(PercentDisplay));
            }
        }
    }

    public string PercentDisplay => $"{Percent}%";

    public bool IsLedActive => _brightness > 0 &&
                               !string.IsNullOrWhiteSpace(_colorHex) &&
                               _colorHex != "#000000" &&
                               Color.TryParse(_colorHex, out var c) &&
                               (c.R > 0 || c.G > 0 || c.B > 0);

    public double GlowBlurRadius => IsLedActive ? 24.0 : 0.0;
    public double GlowOpacity => IsLedActive ? 0.85 : 0.0;
    public Color GlowColor => IsLedActive && Color.TryParse(_colorHex, out var c)
        ? c
        : Colors.Transparent;

    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (SetField(ref _colorHex, value))
            {
                OnPropertyChanged(nameof(IsLedActive));
                OnPropertyChanged(nameof(ColorBrush));
                OnPropertyChanged(nameof(MeterBrush));
                OnPropertyChanged(nameof(Color));
                OnPropertyChanged(nameof(GlowColor));
                OnPropertyChanged(nameof(GlowBlurRadius));
                OnPropertyChanged(nameof(GlowOpacity));
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
        get => IsLedActive && Color.TryParse(ColorHex, out var c)
            ? c
            : Color.FromRgb(0x43, 0x46, 0x61);
        set
        {
            ColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            if (Brightness == 0 && (value.R > 0 || value.G > 0 || value.B > 0))
            {
                Brightness = 100;
            }
        }
    }

    public byte Brightness
    {
        get => _brightness;
        set
        {
            if (SetField(ref _brightness, value))
            {
                OnPropertyChanged(nameof(IsLedActive));
                OnPropertyChanged(nameof(BrightnessDisplay));
                OnPropertyChanged(nameof(ColorBrush));
                OnPropertyChanged(nameof(MeterBrush));
                OnPropertyChanged(nameof(Color));
                OnPropertyChanged(nameof(GlowColor));
                OnPropertyChanged(nameof(GlowBlurRadius));
                OnPropertyChanged(nameof(GlowOpacity));
                BrightnessChangedCallback?.Invoke(this, value);
            }
        }
    }

    public string BrightnessDisplay => $"{Brightness}%";

    public IBrush ColorBrush
    {
        get
        {
            if (IsLedActive && Color.TryParse(_colorHex, out var c))
            {
                return new SolidColorBrush(c);
            }
            // Elegancki, ciemny matowy tor sprzętowy dla wyłączonego podświetlenia (bez jaskrawego niebieskiego)
            return new SolidColorBrush(Color.FromRgb(0x43, 0x46, 0x61));
        }
    }

    public IBrush MeterBrush
    {
        get
        {
            try
            {
                if (Color.TryParse(_colorHex, out var c))
                {
                    return new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                        GradientStops = new GradientStops
                        {
                            new GradientStop(Color.FromArgb(90, c.R, c.G, c.B), 0),
                            new GradientStop(c, 0.75),
                            new GradientStop(Colors.White, 1.0)
                        }
                    };
                }
            }
            catch { }
            return new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
        }
    }

    public string SelectedTarget
    {
        get
        {
            if (_targetType == AudioTargetType.None)
            {
                return LocalizationService.Instance.GetString("Slider_Unassigned", "⚪ Brak (Nieprzypisany)");
            }
            if (_targetType == AudioTargetType.MasterPlayback)
            {
                return LocalizationService.Instance.GetString("Fader_Master", "🔊 Główna głośność");
            }
            if (_targetType == AudioTargetType.MasterRecording)
            {
                return LocalizationService.Instance.GetString("Fader_Mic", "🎙️ Mikrofon");
            }
            return _selectedTarget;
        }
        set
        {
            if (SetField(ref _selectedTarget, value))
            {
                ParseTarget(value);
                TargetChangedCallback?.Invoke(this, value);
            }
        }
    }

    public AudioTargetType TargetType => _targetType;
    public string TargetProcessName => _targetProcessName;
    public bool HasTarget => _targetType != AudioTargetType.None;

    public void SetTarget(AudioTargetType type, string processName, string displayTarget)
    {
        _targetType = type;
        _targetProcessName = processName;
        _selectedTarget = displayTarget;
        OnPropertyChanged(nameof(SelectedTarget));
        OnPropertyChanged(nameof(TargetType));
        OnPropertyChanged(nameof(TargetProcessName));
        OnPropertyChanged(nameof(HasTarget));
        TargetChangedCallback?.Invoke(this, displayTarget);
    }

    [RelayCommand]
    public void Clear()
    {
        ClearTarget();
    }

    public void ClearTarget()
    {
        SetTarget(AudioTargetType.None, "", LocalizationService.Instance.GetString("Slider_Unassigned", "⚪ Brak (Nieprzypisany)"));
    }

    private void ParseTarget(string target)
    {
        if (target.Contains("Główna głośność") || target.Contains("Master") || target.Contains("🔊"))
        {
            _targetType = AudioTargetType.MasterPlayback;
            _targetProcessName = "";
        }
        else if (target.Contains("Mikrofon") || target.Contains("Mic") || target.Contains("🎙️"))
        {
            _targetType = AudioTargetType.MasterRecording;
            _targetProcessName = "";
        }
        else if (target.Contains("Brak") || target.Contains("None") || target.Contains("⚪") || string.IsNullOrWhiteSpace(target))
        {
            _targetType = AudioTargetType.None;
            _targetProcessName = "";
        }
        else
        {
            _targetType = AudioTargetType.Process;
            var parts = target.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            _targetProcessName = parts.Length > 1 ? parts[1].Trim() : target.Trim();
        }
        OnPropertyChanged(nameof(TargetType));
        OnPropertyChanged(nameof(TargetProcessName));
    }

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(SelectedTarget));
    }
}
