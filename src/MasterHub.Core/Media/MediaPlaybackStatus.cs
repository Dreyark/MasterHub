namespace MasterHub.Core.Media;

/// <summary>
/// Status odtwarzania mediów (Etap 3.3).
/// </summary>
public enum MediaPlaybackStatus
{
    /// <summary>Odtwarzanie zatrzymane lub brak aktywnego strumienia.</summary>
    Stopped = 0,

    /// <summary>Muzyka / dźwięk jest aktywnie odtwarzany.</summary>
    Playing = 1,

    /// <summary>Odtwarzanie jest wstrzymane (pauza).</summary>
    Paused = 2
}
