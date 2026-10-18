using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.Streamlabs;

/// <summary>
/// Reprezentuje scenę w aplikacji Streamlabs Desktop (id zasobu oraz nazwę wyświetlaną).
/// </summary>
public sealed record StreamlabsSceneInfo(
    string Id,
    string Name);

/// <summary>
/// Interfejs serwisu integracji ze Streamlabs Desktop (Etap 4.10) przez lokalne API JSON-RPC 2.0 (port 59650 / Named Pipe).
/// </summary>
public interface IStreamlabsService : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Czy połączenie ze Streamlabs Desktop jest aktualnie aktywne.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Adres hosta lokalnego serwera Streamlabs Desktop (domyślnie 127.0.0.1).
    /// </summary>
    string Host { get; }

    /// <summary>
    /// Port lokalnego serwera Streamlabs Desktop (domyślnie 59650).
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Identyfikator aktualnie aktywnej sceny w Streamlabs Desktop.
    /// </summary>
    string? ActiveSceneId { get; }

    /// <summary>
    /// Nazwa aktualnie aktywnej sceny w Streamlabs Desktop.
    /// </summary>
    string? ActiveSceneName { get; }

    /// <summary>
    /// Lista ostatnio pobranych scen ze Streamlabs Desktop.
    /// </summary>
    IReadOnlyList<StreamlabsSceneInfo> Scenes { get; }

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu połączenia (true = połączono, false = rozłączono).
    /// </summary>
    event Action<bool>? ConnectionStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie aktywnej sceny (przekazuje nazwę aktywnej sceny).
    /// </summary>
    event Action<string>? ActiveSceneChanged;

    /// <summary>
    /// Zdarzenie logowania komunikatów diagnostycznych.
    /// </summary>
    event Action<string>? LogMessage;

    /// <summary>
    /// Nawiązuje połączenie z lokalnym API Streamlabs Desktop (WebSocket na porcie 59650 z opcjonalnym tokenem lub Named Pipe 'slobs').
    /// </summary>
    Task<bool> ConnectAsync(
        string host = "127.0.0.1",
        int port = 59650,
        string? apiToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rozłącza połączenie ze Streamlabs Desktop.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Pobiera listę wszystkich scen ze Streamlabs Desktop (ScenesService.getScenes).
    /// </summary>
    Task<IReadOnlyList<StreamlabsSceneInfo>> GetScenesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pobiera nazwę aktualnie aktywnej sceny (ScenesService.activeScene).
    /// </summary>
    Task<string?> GetActiveSceneAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza aktywną scenę w Streamlabs Desktop po nazwie sceny (case-insensitive) lub identyfikatorze (ScenesService.makeSceneActive).
    /// </summary>
    Task<bool> SwitchSceneAsync(string sceneNameOrId, CancellationToken cancellationToken = default);
}
