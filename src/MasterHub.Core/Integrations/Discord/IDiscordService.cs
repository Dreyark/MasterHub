using System;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.Discord;

/// <summary>
/// Kody operacji (Opcodes) protokołu lokalnego potoku Discord IPC (Named Pipe).
/// Nagłówek ramki ma zawsze 8 bajtów (Little-Endian): [uint32 Opcode][uint32 Length].
/// </summary>
public enum DiscordIpcOpcode : uint
{
    Handshake = 0,
    Frame = 1,
    Close = 2,
    Ping = 3,
    Pong = 4
}

/// <summary>
/// Reprezentuje bieżący stan ustawień głosowych użytkownika w aplikacji Discord Desktop.
/// </summary>
public sealed record DiscordVoiceSettings(
    bool IsMuted,
    bool IsDeafened,
    double InputVolume = 100.0,
    double OutputVolume = 100.0,
    string? ModeType = null);

/// <summary>
/// Kontrakt serwisu integracji z lokalną aplikacją Discord Desktop poprzez Named Pipe (discord-ipc-0 .. discord-ipc-9).
/// Etap 5.1: Połączenie Local IPC, Handshake JSON-RPC oraz odczyt stanu wyciszenia mikrofonu (Mute) i odsłuchu (Deafen).
/// </summary>
public interface IDiscordService : IDisposable
{
    /// <summary>
    /// Czy nawiązano aktywne połączenie IPC z lokalnym klientem Discord Desktop.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Czy sesja RPC została zautoryzowana tokenem OAuth2 (AUTHENTICATE).
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Czy mikrofon użytkownika na Discordzie jest aktualnie wyciszony (Mute lub Deafen).
    /// </summary>
    bool IsMuted { get; }

    /// <summary>
    /// Czy odsłuch użytkownika na Discordzie jest aktualnie wyciszony (Deafen).
    /// </summary>
    bool IsDeafened { get; }

    /// <summary>
    /// Czy kamera wideo użytkownika w rozmowie Discord jest aktualnie włączona (Etap 5.3).
    /// </summary>
    bool IsCameraEnabled { get; }

    /// <summary>
    /// Czy udostępnianie ekranu (Go Live / Screen Share) na Discordzie jest aktualnie aktywne (Etap 5.3).
    /// </summary>
    bool IsScreenSharing { get; }

    /// <summary>
    /// Czy transmisja głosu Push-To-Talk jest aktualnie aktywna (Etap 5.4).
    /// </summary>
    bool IsPushToTalkActive { get; }

    /// <summary>
    /// Identyfikator kanału głosowego, do którego użytkownik jest aktualnie dołączony (Etap 5.5), lub null.
    /// </summary>
    string? CurrentVoiceChannelId { get; }

    /// <summary>
    /// Nazwa kanału głosowego, do którego użytkownik jest aktualnie dołączony (Etap 5.5), lub null.
    /// </summary>
    string? CurrentVoiceChannelName { get; }

    /// <summary>
    /// Identyfikator serwera (Guild ID), na którym znajduje się aktywny kanał głosowy (Etap 5.5), lub null.
    /// </summary>
    string? CurrentGuildId { get; }

    /// <summary>
    /// Używany identyfikator aplikacji Discord RPC (Client ID).
    /// </summary>
    string ClientId { get; }

    /// <summary>
    /// Nazwa potoku Named Pipe, z którym nawiązano połączenie (np. "discord-ipc-0"), lub null.
    /// </summary>
    string? ConnectedPipeName { get; }

    /// <summary>
    /// Nazwa zalogowanego użytkownika Discord odczytana podczas Handshake (READY), lub null.
    /// </summary>
    string? CurrentUsername { get; }

    /// <summary>
    /// Identyfikator zalogowanego użytkownika Discord odczytany podczas Handshake (READY), lub null.
    /// </summary>
    string? CurrentUserId { get; }

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu połączenia IPC z Discordem.
    /// </summary>
    event Action<bool>? ConnectionStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu wyciszenia mikrofonu lub odsłuchu (isMuted, isDeafened).
    /// </summary>
    event Action<bool, bool>? VoiceStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu kamery lub udostępniania ekranu (isCameraEnabled, isScreenSharing).
    /// </summary>
    event Action<bool, bool>? VideoStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie stanu nadawania głosu Push-To-Talk (isTransmitting) (Etap 5.4).
    /// </summary>
    event Action<bool>? PushToTalkStateChanged;

