using System;
using System.Collections.Generic;

namespace MasterHub.Core.Razer;

/// <summary>
/// Działania klawiaturowe wysyłane do oficjalnej aplikacji Razer Synapse (Metoda 1: Global Shortcuts).
/// </summary>
public enum RazerShortcutAction
{
    NextProfile,    // Następny profil (Ctrl+Alt+F13)
    PrevProfile,    // Poprzedni profil (Ctrl+Alt+F14)
    Profile1,       // Profil 1 (Ctrl+Alt+F15)
    Profile2,       // Profil 2 (Ctrl+Alt+F16)
    Profile3,       // Profil 3 (Ctrl+Alt+F17)
    Profile4,       // Profil 4 (Ctrl+Alt+F18)
    Profile5,       // Profil 5 (Ctrl+Alt+F19)
    DpiUp,          // Zwiększ DPI (Ctrl+Alt+F20)
    DpiDown,        // Zmniejsz DPI (Ctrl+Alt+F21)
    DpiCycle        // Cykl DPI (Ctrl+Alt+F22)
}

/// <summary>
/// Model urządzenia Razer pobrany przez SynapseCTRL API (Metoda 2).
/// </summary>
public class RazerDeviceModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DeviceType { get; set; } = "Mouse";
    public List<RazerProfileModel> Profiles { get; set; } = new();
}

/// <summary>
/// Model profilu Razer pobrany przez SynapseCTRL API (Metoda 2).
/// </summary>
public class RazerProfileModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public bool IsActive { get; set; }
}
