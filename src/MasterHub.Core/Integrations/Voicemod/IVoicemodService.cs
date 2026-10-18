namespace MasterHub.Core.Integrations.Voicemod;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Profiles;

public record VoicemodVoiceItem(
    string Id,
    string Name,
    bool IsFavorited);

public record VoicemodSoundItem(
    string FileName,
    string Name,
    string? Type = null,
    bool IsCore = false);

public record VoicemodState(
    bool IsConnected,
    bool IsVoiceChangerEnabled,
    bool IsHearMyVoiceEnabled,
    bool IsMicMuted,
    string? CurrentVoiceId,
    string? CurrentVoiceName,
    bool IsBackgroundEffectsEnabled = true);

public interface IVoicemodService : IDisposable
{
    bool IsConnected { get; }
    bool IsVoiceChangerEnabled { get; }
    bool IsHearMyVoiceEnabled { get; }
    bool IsMicMuted { get; }
    bool IsBackgroundEffectsEnabled { get; }
    string? CurrentVoiceId { get; }
    string? CurrentVoiceName { get; }
    IReadOnlyList<VoicemodVoiceItem> AvailableVoices { get; }
    IReadOnlyList<VoicemodSoundItem> AvailableSounds { get; }
    VoicemodState CurrentState { get; }

    event Action<bool>? ConnectionStateChanged;
    event Action<VoicemodState>? StateChanged;
    event Action<IReadOnlyList<VoicemodVoiceItem>>? VoicesLoaded;
    event Action<IReadOnlyList<VoicemodSoundItem>>? SoundsLoaded;
    event Action<string>? SoundPlayed;

    void Configure(VoicemodSettingsModel settings);
    Task<(bool Success, string Message)> ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();

    Task<bool> SelectVoiceAsync(string voiceIdOrName, CancellationToken ct = default);
    Task<bool> ToggleVoiceChangerAsync(CancellationToken ct = default);
    Task<bool> ToggleHearMyVoiceAsync(CancellationToken ct = default);
    Task<bool> ToggleMuteMicAsync(CancellationToken ct = default);
    Task<bool> ToggleBackgroundEffectsAsync(CancellationToken ct = default);
    Task<bool> SetBackgroundEffectsAsync(bool enabled, CancellationToken ct = default);
    Task<IReadOnlyList<VoicemodVoiceItem>> GetVoicesAsync(CancellationToken ct = default);
    Task<VoicemodState> RefreshStateAsync(CancellationToken ct = default);

    Task<bool> PlaySoundAsync(string soundFileNameOrName, CancellationToken ct = default);
    Task<bool> StopAllSoundsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VoicemodSoundItem>> GetSoundsAsync(CancellationToken ct = default);

    void StartMonitoring();
    void StopMonitoring();
}
