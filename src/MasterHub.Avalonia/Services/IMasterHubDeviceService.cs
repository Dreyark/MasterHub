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
    bool IsStandby { get; }
    byte CurrentBrightness { get; }

    IReadOnlyList<DeviceInfo> ConnectedDevices { get; }

    event Action<int, bool>? KeyStateChanged;
    event Action<int, byte, int>? SliderValueChanged;
    event Action<int, int>? RollerRotated;
    event Action<bool>? StandbyStateChanged;
    event Action? DevicesUpdated;

    void Initialize();
    void SetBrightness(byte brightness);
    void SetKeypadBacklight(byte brightness);
    void ToggleStandby();
    void SetKeyImage(int keyIndex, byte[] jpegData);
    void SetSliderLedColor(int sliderIndex, byte r, byte g, byte b);
    void SetBaseLedColor(byte r, byte g, byte b);
    void SetRollerLedColor(int rollerIndex, byte r, byte g, byte b);
}
