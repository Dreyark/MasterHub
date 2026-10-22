using System;
using System.Collections.Generic;
using MasterHub.Core.Models;

namespace MasterHub.Core.Devices;

/// <summary>
/// Primary hardware management service interface for Cooler Master MasterHub devices.
/// Handles USB discovery, hot-plugging, input event polling, and module state.
/// </summary>
public interface IMasterHubManager : IDisposable
{
    // Module connection states
    bool IsKeypadConnected { get; }
    bool IsSliderConnected { get; }
    bool IsRollerConnected { get; }
    bool IsBaseConnected { get; }
    bool IsKnobConnected { get; }
    bool IsDialConnected { get; }

    // Standby & Power
    bool IsStandby { get; }
    byte CurrentBrightness { get; }

    // Device inventory & input states
    List<DeviceInfo> ConnectedDevices { get; }
    IReadOnlyList<bool> CurrentKeyStates { get; }
    IReadOnlyList<byte> CurrentSliderValues { get; }
    Func<int, bool, byte[]>? CustomKeyImageProvider { get; set; }

    // Hardware Orientation & Rotation
    DeckOrientation HardwareBaseOrientation { get; }
    DeckRotationAngle EffectiveRotationAngle { get; }
    DeckRotationMode RotationMode { get; set; }
    int ManualRotationAngle { get; set; }

    // Events
    event Action<int, bool>? KeyStateChanged;               // keyIndex 0..14, isPressed
    event Action<int, byte, int>? SliderValueChanged;       // sliderIndex 0..4, raw 0..255, percent 0..100
    event Action<int, int>? RollerRotated;                  // rollerIndex 0=Left, 1=Right; direction +1=Up, -1=Down
    event Action<int, int>? KnobRotated;                    // knobIndex 0..2, delta +1=CW, -1=CCW
    event Action<int, bool>? KnobPressed;                   // knobIndex 0..2, isPressed
    event Action<int>? DialRotated;                         // delta +1=CW, -1=CCW
    event Action<bool>? DialPressed;                        // isPressed
    event Action<DeckOrientation>? BaseOrientationChanged;  // sprzętowa zmiana orientacji podstawki
    event Action<DeckRotationAngle>? EffectiveRotationChanged; // zmiana efektywnego kąta obrotu decka
    event Action<string>? LogMessage;
    event Action<List<DeviceInfo>>? DevicesDiscovered;
    event Action<bool>? StandbyStateChanged;

    // Control methods
    void Start();
    void DiscoverAndConnect();
    void SyncConnectedDevices();
    void EnterStandby();
    void WakeUp();
    void SetBacklight(byte brightness);
    void SetKeypadBrightness(byte brightness);
    void SetLedColor(byte r, byte g, byte b);
    void SetSliderLedColor(int sliderIndex, byte r, byte g, byte b);
    void SetSliderLedBrightness(int sliderIndex, byte brightness);
    void SetBaseLedColor(byte r, byte g, byte b);
    void SetBaseLedBrightness(byte brightness);
    void SetRollerLedColor(int rollerIndex, byte r, byte g, byte b);
    void SetRollerLedBrightness(int rollerIndex, byte brightness);
    void SetKnobLedColor(int knobIndex, byte r, byte g, byte b);
    void SetKnobLedBrightness(int knobIndex, byte brightness);
    void SetKnobLedMode(int knobIndex, MasterHub.Core.Profiles.KnobLedMode mode);
    void SetDialBrightness(byte brightness);
    void SetDialColor(byte r, byte g, byte b);
    void SetDialImage(byte[] jpeg);
    System.Threading.Tasks.Task<bool> SetDialImageAsync(byte[] imageBytes);
    void UpdateKeyDisplay(int keyIndex, bool isPressed);
    void SetKeyImage(int keyIndex, byte[] jpeg);
    void SetPanoramicWallpaper(byte[] jpeg);
    System.Threading.Tasks.Task<bool> SetPanoramicWallpaperAsync(byte[] imageBytes);
    void InitializeKeypadDisplay();
}
