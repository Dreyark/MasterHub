namespace MasterHub.Core.Integrations.Vlc;

using System;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Profiles;

public enum VlcPlaybackStateEnum
{
    Stopped = 0,
    Playing = 1,
    Paused = 2
}

public record VlcPlaybackState(
    bool IsConnected,
    VlcPlaybackStateEnum State,
    bool IsPlaying,
    bool IsFullscreen,
    int VolumePercent,
    string? Title,
    string? Artist,
    string? Version);

public interface IVlcService : IDisposable
{
    bool IsConnected { get; }
    bool IsPlaying { get; }
    bool IsFullscreen { get; }
    int VolumePercent { get; }
    string? CurrentTitle { get; }
    string? CurrentArtist { get; }
    VlcPlaybackStateEnum PlaybackState { get; }
    VlcPlaybackState CurrentState { get; }

    event Action<bool>? ConnectionStateChanged;
    event Action<VlcPlaybackState>? PlaybackStateChanged;

    void Configure(VlcSettingsModel settings);
    Task<(bool Success, string Message)> TestConnectionDetailedAsync(CancellationToken ct = default);
    Task DisconnectAsync();

    Task<bool> PlayPauseAsync(CancellationToken ct = default);
    Task<bool> StopAsync(CancellationToken ct = default);
    Task<bool> NextTrackAsync(CancellationToken ct = default);
    Task<bool> PreviousTrackAsync(CancellationToken ct = default);
    Task<bool> ToggleFullscreenAsync(CancellationToken ct = default);

    Task<VlcPlaybackState?> RefreshPlaybackStateAsync(CancellationToken ct = default);
    void StartMonitoring();
    void StopMonitoring();
}
