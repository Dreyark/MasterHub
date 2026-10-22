using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.HomeAssistant;

/// <summary>
/// Serwis integracji ze Smart Home Home Assistant przez REST API oraz WebSocket.
/// Obsługuje sterowanie encjami (przełączanie, włączanie, wyłączanie, wywoływanie usług)
/// oraz dwukierunkowy feedback stanu na żywo.
/// </summary>
public interface IHomeAssistantService : IDisposable
{
    bool IsConnected { get; }
    string ServerUrl { get; }
    IReadOnlyList<HomeAssistantEntityModel> CachedEntities { get; }
    IReadOnlyList<HomeAssistantEntityModel> Entities { get; }
    HomeAssistantEntityModel? GetEntity(string entityId);

    event EventHandler<bool>? ConnectionStateChanged;
    event EventHandler<HomeAssistantEntityModel>? EntityStateChanged;
    event EventHandler<IReadOnlyList<HomeAssistantEntityModel>>? EntitiesUpdated;

    Task<bool> ConnectAsync(CancellationToken ct = default);
    Task<bool> ConnectAsync(string serverUrl, string accessToken, CancellationToken ct = default);
    Task DisconnectAsync();
    Task<List<HomeAssistantEntityModel>> FetchEntitiesAsync(CancellationToken ct = default);
    Task<HomeAssistantEntityModel?> GetEntityStateAsync(string entityId, CancellationToken ct = default);

    Task<bool> ToggleAsync(string entityId, CancellationToken ct = default);
    Task<bool> TurnOnAsync(string entityId, int? brightness = null, CancellationToken ct = default);
    Task<bool> TurnOffAsync(string entityId, CancellationToken ct = default);
    Task<bool> CallServiceAsync(string domain, string service, string? entityId = null, object? serviceData = null, CancellationToken ct = default);
    Task<bool> SetLightBrightnessAsync(string entityId, int brightness, CancellationToken ct = default);
    Task<bool> AdjustLightBrightnessAsync(string? entityId, int deltaBrightness, CancellationToken ct = default);
}
