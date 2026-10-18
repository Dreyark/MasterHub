namespace MasterHub.Core.Integrations.Obs;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Interfejs usługi komunikacji z OBS Studio przez protokół OBS WebSocket v5.
/// </summary>
public interface IObsService : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Czy połączenie z OBS WebSocket jest aktywne i uwierzytelnione.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Host serwera OBS WebSocket (np. localhost lub 127.0.0.1).
    /// </summary>
    string Host { get; }

    /// <summary>
    /// Port serwera OBS WebSocket (domyślnie 4455).
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Zwraca zgłoszoną przez serwer wersję protokołu OBS WebSocket (np. 5.5.0).
    /// </summary>
    string? ObsWebSocketVersion { get; }

    /// <summary>
    /// Ostatni zarejestrowany błąd połączenia lub uwierzytelnienia z OBS.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Czy aktywny jest aktualnie stream / nadawanie na żywo.
    /// </summary>
    bool IsStreaming { get; }

    /// <summary>
    /// Czy aktywne jest aktualnie nagrywanie wideo.
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Czy wirtualna kamera OBS jest aktualnie aktywna.
    /// </summary>
    bool IsVirtualCamActive { get; }

    /// <summary>
    /// Czy bufor powtórek OBS jest aktualnie aktywny.
    /// </summary>
    bool IsReplayBufferActive { get; }

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu połączenia (true = połączono i zalogowano, false = rozłączono).
    /// </summary>
    event Action<bool>? ConnectionStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu streamu (true = nadawanie, false = zatrzymany).
    /// </summary>
    event Action<bool>? StreamStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu nagrywania (true = nagrywa, false = zatrzymane).
    /// </summary>
    event Action<bool>? RecordStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu wirtualnej kamery (true = włączona, false = wyłączona).
    /// </summary>
    event Action<bool>? VirtualCamStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu bufora powtórek (true = aktywny, false = wyłączony).
    /// </summary>
    event Action<bool>? ReplayBufferStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane po zapisaniu pliku powtórki (ścieżka do pliku).
    /// </summary>
    event Action<string>? ReplayBufferSaved;

    /// <summary>
    /// Nazwa aktualnie aktywnej sceny w programie OBS (Program Scene).
    /// </summary>
    string? CurrentProgramSceneName { get; }

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie aktywnej sceny programu w OBS.
    /// </summary>
    event Action<string>? CurrentProgramSceneChanged;

    /// <summary>
    /// Czy tryb studyjny (Studio Mode) w OBS jest włączony.
    /// </summary>
    bool IsStudioModeActive { get; }

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu trybu studyjnego (true = włączony, false = wyłączony).
    /// </summary>
    event Action<bool>? StudioModeStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane po odebraniu zdarzenia z OBS (OpCode 5: Event).
    /// </summary>
    event Action<string, JsonElement>? EventReceived;

    /// <summary>
    /// Nawiązuje asynchroniczne połączenie z OBS WebSocket v5 i wykonuje handshake/uwierzytelnianie.
    /// </summary>
    Task<bool> ConnectAsync(string host = "localhost", int port = 4455, string? password = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rozłącza aktywne połączenie z serwerem OBS WebSocket.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Wysyła żądanie RPC (OpCode 6) do OBS WebSocket i oczekuje na odpowiedź (OpCode 7).
    /// </summary>
    Task<JsonElement?> SendRequestAsync(string requestType, object? requestData = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza stan streamu (ToggleStream).
    /// </summary>
    Task<bool> ToggleStreamAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uruchamia nadawanie streamu (StartStream).
    /// </summary>
    Task<bool> StartStreamAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Zatrzymuje nadawanie streamu (StopStream).
    /// </summary>
    Task<bool> StopStreamAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza stan nagrywania (ToggleRecord).
    /// </summary>
    Task<bool> ToggleRecordAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rozpoczyna nagrywanie wideo (StartRecord).
    /// </summary>
    Task<bool> StartRecordAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Zatrzymuje nagrywanie wideo (StopRecord).
    /// </summary>
    Task<bool> StopRecordAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Odpytuje OBS o aktualny stan streamu (GetStreamStatus).
    /// </summary>
    Task<bool> RefreshStreamStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Odpytuje OBS o aktualny stan nagrywania (GetRecordStatus).
    /// </summary>
    Task<bool> RefreshRecordStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza stan wirtualnej kamery (ToggleVirtualCam).
    /// </summary>
    Task<bool> ToggleVirtualCamAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Włącza wirtualną kamerę OBS (StartVirtualCam).
    /// </summary>
    Task<bool> StartVirtualCamAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Wyłącza wirtualną kamerę OBS (StopVirtualCam).
    /// </summary>
    Task<bool> StopVirtualCamAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Odpytuje OBS o aktualny stan wirtualnej kamery (GetVirtualCamStatus).
    /// </summary>
    Task<bool> RefreshVirtualCamStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza stan bufora powtórek (ToggleReplayBuffer).
    /// </summary>
    Task<bool> ToggleReplayBufferAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uruchamia bufor powtórek OBS (StartReplayBuffer).
    /// </summary>
    Task<bool> StartReplayBufferAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Zatrzymuje bufor powtórek OBS (StopReplayBuffer).
    /// </summary>
    Task<bool> StopReplayBufferAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Zapisuje aktualną zawartość bufora powtórek na dysk (SaveReplayBuffer).
    /// </summary>
    Task<bool> SaveReplayBufferAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Odpytuje OBS o aktualny stan bufora powtórek (GetReplayBufferStatus).
    /// </summary>
    Task<bool> RefreshReplayBufferStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza aktywną scenę programu w OBS (SetCurrentProgramScene).
    /// </summary>
    Task<bool> SetCurrentProgramSceneAsync(string sceneName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pobiera nazwę aktualnie wybranej sceny w OBS (GetCurrentProgramScene).
    /// </summary>
    Task<string?> GetCurrentProgramSceneAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pobiera listę wszystkich dostępnych scen w OBS Studio (GetSceneList).
    /// </summary>
    Task<System.Collections.Generic.IReadOnlyList<string>> GetSceneListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza stan trybu studyjnego OBS (ToggleStudioMode).
    /// </summary>
    Task<bool> ToggleStudioModeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ustawia stan trybu studyjnego OBS (SetStudioModeEnabled).
    /// </summary>
    Task<bool> SetStudioModeEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pobiera aktualny stan włączenia trybu studyjnego z OBS (GetStudioModeEnabled).
    /// </summary>
    Task<bool> GetStudioModeEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Wyzwala przejście podglądu do programu w trybie studyjnym (TriggerStudioModeTransition).
    /// </summary>
    Task<bool> TriggerStudioModeTransitionAsync(CancellationToken cancellationToken = default);
}
