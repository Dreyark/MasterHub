using System;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Actions;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;

namespace MasterHub.Avalonia.ViewModels;

public partial class KnobItemViewModel : ObservableObject
{
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        return SetProperty(ref field, value, propertyName);
    }

    private int _steps;
    private string _lastDirection = "—";
    private bool _isSelected;
    private bool _isActivityActive;
    private bool _isPressed;
    private RollerActionType _rotateAction = RollerActionType.None;
    private DeckActionType _pressAction = DeckActionType.None;
    private string _pressActionParameter = "";
    private KnobLedMode _ledMode = KnobLedMode.Static;
    private string _colorHex = "#00E5A3";
    private byte _brightness = 100;
    private byte _speed = 50;

    public int Index { get; set; }
    public int Number => Index + 1;
    public string Name => string.Format(LocalizationService.Instance.GetString("Knob_NumberFormat", "Pokrętło {0}"), Number);
    public string LastDirectionDisplay => string.Format(LocalizationService.Instance.GetString("Roller_LastDirection", "Ostatni ruch: {0}"), LastDirection);

    public Action<KnobItemViewModel>? ActionChangedCallback { get; set; }
    public Action<KnobItemViewModel, string>? ColorChangedCallback { get; set; }
    public Action<KnobItemViewModel, byte>? BrightnessChangedCallback { get; set; }
    public Action<KnobItemViewModel, KnobLedMode>? LedModeChangedCallback { get; set; }

    public KnobItemViewModel()
    {
        LocalizationService.Instance.LanguageChanged += _ => NotifyLanguageChanged();
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsPressed
    {
        get => _isPressed;
        set => SetField(ref _isPressed, value);
    }

    public int Steps
    {
        get => _steps;
        set
        {
            if (SetField(ref _steps, value))
            {
                OnPropertyChanged(nameof(StepsDisplay));
            }
        }
    }

    public string StepsDisplay => $"{Steps}";

    public string LastDirection
    {
        get => _lastDirection;
        set
        {
            if (SetField(ref _lastDirection, value))
            {
                OnPropertyChanged(nameof(LastDirectionDisplay));
            }
        }
    }

    public bool IsActivityActive
    {
        get => _isActivityActive;
        set => SetField(ref _isActivityActive, value);
    }

    public RollerActionType RotateAction
    {
        get => _rotateAction;
        set
        {
            if (SetField(ref _rotateAction, value))
            {
                OnPropertyChanged(nameof(RotateActionDisplayName));
                OnPropertyChanged(nameof(HasAction));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public DeckActionType PressAction
    {
        get => _pressAction;
        set
        {
            if (SetField(ref _pressAction, value))
            {
                OnPropertyChanged(nameof(PressActionDisplayName));
                OnPropertyChanged(nameof(HasAction));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string PressActionParameter
    {
        get => _pressActionParameter;
        set
        {
            if (SetField(ref _pressActionParameter, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public KnobLedMode LedMode
    {
        get => _ledMode;
        set
        {
            if (SetField(ref _ledMode, value))
            {
                OnPropertyChanged(nameof(LedModeDisplayName));
                LedModeChangedCallback?.Invoke(this, value);
            }
        }
    }

    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (SetField(ref _colorHex, value))
            {
                OnPropertyChanged(nameof(Color));
                OnPropertyChanged(nameof(ColorBrush));
                ColorChangedCallback?.Invoke(this, value);
            }
        }
    }

    public Color Color
    {
        get => Color.TryParse(_colorHex, out var c) ? c : Color.FromRgb(0x00, 0xE5, 0xA3);
        set => ColorHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
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

    public IBrush ColorBrush
    {
        get
        {
            try
            {
                if (Color.TryParse(_colorHex, out var c))
                {
                    return new SolidColorBrush(c);
                }
            }
            catch { }
            return new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xA3));
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

    public byte Speed
    {
        get => _speed;
        set => SetField(ref _speed, value);
    }

    public bool HasAction => _rotateAction != RollerActionType.None || _pressAction != DeckActionType.None;

    public string RotateActionDisplayName
    {
        get
        {
            var loc = LocalizationService.Instance;
            return RotateAction switch
            {
                RollerActionType.None => loc.GetString("Action_None"),
                RollerActionType.MasterVolume => loc.GetString("Roller_Volume"),
                RollerActionType.ScrollVertical => loc.GetString("Roller_ScrollV"),
                RollerActionType.ScrollHorizontal => loc.GetString("Roller_ScrollH"),
                RollerActionType.MediaTrack => loc.GetString("Roller_Media"),
                RollerActionType.RazerDpiCycle => loc.GetString("Roller_RazerDpi"),
                RollerActionType.RazerProfileCycle => loc.GetString("Roller_RazerProfiles"),
                RollerActionType.RazerPollingRateCycle => loc.GetString("Roller_RazerPolling"),
                RollerActionType.WootingProfileCycle => loc.GetString("Roller_WootingProfiles"),
                _ => RotateAction.ToString()
            };
        }
    }

    public string PressActionDisplayName
    {
        get
        {
            var loc = LocalizationService.Instance;
            return PressAction switch
            {
                DeckActionType.None => loc.GetString("Action_None"),
                DeckActionType.MasterMute => loc.GetString("Action_MasterMute", "Wycisz / Odcisz Głośniki"),
                DeckActionType.MicMute => loc.GetString("Action_MicMute", "Wycisz / Odcisz Mikrofon"),
                DeckActionType.MediaPlayPause => loc.GetString("Action_MediaPlayPause", "Play / Pause"),
                DeckActionType.MediaNext => loc.GetString("Action_MediaNext", "Następny utwór"),
                DeckActionType.MediaPrevious => loc.GetString("Action_MediaPrevious", "Poprzedni utwór"),
                _ => PressAction.ToString()
            };
        }
    }

    public static KnobLedMode[] AllLedModes { get; } = (KnobLedMode[])Enum.GetValues(typeof(KnobLedMode));

    public string LedModeDisplayName
    {
        get
        {
            var loc = LocalizationService.Instance;
            return LedMode switch
            {
                KnobLedMode.Disable => loc.GetString("Knob_LedMode_Disable", "Wyłączone"),
                KnobLedMode.Static => loc.GetString("Knob_LedMode_Static", "Statyczne"),
                KnobLedMode.Breathing => loc.GetString("Knob_LedMode_Breathing", "Oddychanie"),
                KnobLedMode.Strobe => loc.GetString("Knob_LedMode_Strobe", "Stroboskop"),
                KnobLedMode.Spring => loc.GetString("Knob_LedMode_Spring", "Sprężyna"),
                KnobLedMode.Cycling => loc.GetString("Knob_LedMode_Cycling", "Cykl kolorów"),
                KnobLedMode.Random => loc.GetString("Knob_LedMode_Random", "Losowe"),
                KnobLedMode.Wave => loc.GetString("Knob_LedMode_Wave", "Fala"),
                KnobLedMode.Water => loc.GetString("Knob_LedMode_Water", "Woda"),
                KnobLedMode.Rainbow => loc.GetString("Knob_LedMode_Rainbow", "Tęcza"),
                _ => LedMode.ToString()
            };
        }
    }

    [RelayCommand]
    public void ClearAction()
    {
        RotateAction = RollerActionType.None;
        PressAction = DeckActionType.None;
    }

    public void ResetSteps()
    {
        Steps = 0;
        LastDirection = "—";
    }

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(RotateActionDisplayName));
        OnPropertyChanged(nameof(PressActionDisplayName));
        OnPropertyChanged(nameof(LastDirectionDisplay));
        OnPropertyChanged(nameof(LedModeDisplayName));
    }
}
