using System;

namespace MasterHub.Core.Actions;

/// <summary>
/// Silnik przetwarzania interakcji sprzętowych klawiszy (Single Press, Release, Long Press, Double Press).
/// Realizuje wzorzec Strategy/Observer dla wejść HID MasterHub (Etap 1.3).
/// </summary>
public interface IKeyInteractionEngine : IDisposable
{
    /// <summary>Czas w ms wymagany do zakwalifikowania wciśnięcia jako Long Press (domyślnie 500 ms).</summary>
    int HoldDurationMs { get; set; }

    /// <summary>Maksymalny odstęp w ms między dwoma wciśnięciami dla Double Press (domyślnie 300 ms).</summary>
    int DoublePressIntervalMs { get; set; }

    /// <summary>Opóźnienie początkowe w ms przed rozpoczęciem powtarzania dla OnHoldRepeat (domyślnie 400 ms).</summary>
    int HoldRepeatInitialDelayMs { get; set; }

    /// <summary>Interwał powtarzania w ms dla OnHoldRepeat (domyślnie 100 ms).</summary>
    int HoldRepeatIntervalMs { get; set; }

    /// <summary>Zdarzenie wywoływane gdy warunek danego trybu klawisza zostanie spełniony.</summary>
    event Action<int, KeyPressMode>? KeyTriggered;

    /// <summary>
    /// Przetwarza surowy stan klawisza (ze zdarzenia HID urządzenia).
    /// </summary>
    /// <param name="keyIndex">Indeks klawisza (0..14).</param>
    /// <param name="isPressed">Stan fizyczny klawisza (true = wciśnięty, false = zwolniony).</param>
    /// <param name="mode">Skonfigurowany tryb wyzwalania dla tego klawisza.</param>
    void HandleKeyEvent(int keyIndex, bool isPressed, KeyPressMode mode);

    /// <summary>Resetuje stany i timery wszystkich klawiszy.</summary>
    void Reset();
}
