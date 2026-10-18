using System;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Actions;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels;

public partial class RollerItemViewModel : ObservableObject
{
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        return SetProperty(ref field, value, propertyName);
    }
    private int _steps;
    private string _lastDirection = "—";
    private bool _isSelected;
    private bool _isActivityActive;
    private RollerActionType _assignedAction = RollerActionType.None;
    private string _colorHex = "#A78BFA";
    private byte _brightness = 100;

    public int Index { get; set; }
    public int Number => Index + 1;
    public string Name => string.Format(LocalizationService.Instance.GetString("Roller_NumberFormat", "Rolka {0}"), Number);
    public string LastDirectionDisplay => string.Format(LocalizationService.Instance.GetString("Roller_LastDirection", "Ostatni ruch: {0}"), LastDirection);
    public Action<RollerItemViewModel>? ActionChangedCallback { get; set; }
    public Action<RollerItemViewModel, string>? ColorChangedCallback { get; set; }
    public Action<RollerItemViewModel, byte>? BrightnessChangedCallback { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
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

    public static RollerActionType[] AvailableActions => new[]
    {
        RollerActionType.None,
        RollerActionType.MasterVolume,
        RollerActionType.ScrollVertical,
        RollerActionType.ScrollHorizontal,
        RollerActionType.MediaTrack,
        RollerActionType.RazerDpiCycle,
        RollerActionType.RazerProfileCycle,
        RollerActionType.WootingProfileCycle
    };

    public void ResetSteps()
    {
        Steps = 0;
        LastDirection = "—";
    }

    public bool HasAction => _assignedAction != RollerActionType.None;

    [RelayCommand]
    public void ClearAction()
    {
        AssignedAction = RollerActionType.None;
    }

    public RollerActionType AssignedAction
    {
        get => _assignedAction;
        set
        {
            if (SetField(ref _assignedAction, value))
            {
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(HasAction));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public RollerItemViewModel()
    {
        LocalizationService.Instance.LanguageChanged += _ => NotifyLanguageChanged();
    }

    public string ActionDisplayName
    {
        get
        {
            var loc = LocalizationService.Instance;
            return AssignedAction switch
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
                _ => AssignedAction.ToString()
            };
        }
    }

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ActionDisplayName));
        OnPropertyChanged(nameof(LastDirectionDisplay));
    }

    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (SetField(ref _colorHex, value))
            {
                OnPropertyChanged(nameof(ColorBrush));
                ColorChangedCallback?.Invoke(this, value);
            }
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
            return new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA));
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
