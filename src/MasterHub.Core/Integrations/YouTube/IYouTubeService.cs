using System;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.YouTube;

/// <summary>
/// Reprezentuje aktualny stan transmisji na żywo YouTube Live oraz statystyki oglądalności (Etap 4.9).
/// </summary>
public sealed record YouTubeStreamStatus(
    bool Success,
    bool IsLive,
    int ConcurrentViewers,
    int LikeCount,
    string StreamTitle,
    string ChannelTitle,
    string? VideoId = null,
    string? ErrorMessage = null);

/// <summary>
/// Interfejs serwisu integracji z YouTube Live (YouTube Data API v3 + publiczny fallback bez klucza API).
/// Odpowiada za odpytywanie statusu transmisji (LIVE / OFFLINE) oraz liczby widzów na żywo (Etap 4.9).
/// </summary>
public interface IYouTubeService : IDisposable
{
    /// <summary>Czy monitorowany kanał lub strumień prowadzi aktualnie transmisję na żywo.</summary>
    bool IsLive { get; }

    /// <summary>Aktualna liczba widzów oglądających transmisję na żywo (concurrentViewers).</summary>
    int ConcurrentViewers { get; }

    /// <summary>Liczba polubień (łapek w górę) aktualnej transmisji.</summary>
    int LikeCount { get; }

    /// <summary>Tytuł aktualnej transmisji na żywo.</summary>
    string StreamTitle { get; }

    /// <summary>Nazwa kanału YouTube prowadzącego transmisję.</summary>
    string ChannelTitle { get; }

    /// <summary>Skonfigurowany identyfikator docelowy (ID wideo, ID kanału UC..., @handle lub URL).</summary>
    string TargetIdentifier { get; }

    /// <summary>Skonfigurowany klucz YouTube Data API v3 (opcjonalny).</summary>
    string ApiKey { get; }

    /// <summary>Czy aktywne jest cykliczne odpytywanie statusu transmisji w tle.</summary>
    bool IsPolling { get; }

    /// <summary>Ostatni pobrany wynik statusu transmisji YouTube Live.</summary>
    YouTubeStreamStatus? LastStatus { get; }

    /// <summary>Zdarzenie wywoływane po każdej aktualizacji danych transmisji YouTube Live.</summary>
    event Action<YouTubeStreamStatus>? StatusUpdated;

    /// <summary>Zdarzenie wywoływane przy zmianie stanu nadawania (LIVE &lt;-&gt; OFFLINE).</summary>
    event Action<bool>? LiveStateChanged;

    /// <summary>Zdarzenie przekazujące komunikaty diagnostyczne do dziennika aplikacji.</summary>
    event Action<string>? LogMessage;

    /// <summary>
    /// Konfiguruje domyślny identyfikator kanału/wideo oraz opcjonalny klucz API.
    /// </summary>
    void Configure(string? channelOrVideoId, string? apiKey = null);

    /// <summary>
    /// Pobiera aktualny status transmisji YouTube Live oraz liczbę widzów.
    /// Gdy podano klucz API, korzysta z oficjalnego YouTube Data API v3 (endpointy search + videos).
    /// Gdy klucz API jest pusty, wykorzystuje publiczny odczyt strony transmisji YouTube Live.
    /// </summary>
    Task<YouTubeStreamStatus> FetchStreamStatusAsync(
        string? channelOrVideoId = null,
        string? apiKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uruchamia cykliczne odświeżanie statusu transmisji w tle.
    /// </summary>
    void StartPolling(int intervalSeconds = 30);

    /// <summary>
    /// Zatrzymuje cykliczne odświeżanie statusu transmisji w tle.
    /// </summary>
    void StopPolling();
}
