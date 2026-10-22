using System;
using System.Collections.Generic;
using Avalonia.Threading;
using MasterHub.Core.Devices;
using MasterHub.Core.Models;

namespace MasterHub.Avalonia.Services;

/// <summary>
/// Implementacja serwisu sprzętowego z automatycznym marshalingiem zdarzeń na wątek UI Avalonia.
/// </summary>
public class MasterHubDeviceService : IMasterHubDeviceService
{
    private readonly IMasterHubManager _manager;
    private bool _disposed;

    public bool IsKeypadConnected => _manager.IsKeypadConnected;
    public bool IsSliderConnected => _manager.IsSliderConnected;
    public bool IsRollerConnected => _manager.IsRollerConnected;
    public bool IsBaseConnected => _manager.IsBaseConnected;
    public bool IsKnobConnected => _manager.IsKnobConnected;
    public bool IsDialConnected => _manager.IsDialConnected;
    public bool IsStandby => _manager.IsStandby;
    public byte CurrentBrightness => _manager.CurrentBrightness;

    public DeckOrientation HardwareBaseOrientation => _manager.HardwareBaseOrientation;
    public DeckRotationAngle EffectiveRotationAngle => _manager.EffectiveRotationAngle;
    public DeckRotationMode RotationMode
    {
        get => _manager.RotationMode;
        set => _manager.RotationMode = value;
    }
    public int ManualRotationAngle
    {
        get => _manager.ManualRotationAngle;
        set => _manager.ManualRotationAngle = value;
    }

    public IReadOnlyList<DeviceInfo> ConnectedDevices => _manager.ConnectedDevices;

    public event Action<int, bool>? KeyStateChanged;
    public event Action<int, byte, int>? SliderValueChanged;
    public event Action<int, int>? RollerRotated;
    public event Action<int, int>? KnobRotated;
    public event Action<int, bool>? KnobPressed;
    public event Action<int>? DialRotated;
    public event Action<bool>? DialPressed;
    public event Action<DeckOrientation>? BaseOrientationChanged;
    public event Action<DeckRotationAngle>? EffectiveRotationChanged;
    public event Action<bool>? StandbyStateChanged;
    public event Action? DevicesUpdated;

