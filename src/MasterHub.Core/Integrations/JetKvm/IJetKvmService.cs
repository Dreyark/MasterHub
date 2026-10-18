using System;
using System.Threading.Tasks;
using MasterHub.Core.Profiles;

namespace MasterHub.Core.Integrations.JetKvm;

public record JetKvmPowerState(bool Power, bool Hdd, bool IsConnected, string? ErrorMessage = null);

public interface IJetKvmService : IDisposable
{
    bool IsConnected { get; }
    bool IsAuthenticated { get; }
    bool LastPowerState { get; }
    bool LastHddState { get; }
    string? DeviceId { get; }
    string? AuthMode { get; }
    JetKvmPowerState CurrentState { get; }

    event Action<bool>? ConnectionStateChanged;
    event Action<JetKvmPowerState>? PowerStateChanged;

    void Configure(JetKvmSettingsModel settings);
    Task<bool> TestConnectionAsync();
    Task<(bool Success, string Message)> TestConnectionDetailedAsync();
    Task DisconnectAsync();
    Task<JetKvmPowerState> RefreshStateAsync();
    Task<bool> PowerShortPressAsync();
    Task<bool> PowerLongPressAsync();
    Task<bool> ResetAsync();
    void StartMonitoring();
    void StopMonitoring();
}

