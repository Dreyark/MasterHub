namespace MasterHub.Core.Integrations.Twitch;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Interfejs usługi komunikacji z czatem Twitch (Twitch IRC over WebSocket).
/// Umożliwia sterowanie trybami czatu (Emote-Only, Sub-Only, Slow Mode),
/// czyszczenie czatu oraz wysyłanie wiadomości.
/// </summary>
public interface ITwitchService : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Czy połączenie z Twitch IRC jest aktywne.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Nazwa aktualnie połączonego kanału Twitch (bez znaku #).
    /// </summary>
    string? ChannelName { get; }

    /// <summary>
    /// Nazwa zalogowanego użytkownika / bota Twitch.
    /// </summary>
    string? BotUsername { get; }

    /// <summary>
    /// Czy aktywny jest tryb czatu tylko dla emotek (Emote-Only).
    /// </summary>
    bool IsEmoteOnly { get; }

    /// <summary>
    /// Czy aktywny jest tryb czatu tylko dla subskrybentów (Subscribers-Only).
    /// </summary>
    bool IsSubOnly { get; }

    /// <summary>
    /// Czy aktywny jest tryb powolny czatu (Slow Mode).
    /// </summary>
    bool IsSlowMode { get; }

    /// <summary>
    /// Czas oczekiwania w trybie powolnym w sekundach.
    /// </summary>
    int SlowModeDurationSeconds { get; }

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu połączenia (true = połączono, false = rozłączono).
    /// </summary>
    event Action<bool>? ConnectionStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie trybu Emote-Only (true = włączony, false = wyłączony).
    /// </summary>
    event Action<bool>? EmoteOnlyStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie trybu Subscribers-Only (true = włączony, false = wyłączony).
    /// </summary>
    event Action<bool>? SubOnlyStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie trybu Slow Mode (włączony/wyłączony, sekundy oczekiwania).
    /// </summary>
    event Action<bool, int>? SlowModeStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy odebraniu nowej wiadomości (użytkownik, treść).
    /// </summary>
    event Action<string, string>? MessageReceived;

    /// <summary>
    /// Nawiązuje bezpieczne połączenie z Twitch IRC WebSocket (wss://irc-ws.chat.twitch.tv:443).
    /// </summary>
    Task<bool> ConnectAsync(
        string channel,
        string oauthToken,
        string? username = null,
        string? clientId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Zamyka połączenie z Twitch IRC.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Przełącza tryb tylko dla emotek (Emote-Only) na czacie kanału.
    /// </summary>
    Task<bool> ToggleEmoteOnlyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Włącza lub wyłącza tryb tylko dla emotek (Emote-Only).
    /// </summary>
    Task<bool> SetEmoteOnlyAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza tryb tylko dla subskrybentów (Subscribers-Only) na czacie kanału.
    /// </summary>
    Task<bool> ToggleSubOnlyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Włącza lub wyłącza tryb tylko dla subskrybentów (Subscribers-Only).
    /// </summary>
    Task<bool> SetSubOnlyAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza tryb powolny czatu (Slow Mode) na czacie kanału.
    /// </summary>
    Task<bool> ToggleSlowModeAsync(int seconds = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Włącza lub wyłącza tryb powolny czatu (Slow Mode).
    /// </summary>
    Task<bool> SetSlowModeAsync(bool enabled, int seconds = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Czyści czat transmisji Twitch (/clear).
    /// </summary>
    Task<bool> ClearChatAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Wysyła wiadomość tekstową na czat kanału.
    /// </summary>
    Task<bool> SendChatMessageAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ostatnio utworzony adres URL do edycji/podglądu klipu Twitch.
    /// </summary>
    string? LastClipUrl { get; }

    /// <summary>
    /// Ostatnia pozycja utworzonego znacznika transmisji (np. "04:12").
    /// </summary>
    string? LastMarkerPosition { get; }

    /// <summary>
    /// Ostatnio wybrany docelowy kanał dla raidu.
    /// </summary>
    string? LastRaidTargetChannel { get; }

    /// <summary>
    /// Czy trwa oczekiwanie / odliczanie do raidu.
    /// </summary>
    bool IsRaidPending { get; }

    /// <summary>
    /// Długość ostatnio uruchomionego bloku reklamowego w sekundach.
    /// </summary>
    int LastCommercialLengthSeconds { get; }

    /// <summary>
    /// Zdarzenie wywoływane po pomyślnym (lub nieudanym) utworzeniu klipu z transmisji.
    /// </summary>
    event Action<TwitchClipResult>? ClipCreated;

    /// <summary>
    /// Zdarzenie wywoływane po utworzeniu znacznika / markera w transmisji.
    /// </summary>
    event Action<TwitchMarkerResult>? MarkerCreated;

    /// <summary>
    /// Zdarzenie wywoływane po rozpoczęciu raidu do innego kanału.
    /// </summary>
    event Action<TwitchRaidResult>? RaidStarted;

    /// <summary>
    /// Zdarzenie wywoływane po anulowaniu raidu.
    /// </summary>
    event Action<bool>? RaidCancelled;

    /// <summary>
    /// Zdarzenie wywoływane po uruchomieniu przerwy reklamowej (Commercial).
    /// </summary>
    event Action<TwitchCommercialResult>? CommercialStarted;

    /// <summary>
    /// Tworzy klip z aktualnie trwającej transmisji na żywo (Twitch Helix API).
    /// </summary>
    Task<TwitchClipResult?> CreateClipAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tworzy znacznik czasu (Stream Marker) w aktualnej transmisji na żywo (Twitch Helix API).
    /// </summary>
    Task<TwitchMarkerResult?> CreateStreamMarkerAsync(string? description = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rozpoczyna raid do wskazanego kanału Twitch (Twitch Helix API lub IRC fallback).
    /// </summary>
    Task<TwitchRaidResult> StartRaidAsync(string targetChannel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Anuluje trwający / oczekujący raid na inny kanał (Twitch Helix API lub IRC fallback).
    /// </summary>
    Task<bool> CancelRaidAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uruchamia przerwę reklamową (Commercial) na kanale o zadanej długości w sekundach (30, 60, 90, 120, 150, 180).
    /// </summary>
    Task<TwitchCommercialResult> StartCommercialAsync(int lengthSeconds = 30, CancellationToken cancellationToken = default);
}

/// <summary>
/// Wynik operacji tworzenia klipu Twitch.
/// </summary>
public record TwitchClipResult(bool Success, string? ClipId, string? EditUrl, string? ErrorMessage);

/// <summary>
/// Wynik operacji tworzenia znacznika transmisji (Stream Marker) Twitch.
/// </summary>
public record TwitchMarkerResult(bool Success, string? MarkerId, int PositionSeconds, string? Description, string? ErrorMessage);

/// <summary>
/// Wynik operacji uruchomienia raidu Twitch.
/// </summary>
public record TwitchRaidResult(bool Success, string? TargetChannel, string? ErrorMessage);

/// <summary>
/// Wynik operacji uruchomienia bloku reklamowego (Commercial) Twitch.
/// </summary>
public record TwitchCommercialResult(bool Success, int LengthSeconds, int RetryAfterSeconds, string? ErrorMessage);