    /// <summary>
    /// Zdarzenie wywoływane przy zmianie aktywnego kanału głosowego Discord (channelId, guildId) (Etap 5.5).
    /// </summary>
    event Action<string?, string?>? VoiceChannelChanged;

    /// <summary>
    /// Zdarzenie diagnostyczne przekazujące komunikaty logów serwisu Discord IPC.
    /// </summary>
    event Action<string>? LogMessage;

    /// <summary>
    /// Łączy się z lokalnym potokiem Discord IPC (discord-ipc-0..9), wykonuje Handshake RPC
    /// oraz opcjonalnie uwierzytelnia sesję i pobiera stan wyciszenia (GET_VOICE_SETTINGS).
    /// Bezpiecznie zwraca false, jeśli Discord nie jest uruchomiony.
    /// </summary>
    Task<bool> ConnectAsync(
        string? clientId = null,
        string? accessToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Wykonuje pełny automatyczny proces autoryzacji OAuth2 (AUTHORIZE -> wymiana kodu na token -> AUTHENTICATE).
    /// Wyświetla oficjalne okienko zgody wewnątrz aplikacji Discord Desktop.
    /// </summary>
    Task<(bool Success, string? AccessToken, string? Error)> AuthorizeAndAuthenticateAsync(
        string clientId,
        string clientSecret,
        string redirectUri = "https://localhost/",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Zamyka połączenie z potokiem Discord IPC i zwalnia zasoby strumienia.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Pobiera aktualny stan ustawień głosowych (GET_VOICE_SETTINGS) z klienta Discord Desktop.
    /// </summary>
    Task<DiscordVoiceSettings?> GetVoiceSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ustawia stan wyciszenia mikrofonu (SET_VOICE_SETTINGS mute) w kliencie Discord Desktop.
    /// </summary>
    Task<bool> SetMuteAsync(bool mute, CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza stan wyciszenia mikrofonu (Mute / Unmute) w kliencie Discord Desktop.
    /// </summary>
    Task<bool> ToggleMuteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ustawia stan wyciszenia odsłuchu (SET_VOICE_SETTINGS deaf) w kliencie Discord Desktop.
    /// </summary>
    Task<bool> SetDeafenAsync(bool deaf, CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza stan wyciszenia odsłuchu (Deafen / Undeafen) w kliencie Discord Desktop.
    /// </summary>
    Task<bool> ToggleDeafenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ustawia stan kamery wideo (TOGGLE_VIDEO) w rozmowie Discord Desktop (Etap 5.3).
    /// </summary>
    Task<bool> SetCameraAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza stan kamery wideo (ON / OFF) w rozmowie Discord Desktop (Etap 5.3).
    /// </summary>
    Task<bool> ToggleCameraAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ustawia stan udostępniania ekranu / Go Live (TOGGLE_SCREENSHARE) w rozmowie Discord Desktop (Etap 5.3).
    /// </summary>
    Task<bool> SetScreenShareAsync(bool active, CancellationToken cancellationToken = default);

    /// <summary>
    /// Przełącza stan udostępniania ekranu / Go Live (ON / OFF) w rozmowie Discord Desktop (Etap 5.3).
    /// </summary>
    Task<bool> ToggleScreenShareAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ustawia stan Push-To-Talk (aktywacja nadawania na naciśnięcie klawisza / dezaktywacja na zwolnienie) (Etap 5.4).
    /// </summary>
    Task<bool> SetPushToTalkActiveAsync(bool active, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dołącza do wskazanego kanału głosowego w Discord Desktop (SELECT_VOICE_CHANNEL) (Etap 5.5).
    /// Jeśli parametr <paramref name="channelId"/> jest null lub pusty, następuje opuszczenie kanału głosowego.
    /// </summary>
    Task<bool> SelectVoiceChannelAsync(string? channelId, bool force = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opuszcza bieżący kanał głosowy w Discord Desktop (SELECT_VOICE_CHANNEL channel_id=null) (Etap 5.5).
    /// </summary>
    Task<bool> LeaveVoiceChannelAsync(CancellationToken cancellationToken = default);
}