    public MasterHubDeviceService(IMasterHubManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));

        _manager.KeyStateChanged += OnKeyStateChanged;
        _manager.SliderValueChanged += OnSliderValueChanged;
        _manager.RollerRotated += OnRollerRotated;
        _manager.KnobRotated += OnKnobRotated;
        _manager.KnobPressed += OnKnobPressed;
        _manager.DialRotated += OnDialRotated;
        _manager.DialPressed += OnDialPressed;
        _manager.BaseOrientationChanged += OnBaseOrientationChanged;
        _manager.EffectiveRotationChanged += OnEffectiveRotationChanged;
        _manager.StandbyStateChanged += OnStandbyStateChanged;
        _manager.DevicesDiscovered += OnDevicesDiscovered;
    }

    public void Initialize()
    {
        _manager.Start();
    }

    private void OnKeyStateChanged(int keyIndex, bool isPressed)
    {
        Dispatcher.UIThread.Post(() => KeyStateChanged?.Invoke(keyIndex, isPressed));
    }

    private void OnSliderValueChanged(int sliderIndex, byte rawValue, int percent)
    {
        Dispatcher.UIThread.Post(() => SliderValueChanged?.Invoke(sliderIndex, rawValue, percent));
    }

    private void OnRollerRotated(int rollerIndex, int direction)
    {
        Dispatcher.UIThread.Post(() => RollerRotated?.Invoke(rollerIndex, direction));
    }

    private void OnKnobRotated(int knobIndex, int delta)
    {
        Dispatcher.UIThread.Post(() => KnobRotated?.Invoke(knobIndex, delta));
    }

    private void OnKnobPressed(int knobIndex, bool isPressed)
    {
        Dispatcher.UIThread.Post(() => KnobPressed?.Invoke(knobIndex, isPressed));
    }

    private void OnDialRotated(int delta)
    {
        Dispatcher.UIThread.Post(() => DialRotated?.Invoke(delta));
    }

    private void OnDialPressed(bool isPressed)
    {
        Dispatcher.UIThread.Post(() => DialPressed?.Invoke(isPressed));
    }

    private void OnBaseOrientationChanged(DeckOrientation orientation)
    {
        Dispatcher.UIThread.Post(() => BaseOrientationChanged?.Invoke(orientation));
    }

    private void OnEffectiveRotationChanged(DeckRotationAngle angle)
    {
        Dispatcher.UIThread.Post(() => EffectiveRotationChanged?.Invoke(angle));
    }

    private void OnStandbyStateChanged(bool isStandby)
    {
        Dispatcher.UIThread.Post(() => StandbyStateChanged?.Invoke(isStandby));
    }

    private void OnDevicesDiscovered(List<DeviceInfo> devices)
    {
        Dispatcher.UIThread.Post(() => DevicesUpdated?.Invoke());
    }

    public void SetBrightness(byte brightness)
    {
        _manager.SetBacklight(brightness);
        _manager.SetKeypadBrightness(brightness);
    }

    public void SetKeypadBacklight(byte brightness)
    {
        _manager.SetKeypadBrightness(brightness);
    }

    public void ToggleStandby()
    {
        if (_manager.IsStandby)
        {
            _manager.WakeUp();
        }
        else
        {
            _manager.EnterStandby();
        }
    }

    public void SetKeyImage(int keyIndex, byte[] jpegData)
    {
        _manager.SetKeyImage(keyIndex, jpegData);
    }

    public void SetPanoramicWallpaper(byte[] jpegData)
    {
        _manager.SetPanoramicWallpaper(jpegData);
    }

    public System.Threading.Tasks.Task<bool> SetPanoramicWallpaperAsync(byte[] imageBytes)
    {
        return _manager.SetPanoramicWallpaperAsync(imageBytes);
    }

    public void SetSliderLedColor(int sliderIndex, byte r, byte g, byte b)
    {
        _manager.SetSliderLedColor(sliderIndex, r, g, b);
    }

    public void SetBaseLedColor(byte r, byte g, byte b)
    {
        _manager.SetBaseLedColor(r, g, b);
    }

    public void SetRollerLedColor(int rollerIndex, byte r, byte g, byte b)
    {
        _manager.SetRollerLedColor(rollerIndex, r, g, b);
    }

    public void SetKnobLedColor(int knobIndex, byte r, byte g, byte b)
    {
        _manager.SetKnobLedColor(knobIndex, r, g, b);
    }

    public void SetKnobLedBrightness(int knobIndex, byte brightness)
    {
        _manager.SetKnobLedBrightness(knobIndex, brightness);
    }

    public void SetKnobLedMode(int knobIndex, MasterHub.Core.Profiles.KnobLedMode mode)
    {
        _manager.SetKnobLedMode(knobIndex, mode);
    }

    public void SetDialBrightness(byte brightness)
    {
        _manager.SetDialBrightness(brightness);
    }

    public void SetDialColor(byte r, byte g, byte b)
    {
        _manager.SetDialColor(r, g, b);
    }

    public void SetDialImage(byte[] jpeg)
    {
        _manager.SetDialImage(jpeg);
    }

    public System.Threading.Tasks.Task<bool> SetDialImageAsync(byte[] imageBytes)
    {
        return _manager.SetDialImageAsync(imageBytes);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _manager.KeyStateChanged -= OnKeyStateChanged;
        _manager.SliderValueChanged -= OnSliderValueChanged;
        _manager.RollerRotated -= OnRollerRotated;
        _manager.KnobRotated -= OnKnobRotated;
        _manager.KnobPressed -= OnKnobPressed;
        _manager.DialRotated -= OnDialRotated;
        _manager.DialPressed -= OnDialPressed;
        _manager.StandbyStateChanged -= OnStandbyStateChanged;
        _manager.DevicesDiscovered -= OnDevicesDiscovered;
        _manager.Dispose();
    }
}
