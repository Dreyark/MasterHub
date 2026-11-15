using System;
using System.Collections.Generic;

namespace MasterHub.Core.Profiles;

/// <summary>
/// Serwis nasłuchujący aktywne okno w systemie Windows i automatycznie
/// przełączający profil MasterHub na podstawie reguł Smart Profile (Etap 2.2).
/// </summary>
public interface ISmartProfileService : IDisposable
{
    /// <summary>Określa czy automatyczne przełączanie profili jest aktywne.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Interwał zabezpieczającego porównania okna pierwszoplanowego (stałe 250 ms; główny sygnał to hook systemowy).</summary>
    int PollingIntervalMs { get; set; }

    /// <summary>Profil bazowy/domyślny, do którego następuje powrót po utracie fokusa (Etap 2.2).</summary>
    string? FallbackProfileId { get; set; }

    /// <summary>Ostatnio wykryty proces na pierwszym planie.</summary>
    string LastForegroundProcess { get; }

    /// <summary>Ostatnio wykryty tytuł aktywnego okna.</summary>
    string LastForegroundTitle { get; }

    /// <summary>Zdarzenie wywoływane gdy zmieni się okno na pierwszym planie (processName, windowTitle).</summary>
    event Action<string, string>? ForegroundWindowChanged;

    /// <summary>Zdarzenie żądania przełączenia profilu (profileId).</summary>
    event Action<string>? AutoSwitchProfileRequested;

    /// <summary>Zdarzenie: globalny przełącznik Auto-switch został zmieniony (np. z paska zadań).</summary>
    event Action<bool>? IsEnabledChanged;

    /// <summary>Rejestruje listę reguł Smart Profile do monitorowania.</summary>
    void UpdateRules(IEnumerable<SmartProfileRule> rules);

    /// <summary>Uruchamia monitorowanie w tle.</summary>
    void Start();

    /// <summary>Zatrzymuje monitorowanie w tle.</summary>
    void Stop();

    /// <summary>Ręczne wymuszenie sprawdzenia aktywnego okna.</summary>
    void CheckForegroundWindowNow();
}
