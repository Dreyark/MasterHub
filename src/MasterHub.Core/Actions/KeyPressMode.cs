namespace MasterHub.Core.Actions;

/// <summary>
/// Tryb wyzwalania akcji przypisanej do klawisza fizycznego MasterHub.
/// Zgodny ze standardem Elgato Stream Deck (Etap 1.3).
/// </summary>
public enum KeyPressMode
{
    /// <summary>Akcja uruchamiana natychmiast w momencie wciśnięcia (domyślny tryb).</summary>
    OnPress = 0,

    /// <summary>Akcja uruchamiana dopiero po zwolnieniu (puszczeniu) klawisza.</summary>
    OnRelease = 1,

    /// <summary>Akcja uruchamiana przy przytrzymaniu klawisza (domyślnie min. 500 ms).</summary>
    OnHold = 2,

    /// <summary>Akcja uruchamiana przy szybkim dwukrotnym naciśnięciu (w oknie 300 ms).</summary>
    OnDoublePress = 3,

    /// <summary>Akcja uruchamiana natychmiast, a następnie cyklicznie powtarzana przy przytrzymaniu (domyślnie po 400 ms, co 100 ms).</summary>
    OnHoldRepeat = 4
}
