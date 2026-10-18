using System;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.Teams;

/// <summary>
/// Typy skrótów klawiszowych sterowania spotkaniem w Microsoft Teams (Etap 5.6).
/// </summary>
public enum TeamsShortcutType
{
    /// <summary>
    /// Wycisz / odcisz mikrofon (Ctrl + Shift + M).
    /// </summary>
    Mute = 0,

    /// <summary>
    /// Włącz / wyłącz kamerę (Ctrl + Shift + O).
    /// </summary>
    Camera = 1,

    /// <summary>
    /// Podnieś / opuść rękę (Ctrl + Shift + K).
    /// </summary>
    RaiseHand = 2,

    /// <summary>
    /// Opuść / zakończ spotkanie (Ctrl + Shift + H).
    /// </summary>
    LeaveMeeting = 3
}

/// <summary>
/// Interfejs usługi sterowania spotkaniami w Microsoft Teams (Etap 5.6).
/// Pozwala na globalne przełączanie stanu wyciszenia, kamery, podnoszenia ręki oraz opuszczania spotkania.
/// </summary>
public interface ITeamsService : IDisposable
{
    /// <summary>
    /// Określa, czy proces aplikacji Microsoft Teams (ms-teams lub Teams) jest obecnie uruchomiony w systemie Windows.
    /// </summary>
    bool IsTeamsRunning { get; }

    /// <summary>
    /// Nazwa wykrytego procesu aplikacji Microsoft Teams (np. "ms-teams" lub "Teams").
    /// </summary>
    string? DetectedTeamsProcessName { get; }

    /// <summary>
    /// Śledzony stan wyciszenia mikrofonu w Teams.
    /// </summary>
    bool IsMuted { get; set; }

    /// <summary>
    /// Śledzony stan kamery w Teams.
    /// </summary>
    bool IsCameraOn { get; set; }

    /// <summary>
    /// Śledzony stan podniesienia ręki w Teams.
    /// </summary>
    bool IsHandRaised { get; set; }

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu obecności procesu Microsoft Teams w systemie.
    /// </summary>
    event Action<bool>? TeamsRunningChanged;

    /// <summary>
    /// Zdarzenie komunikatów diagnostycznych i logów usługi Teams.
    /// </summary>
    event Action<string>? LogMessage;

    /// <summary>
    /// Zdarzenie informujące o zmianie śledzonego stanu spotkania (isMuted, isCameraOn, isHandRaised).
    /// </summary>
    event Action<bool, bool, bool>? MeetingStateChanged;

    /// <summary>
    /// Przełącza wyciszenie mikrofonu w Microsoft Teams (Ctrl + Shift + M).
    /// </summary>
    Task<bool> ToggleMuteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza kamerę wideo w Microsoft Teams (Ctrl + Shift + O).
    /// </summary>
    Task<bool> ToggleCameraAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Podnosi lub opuszcza rękę w Microsoft Teams (Ctrl + Shift + K).
    /// </summary>
    Task<bool> ToggleRaiseHandAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opuszcza bieżące spotkanie w Microsoft Teams (Ctrl + Shift + H).
    /// </summary>
    Task<bool> LeaveMeetingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Wysyła dedykowany skrót klawiszowy do aktywnego lub odnalezionego okna Microsoft Teams.
    /// </summary>
    Task<bool> SendTeamsShortcutAsync(TeamsShortcutType shortcut, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sprawdza w czasie rzeczywistym obecność uruchomionego procesu Microsoft Teams w systemie.
    /// </summary>
    bool CheckIfTeamsRunning();
}
