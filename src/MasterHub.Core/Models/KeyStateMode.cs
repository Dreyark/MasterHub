namespace MasterHub.Core.Models;

/// <summary>
/// Określa tryb zachowania stanu wizualnego i ikon klawisza LCD (Etap 3.5).
/// </summary>
public enum KeyStateMode
{
    /// <summary>Zawsze statyczna ikona/grafika niezależnie od stanu systemowego.</summary>
    Static = 0,

    /// <summary>Dwustanowy przełącznik (Toggle) zmieniający ikonę/stan po naciśnięciu.</summary>
    Toggle = 1,

    /// <summary>Dynamiczny stan na żywo (Live Feedback) aktualizowany automatycznie przez system lub usługi zewnętrzne.</summary>
    LiveFeedback = 2
}
