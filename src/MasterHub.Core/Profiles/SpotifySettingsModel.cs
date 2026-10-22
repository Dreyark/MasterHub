namespace MasterHub.Core.Profiles;

using System;

public class SpotifySettingsModel
{
    /// <summary>
    /// Identyfikator klienta aplikacji Spotify (Spotify Developer Dashboard).
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// Opcjonalny sekret klienta (wymagany tylko w trybie Authorization Code z Client Secret).
    /// W trybie PKCE sekret nie jest wymagany.
    /// </summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Aktualny token dostępu (OAuth Bearer Token).
    /// </summary>
    public string AccessToken { get; set; } = "";

    /// <summary>
    /// Token odświeżania sesji OAuth.
    /// </summary>
    public string RefreshToken { get; set; } = "";

    /// <summary>
    /// Data i czas wygaśnięcia bieżącego tokenu dostępu w UTC.
    /// </summary>
    public DateTime? TokenExpiresAtUtc { get; set; }

    /// <summary>
    /// Czy automatycznie odnawiać połączenie i monitorować stan odtwarzania przy starcie.
    /// </summary>
    public bool AutoConnect { get; set; } = true;

    /// <summary>
    /// Interwał odpytywania stanu odtwarzacza w sekundach (domyślnie 2s).
    /// </summary>
    public int StatusPollIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// Czy używać skrótów klawiaturowych aplikacji Spotify Desktop (Ctrl+S, Ctrl+R),
    /// gdy API Web nie jest połączone lub występuje błąd sieci.
    /// </summary>
    public bool FallbackToDesktopHotkeys { get; set; } = true;
}
