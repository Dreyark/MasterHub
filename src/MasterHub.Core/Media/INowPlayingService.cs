using System;

namespace MasterHub.Core.Media;

/// <summary>
/// Interfejs serwisu wykrywającego aktualnie odtwarzany utwór muzyczny (Now Playing) w systemie Windows (Etap 3.3).
/// </summary>
public interface INowPlayingService : IDisposable
{
    /// <summary>Czy monitorowanie odtwarzania jest włączone.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Tytuł aktualnie granego utworu.</summary>
    string CurrentTitle { get; }

    /// <summary>Wykonawca utworu.</summary>
    string CurrentArtist { get; }

    /// <summary>Nazwa albumu lub aplikacji źródłowej.</summary>
    string CurrentAlbum { get; }

    /// <summary>Czy dźwięk/muzyka jest aktualnie odtwarzana.</summary>
    bool IsPlaying { get; }

    /// <summary>Aktualny status odtwarzania (Playing, Paused, Stopped).</summary>
    MediaPlaybackStatus PlaybackStatus { get; }

    /// <summary>Dane binarne okładki utworu w formacie JPEG (jeśli dostępne).</summary>
    byte[]? CurrentArtworkJpeg { get; }

    /// <summary>Kompletny model informacji o aktualnie granym utworze.</summary>
    NowPlayingInfo CurrentTrackInfo { get; }

    /// <summary>Sformatowany tekst do wyświetlenia na ekranie LCD lub w UI (np. "Queen - Bohemian Rhapsody").</summary>
    string DisplayTrack { get; }

    /// <summary>Zdarzenie wywoływane gdy zmieni się odtwarzany utwór lub stan odtwarzania.</summary>
    event Action? TrackChanged;

    /// <summary>Zdarzenie ze szczegółowymi danymi NowPlayingInfo (Etap 3.3).</summary>
    event Action<NowPlayingInfo>? TrackInfoChanged;

    /// <summary>Wymusza natychmiastowe sprawdzenie stanu odtwarzaczy.</summary>
    void CheckNow();

    /// <summary>Ustawia lub symuluje informacje o aktualnie odtwarzanym utworze.</summary>
    void SetTrackInfo(string artist, string title, string album = "", bool isPlaying = true, MediaPlaybackStatus status = MediaPlaybackStatus.Playing, byte[]? artworkJpeg = null);
}
