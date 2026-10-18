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

    // Standby & Power
    bool IsStandby { get; }
    byte CurrentBrightness { get; }

    // Device inventory & input states
    List<DeviceInfo> ConnectedDevices { get; }
    IReadOnlyList<bool> CurrentKeyStates { get; }
    IReadOnlyList<byte> CurrentSliderValues { get; }
    Func<int, bool, byte[]>? CustomKeyImageProvider { get; set; }

    // Events
    event Action<int, bool>? KeyStateChanged;               // keyIndex 0..14, isPressed
    event Action<int, byte, int>? SliderValueChanged;       // sliderIndex 0..4, raw 0..255, percent 0..100
    event Action<int, int>? RollerRotated;                  // rollerIndex 0=Left, 1=Right; direction +1=Up, -1=Down
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
    void UpdateKeyDisplay(int keyIndex, bool isPressed);
    void SetKeyImage(int keyIndex, byte[] jpeg);
    void InitializeKeypadDisplay();
}
