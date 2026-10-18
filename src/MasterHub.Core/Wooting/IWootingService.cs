using System;
using System.Collections.Generic;

namespace MasterHub.Core.Wooting;

public interface IWootingService
{
    /// <summary>
    /// Czy wykryto podłączoną klawiaturę Wooting przez interfejs kontrolny HID (0x1337).
    /// </summary>
    bool IsDeviceConnected { get; }

    /// <summary>
    /// Nazwa podłączonego modelu Wooting (np. "Wooting 60HE / 60HE+").
    /// </summary>
    string ConnectedDeviceName { get; }

    /// <summary>
    /// Ścieżka systemowego interfejsu kontrolnego HID klawiatury Wooting.
    /// </summary>
    string DevicePath { get; }

    /// <summary>
    /// Aktualny indeks profilu klawiatury (0 = Cyfrowy, 1 = Analogowy 1, 2 = Analogowy 2, 3 = Analogowy 3).
    /// </summary>
    int CurrentProfileIndex { get; }

    /// <summary>
    /// Nazwa aktualnie wybranego profilu.
    /// </summary>
    string CurrentProfileName { get; }

    /// <summary>
    /// Lista nazw 4 slotów profilowych odczytanych z pamięci klawiatury Wooting.
    /// </summary>
    IReadOnlyList<string> ProfileNames { get; }

    /// <summary>
    /// Liczba aktywnie skonfigurowanych profili w pamięci klawiatury (1..4).
    /// </summary>
    int ConfiguredProfileCount { get; }

    /// <summary>
    /// Zwraca nazwę profilu dla podanego indeksu (0..3).
    /// </summary>
    string GetProfileName(int profileIndex);

    /// <summary>
    /// Przełącza sprzętowy profil klawiatury Wooting (0 = Digital, 1..3 = Analog).
    /// </summary>
    bool SwitchProfile(int profileIndex);

    /// <summary>
    /// Przełącza profil w górę (+1) lub w dół (-1).
    /// </summary>
    int CycleProfile(int direction);

    /// <summary>
    /// Przełącza cyklicznie na kolejny profil klawiatury.
    /// </summary>
    int CycleProfileNext();

    /// <summary>
    /// Ponownie skanuje system w poszukiwaniu urządzeń Wooting i odświeża stan.
    /// </summary>
    bool RefreshDevice();

    event Action<int>? ProfileChanged;
    event Action<string>? LogMessage;
}
