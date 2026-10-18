namespace MasterHub.Core.Media;

/// <summary>
/// Informacje o aktualnie odtwarzanym utworze multimedialnym (Etap 3.3).
/// </summary>
public sealed class NowPlayingInfo
{
    /// <summary>Tytuł utworu.</summary>
    public string Title { get; set; } = "";

    /// <summary>Wykonawca lub autor utworu.</summary>
    public string Artist { get; set; } = "";

    /// <summary>Nazwa albumu lub aplikacji odtwarzacza.</summary>
    public string Album { get; set; } = "";

    /// <summary>Stan odtwarzania (Playing, Paused, Stopped).</summary>
    public MediaPlaybackStatus PlaybackStatus { get; set; } = MediaPlaybackStatus.Stopped;

    /// <summary>Okładka albumu w formacie JPEG lub dane binarne grafiki (opcjonalnie).</summary>
    public byte[]? ArtworkJpeg { get; set; }

    /// <summary>Czy dźwięk/muzyka jest aktualnie odtwarzana.</summary>
    public bool IsPlaying => PlaybackStatus == MediaPlaybackStatus.Playing;

    /// <summary>Sformatowany tekst do prezentacji w UI i na klawiszach LCD.</summary>
    public string DisplayText
    {
        get
        {
            if (!IsPlaying || (string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Artist)))
            {
                return "Brak odtwarzania";
            }

            if (!string.IsNullOrWhiteSpace(Artist) && !string.IsNullOrWhiteSpace(Title))
            {
                return $"{Artist} - {Title}";
            }

            return !string.IsNullOrWhiteSpace(Title) ? Title : Artist;
        }
    }
}
