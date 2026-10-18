using System;
using System.Collections.Generic;

namespace MasterHub.Core.Razer;

public interface IRazerService
{
    /// <summary>
    /// Czy wykryto podłączoną mysz Razer przez bezpośredni interfejs HID (MI_00).
    /// </summary>
    bool IsDeviceConnected { get; }

    /// <summary>
    /// Nazwa podłączonego urządzenia (np. "Razer Viper Mini Signature Edition").
    /// </summary>
    string ConnectedDeviceName { get; }

    /// <summary>
    /// Ścieżka systemowego interfejsu HID urządzenia Razer.
    /// </summary>
    string DevicePath { get; }

    /// <summary>
    /// Aktualne sprzętowe DPI sensora myszy (np. 800, 1600).
    /// </summary>
    int CurrentDpi { get; }

    /// <summary>
    /// Indeks aktualnego etapu DPI (0..4).
    /// </summary>
    int CurrentStageIndex { get; }

    /// <summary>
    /// Zdefiniowane etapy DPI myszki (np. 400, 800, 1600, 3200, 6400).
    /// </summary>
    IReadOnlyList<int> DpiStages { get; }

    /// <summary>
    /// Numer aktualnego profilu sprzętowego myszy w pamięci wbudowanej (1..5).
    /// </summary>
    int CurrentProfileIndex { get; }

    /// <summary>
    /// Aktualna częstotliwość próbkowania sensora myszy w Hz (np. 1000, 4000, 8000).
    /// </summary>
    int CurrentPollingRate { get; }

    /// <summary>
    /// Obsługiwane częstotliwości próbkowania Hz (125, 500, 1000, 2000, 4000, 8000).
    /// </summary>
    IReadOnlyList<int> SupportedPollingRates { get; }

    /// <summary>
    /// Aktualny stan naładowania baterii myszki w procentach (0..100%).
    /// </summary>
    int BatteryPercentage { get; }

    /// <summary>
    /// Czy mysz jest aktualnie podłączona do ładowania.
    /// </summary>
    bool IsCharging { get; }

    /// <summary>
    /// Tekstowa reprezentacja stanu baterii (np. "🔋 85%" lub "⚡ Ładowanie 85%").
    /// </summary>
    string BatteryStatusText { get; }

    /// <summary>
    /// Bezpośrednio ustawia sprzętowe DPI sensora myszy Razer (raport 90-bajtowy HID).
    /// </summary>
    bool SetDpi(int dpi);

    /// <summary>
    /// Przełącza etap DPI w górę (+1) lub w dół (-1) z listy DpiStages.
    /// </summary>
    int CycleDpi(int direction);

    /// <summary>
    /// Przełącza cyklicznie na kolejny etap DPI (zapętlone).
    /// </summary>
    int CycleDpiNext();

    /// <summary>
    /// Przełącza profil sprzętowy myszy w pamięci urządzenia (1..5).
    /// </summary>
    bool SwitchProfile(int profileIndex);

    /// <summary>
    /// Przełącza profil sprzętowy w górę (+1) lub w dół (-1).
    /// </summary>
    int CycleProfile(int direction);

    /// <summary>
    /// Ustawia częstotliwość próbkowania sensora w Hz (125, 500, 1000, 2000, 4000, 8000).
    /// </summary>
    bool SetPollingRate(int hz);

    /// <summary>
    /// Przełącza częstotliwość próbkowania w górę (+1) lub w dół (-1).
    /// </summary>
    int CyclePollingRate(int direction);

    /// <summary>
    /// Przełącza cyklicznie na kolejną częstotliwość próbkowania (zapętlone).
    /// </summary>
    int CyclePollingRateNext();

    /// <summary>
    /// Ponownie skanuje system w poszukiwaniu urządzeń Razer i aktualizuje stan.
    /// </summary>
    bool RefreshDevice();

    /// <summary>
    /// Kompatybilność wsteczna z wywołaniami akcji skrótów.
    /// </summary>
    void SendShortcut(RazerShortcutAction action);

    event Action<int, int>? DpiChanged;
    event Action<int>? ProfileChanged;
    event Action<int>? PollingRateChanged;
    event Action<int, bool>? BatteryChanged;
    event Action<string>? LogMessage;
}
