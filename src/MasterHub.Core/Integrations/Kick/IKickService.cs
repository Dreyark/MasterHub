namespace MasterHub.Core.Integrations.Kick;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Interfejs usługi integracji z platformą Kick (Pusher WebSocket &amp; Kick API).
/// Umożliwia sterowanie trybami czatu (Sub-Only, Slow Mode, Emote-Only),
/// czyszczenie czatu, wysyłanie wiadomości oraz tworzenie klipów.
/// </summary>
public interface IKickService : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Czy połączenie z Kick jest aktywne.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Nazwa połączonego kanału Kick.
    /// </summary>
    string? ChannelName { get; }

    /// <summary>
    /// Nazwa użytkownika / bota na Kick.
    /// </summary>
    string? BotUsername { get; }

    /// <summary>
    /// Czy aktywny jest tryb tylko dla subskrybentów.
    /// </summary>
    bool IsSubOnly { get; }

    /// <summary>
    /// Czy aktywny jest tryb powolny czatu.
    /// </summary>
    bool IsSlowMode { get; }

    /// <summary>
    /// Czas oczekiwania w trybie powolnym w sekundach.
    /// </summary>
    int SlowModeDurationSeconds { get; }

    /// <summary>
    /// Czy aktywny jest tryb tylko dla emotek.
    /// </summary>
    bool IsEmoteOnly { get; }

    /// <summary>
    /// Ostatnio utworzony adres URL klipu.
    /// </summary>
    string? LastClipUrl { get; }

    /// <summary>
    /// Zdarzenie zmiany stanu połączenia.
    /// </summary>
    event Action<bool>? ConnectionStateChanged;

    /// <summary>
    /// Zdarzenie odebrania nowej wiadomości czatu (użytkownik, treść).
    /// </summary>
    event Action<string, string>? MessageReceived;

    /// <summary>
    /// Zdarzenie zmiany trybu Sub-Only.
    /// </summary>
    event Action<bool>? SubOnlyStateChanged;

    /// <summary>
    /// Zdarzenie zmiany trybu Slow Mode.
    /// </summary>
    event Action<bool, int>? SlowModeStateChanged;

    /// <summary>
    /// Zdarzenie zmiany trybu Emote-Only.
    /// </summary>
    event Action<bool>? EmoteOnlyStateChanged;

    /// <summary>
    /// Zdarzenie wygenerowania klipu.
    /// </summary>
    event Action<KickClipResult>? ClipCreated;

    /// <summary>
    /// Nawiązuje połączenie z Kick (Pusher WebSocket czatu i API kanału).
    /// </summary>
    Task<bool> ConnectAsync(
        string channel,
        string? authToken = null,
        string? username = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rozłącza sesję z Kick.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Wysyła wiadomość na czat kanału Kick.
    /// </summary>
    Task<bool> SendChatMessageAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Czyści czat transmisji Kick.
    /// </summary>
    Task<bool> ClearChatAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza tryb tylko dla subskrybentów (Subscribers-Only).
    /// </summary>
    Task<bool> ToggleSubOnlyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Włącza lub wyłącza tryb tylko dla subskrybentów.
    /// </summary>
    Task<bool> SetSubOnlyAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza tryb powolny czatu (Slow Mode).
    /// </summary>
    Task<bool> ToggleSlowModeAsync(int seconds = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Włącza lub wyłącza tryb powolny czatu.
    /// </summary>
    Task<bool> SetSlowModeAsync(bool enabled, int seconds = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza tryb tylko dla emotek (Emote-Only).
    /// </summary>
    Task<bool> ToggleEmoteOnlyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Włącza lub wyłącza tryb tylko dla emotek.
    /// </summary>
    Task<bool> SetEmoteOnlyAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tworzy klip z aktualnej transmisji Kick.
    /// </summary>
    Task<KickClipResult?> CreateClipAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Wynik operacji tworzenia klipu na platformie Kick.
/// </summary>
public record KickClipResult(bool Success, string? ClipId, string? ClipUrl, string? ErrorMessage);
