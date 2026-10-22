using CommunityToolkit.Mvvm.ComponentModel;
using MasterHub.Avalonia.Services;

namespace MasterHub.Avalonia.ViewModels.Inspector;

/// <summary>
/// ViewModel mini-kart statusu urządzeń i suwaka jasności w nagłówku inspektora.
/// </summary>
public partial class DeviceHeaderViewModel : ObservableObject
{
    private readonly IMasterHubDeviceService _deviceService;

    [ObservableProperty]
    private bool _isKeypadConnected;

    [ObservableProperty]
    private bool _isSliderConnected;

    [ObservableProperty]
    private bool _isRollerConnected;

    [ObservableProperty]
    private bool _isBaseConnected;

    [ObservableProperty]
    private bool _isKnobConnected;

    [ObservableProperty]
    private bool _isDialConnected;

    [ObservableProperty]
    private byte _brightness;

    public string KeypadStatusColor => IsKeypadConnected ? "#00E5A3" : "#FF5252";
    public string SliderStatusColor => IsSliderConnected ? "#00E5A3" : "#FF5252";
    public string RollerStatusColor => IsRollerConnected ? "#00E5A3" : "#FF5252";
    public string BaseStatusColor => IsBaseConnected ? "#00E5A3" : "#FF5252";
    public string KnobStatusColor => IsKnobConnected ? "#00E5A3" : "#FF5252";
    public string DialStatusColor => IsDialConnected ? "#00E5A3" : "#FF5252";

    public double KeypadOpacity => IsKeypadConnected ? 1.0 : 0.45;
    public double SliderOpacity => IsSliderConnected ? 1.0 : 0.45;
    public double RollerOpacity => IsRollerConnected ? 1.0 : 0.45;
    public double BaseOpacity => IsBaseConnected ? 1.0 : 0.45;
    public double KnobOpacity => IsKnobConnected ? 1.0 : 0.45;
    public double DialOpacity => IsDialConnected ? 1.0 : 0.45;

    public int VisibleColumns => 4 + (IsKnobConnected ? 1 : 0) + (IsDialConnected ? 1 : 0);

    public DeviceHeaderViewModel(IMasterHubDeviceService deviceService)
    {
        _deviceService = deviceService;
        _brightness = _deviceService.CurrentBrightness;

        UpdateStates();
        _deviceService.DevicesUpdated += UpdateStates;
    }

    private void UpdateStates()
    {
        IsKeypadConnected = _deviceService.IsKeypadConnected;
        IsSliderConnected = _deviceService.IsSliderConnected;
        IsRollerConnected = _deviceService.IsRollerConnected;
        IsBaseConnected = _deviceService.IsBaseConnected;
        IsKnobConnected = _deviceService.IsKnobConnected;
        IsDialConnected = _deviceService.IsDialConnected;

        OnPropertyChanged(nameof(KeypadStatusColor));
        OnPropertyChanged(nameof(SliderStatusColor));
        OnPropertyChanged(nameof(RollerStatusColor));
        OnPropertyChanged(nameof(BaseStatusColor));
        OnPropertyChanged(nameof(KnobStatusColor));
        OnPropertyChanged(nameof(DialStatusColor));
        OnPropertyChanged(nameof(KeypadOpacity));
        OnPropertyChanged(nameof(SliderOpacity));
        OnPropertyChanged(nameof(RollerOpacity));
        OnPropertyChanged(nameof(BaseOpacity));
        OnPropertyChanged(nameof(KnobOpacity));
        OnPropertyChanged(nameof(DialOpacity));
        OnPropertyChanged(nameof(VisibleColumns));
    }

    partial void OnBrightnessChanged(byte value)
    {
        _deviceService.SetBrightness(value);
    }
}
