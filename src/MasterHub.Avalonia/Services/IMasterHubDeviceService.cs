using System;
using System.Collections.Generic;
using MasterHub.Core.Devices;
using MasterHub.Core.Models;

namespace MasterHub.Avalonia.Services;

/// <summary>
/// Serwis sprzętowy integrujący kontroler Cooler Master MasterHub z interfejsem Avalonia UI.
/// Odpowiada za bezpieczny marshaling zdarzeń sprzętowych na wątek UI.
/// </summary>
public interface IMasterHubDeviceService : IDisposable
{
    bool IsKeypadConnected { get; }
    bool IsSliderConnected { get; }
    bool IsRollerConnected { get; }
    bool IsBaseConnected { get; }
    bool IsKnobConnected { get; }
    bool IsDialConnected { get; }
    bool IsStandby { get; }
    byte CurrentBrightness { get; }

    DeckOrientation HardwareBaseOrientation { get; }
    DeckRotationAngle EffectiveRotationAngle { get; }
    DeckRotationMode RotationMode { get; set; }
    int ManualRotationAngle { get; set; }

    IReadOnlyList<DeviceInfo> ConnectedDevices { get; }

    event Action<int, bool>? KeyStateChanged;
    event Action<int, byte, int>? SliderValueChanged;
    event Action<int, int>? RollerRotated;
    event Action<int, int>? KnobRotated;
    event Action<int, bool>? KnobPressed;
    event Action<int>? DialRotated;
    event Action<bool>? DialPressed;
    event Action<DeckOrientation>? BaseOrientationChanged;
    event Action<DeckRotationAngle>? EffectiveRotationChanged;
    event Action<bool>? StandbyStateChanged;
    event Action? DevicesUpdated;

    void Initialize();
    void SetBrightness(byte brightness);
    void SetKeypadBacklight(byte brightness);
    void ToggleStandby();
    void SetKeyImage(int keyIndex, byte[] jpegData);
    void SetPanoramicWallpaper(byte[] jpegData);
    System.Threading.Tasks.Task<bool> SetPanoramicWallpaperAsync(byte[] imageBytes);
    void SetSliderLedColor(int sliderIndex, byte r, byte g, byte b);
    void SetBaseLedColor(byte r, byte g, byte b);
    void SetRollerLedColor(int rollerIndex, byte r, byte g, byte b);
    void SetKnobLedColor(int knobIndex, byte r, byte g, byte b);
    void SetKnobLedBrightness(int knobIndex, byte brightness);
    void SetKnobLedMode(int knobIndex, MasterHub.Core.Profiles.KnobLedMode mode);
    void SetDialBrightness(byte brightness);
    void SetDialColor(byte r, byte g, byte b);
    void SetDialImage(byte[] jpeg);
    System.Threading.Tasks.Task<bool> SetDialImageAsync(byte[] imageBytes);
}
