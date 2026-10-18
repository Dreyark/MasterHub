namespace MasterHub.Core.Integrations.Spotify;

using System;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Profiles;

public enum SpotifyRepeatMode
{
    Off = 0,
    Context = 1, // Powtarzanie playlisty/albumu ("all")
    Track = 2    // Powtarzanie bieżącego utworu ("1")
}

public record SpotifyPlaybackState(
    bool IsPlaying,
    bool IsShuffle,
    SpotifyRepeatMode RepeatMode,
    bool IsLiked,
    string? TrackId,
    string? TrackTitle,
    string? ArtistName,
    string? AlbumTitle,
    string? AlbumArtUrl,
    int VolumePercent,
    bool IsConnected);

public record SpotifyPlaylistItem(
    string Id,
    string Name,
    string Uri,
    string? ImageUrl,
    int TrackCount);

public interface ISpotifyService : IDisposable
{
    bool IsConnected { get; }
    bool IsPlaying { get; }
    bool IsShuffle { get; }
    SpotifyRepeatMode RepeatMode { get; }
    bool IsCurrentTrackLiked { get; }
    string? CurrentTrackId { get; }
    string? CurrentTrackTitle { get; }
    string? CurrentTrackArtist { get; }
    string? CurrentAlbumTitle { get; }
    string? CurrentAlbumArtUrl { get; }
    int VolumePercent { get; }
    SpotifyPlaybackState CurrentState { get; }

    event Action<bool>? ConnectionStateChanged;
    event Action<SpotifyPlaybackState>? PlaybackStateChanged;

    void Configure(SpotifySettingsModel settings);
    Task<(bool Success, string Message)> StartPkceLoginAsync(Action<string> openBrowserCallback, CancellationToken ct = default);
    Task DisconnectAsync();

    Task<bool> ToggleShuffleAsync(CancellationToken ct = default);
    Task<bool> SetShuffleAsync(bool enable, CancellationToken ct = default);
    Task<SpotifyRepeatMode> ToggleRepeatAsync(CancellationToken ct = default);
    Task<bool> SetRepeatModeAsync(SpotifyRepeatMode mode, CancellationToken ct = default);
    Task<bool> ToggleLikeTrackAsync(CancellationToken ct = default);
    Task<bool> LikeCurrentTrackAsync(CancellationToken ct = default);
    Task<bool> UnlikeCurrentTrackAsync(CancellationToken ct = default);

    Task<bool> PlayPauseAsync(CancellationToken ct = default);
    Task<bool> NextTrackAsync(CancellationToken ct = default);
    Task<bool> PreviousTrackAsync(CancellationToken ct = default);

    Task<bool> AddToQueueAsync(string trackUriOrUrl, CancellationToken ct = default);
    Task<bool> PlayUriAsync(string uriOrUrl, CancellationToken ct = default);
    Task<IReadOnlyList<SpotifyPlaylistItem>> GetUserPlaylistsAsync(CancellationToken ct = default);

    Task<SpotifyPlaybackState?> RefreshPlaybackStateAsync(CancellationToken ct = default);
    void StartMonitoring();
    void StopMonitoring();
}
