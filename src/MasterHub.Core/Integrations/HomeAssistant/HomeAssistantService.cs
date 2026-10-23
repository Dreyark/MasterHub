using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Profiles;

namespace MasterHub.Core.Integrations.HomeAssistant;

public class HomeAssistantService : IHomeAssistantService
{
    private readonly HttpClient _httpClient;
    private readonly IProfileService? _profileService;
    private readonly ConcurrentDictionary<string, HomeAssistantEntityModel> _entities = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private string _serverUrl = "http://homeassistant.local:8123";
    private string _accessToken = "";
    private bool _isConnected;
    private bool _isDisposed;

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _wsCts;
    private Task? _wsReceiveTask;
    private System.Threading.Timer? _pollTimer;

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                ConnectionStateChanged?.Invoke(this, value);
            }
        }
    }

    public string ServerUrl => _serverUrl;

    public IReadOnlyList<HomeAssistantEntityModel> CachedEntities => _entities.Values.OrderBy(e => e.EntityId).ToList();
    public IReadOnlyList<HomeAssistantEntityModel> Entities => CachedEntities;

    public HomeAssistantEntityModel? GetEntity(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return null;
        if (_entities.TryGetValue(entityId, out var entity)) return entity;
        return _entities.Values.FirstOrDefault(e => string.Equals(e.FriendlyName, entityId, StringComparison.OrdinalIgnoreCase));
    }

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<HomeAssistantEntityModel>? EntityStateChanged;
    public event EventHandler<IReadOnlyList<HomeAssistantEntityModel>>? EntitiesUpdated;

    public HomeAssistantService(IProfileService? profileService = null, HttpClient? httpClient = null)
    {
        _profileService = profileService;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(8);

        if (_profileService != null)
        {
            var s = _profileService.GetHomeAssistantSettings();
            if (!string.IsNullOrWhiteSpace(s.ServerUrl)) _serverUrl = s.ServerUrl;
            if (!string.IsNullOrWhiteSpace(s.AccessToken)) _accessToken = s.AccessToken;
        }
    }

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (_profileService != null)
        {
            var s = _profileService.GetHomeAssistantSettings();
            if (!string.IsNullOrWhiteSpace(s.ServerUrl)) _serverUrl = s.ServerUrl;
            if (!string.IsNullOrWhiteSpace(s.AccessToken)) _accessToken = s.AccessToken;
        }
        return await ConnectAsync(_serverUrl, _accessToken, ct).ConfigureAwait(false);
    }

    public async Task<bool> ConnectAsync(string serverUrl, string accessToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) return false;

        _serverUrl = serverUrl.Trim().TrimEnd('/');
        _accessToken = accessToken?.Trim() ?? "";

        _httpClient.DefaultRequestHeaders.Clear();
        if (!string.IsNullOrWhiteSpace(_accessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        try
        {
            // Sprawdź stan API REST
            var response = await _httpClient.GetAsync($"{_serverUrl}/api/", ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                IsConnected = true;
                _ = FetchEntitiesAsync(ct);
                StartWebSocketConnection();
                StartPollingTimer();
                return true;
            }
        }
        catch
        {
            // Błąd połączenia
        }

        IsConnected = false;
        return false;
    }

    public Task DisconnectAsync()
    {
        StopPollingTimer();
        StopWebSocket();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public async Task<List<HomeAssistantEntityModel>> FetchEntitiesAsync(CancellationToken ct = default)
    {
        if (!IsConnected && string.IsNullOrWhiteSpace(_accessToken))
            return new List<HomeAssistantEntityModel>();

        try
        {
            var response = await _httpClient.GetAsync($"{_serverUrl}/api/states", ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var list = JsonSerializer.Deserialize<List<HomeAssistantEntityModel>>(content);
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        _entities[item.EntityId] = item;
                    }
                    IsConnected = true;
                    var result = _entities.Values.ToList();
                    EntitiesUpdated?.Invoke(this, result);
                    return result;
                }
            }
        }
        catch
        {
            // Błąd pobierania
        }

        return _entities.Values.ToList();
    }

    public async Task<HomeAssistantEntityModel?> GetEntityStateAsync(string entityId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return null;

        if (_entities.TryGetValue(entityId, out var cached))
            return cached;

        try
        {
            var response = await _httpClient.GetAsync($"{_serverUrl}/api/states/{Uri.EscapeDataString(entityId)}", ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var entity = JsonSerializer.Deserialize<HomeAssistantEntityModel>(content);
                if (entity != null)
                {
                    _entities[entity.EntityId] = entity;
                    EntityStateChanged?.Invoke(this, entity);
                    return entity;
                }
            }
        }
        catch { }

        return null;
    }

    public async Task<bool> ToggleAsync(string entityId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return false;

        string domain = ResolveDomain(entityId);
        string service = "toggle";

        // Jeśli encja to scena, usługa to turn_on
        if (domain.Equals("scene", StringComparison.OrdinalIgnoreCase))
        {
            service = "turn_on";
        }

        bool result = await CallServiceAsync(domain, service, entityId, null, ct).ConfigureAwait(false);
        if (result && _entities.TryGetValue(entityId, out var entity))
        {
            entity.State = entity.IsOn ? "off" : "on";
            EntityStateChanged?.Invoke(this, entity);
        }
        return result;
    }

    public async Task<bool> TurnOnAsync(string entityId, int? brightness = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return false;

        string domain = ResolveDomain(entityId);
        object? data = brightness.HasValue ? new { entity_id = entityId, brightness = Math.Clamp(brightness.Value, 0, 255) } : null;

        bool result = await CallServiceAsync(domain, "turn_on", entityId, data, ct).ConfigureAwait(false);
        if (result && _entities.TryGetValue(entityId, out var entity))
        {
            entity.State = "on";
            if (brightness.HasValue) entity.Attributes.Brightness = brightness.Value;
            EntityStateChanged?.Invoke(this, entity);
        }
        return result;
    }

    public async Task<bool> TurnOffAsync(string entityId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return false;

        string domain = ResolveDomain(entityId);
        bool result = await CallServiceAsync(domain, "turn_off", entityId, null, ct).ConfigureAwait(false);
        if (result && _entities.TryGetValue(entityId, out var entity))
        {
            entity.State = "off";
            EntityStateChanged?.Invoke(this, entity);
        }
        return result;
    }

    public async Task<bool> SetLightBrightnessAsync(string entityId, int brightness, CancellationToken ct = default)
    {
        return await TurnOnAsync(entityId, brightness, ct).ConfigureAwait(false);
    }

    public async Task<bool> AdjustLightBrightnessAsync(string? entityId, int deltaBrightness, CancellationToken ct = default)
    {
        string? targetId = entityId;
        if (string.IsNullOrWhiteSpace(targetId))
        {
            targetId = _entities.Values.FirstOrDefault(e => e.Domain.Equals("light", StringComparison.OrdinalIgnoreCase))?.EntityId;
        }

        if (string.IsNullOrWhiteSpace(targetId)) return false;

        var current = await GetEntityStateAsync(targetId, ct).ConfigureAwait(false);
        int currentBrightness = current?.Brightness ?? (current?.IsOn == true ? 255 : 0);
        int newBrightness = Math.Clamp(currentBrightness + deltaBrightness, 0, 255);
        if (newBrightness == 0)
        {
            return await TurnOffAsync(targetId, ct).ConfigureAwait(false);
        }
        return await SetLightBrightnessAsync(targetId, newBrightness, ct).ConfigureAwait(false);
    }

    public async Task<bool> CallServiceAsync(string domain, string service, string? entityId = null, object? serviceData = null, CancellationToken ct = default)
    {
        try
        {
            string url = $"{_serverUrl}/api/services/{domain}/{service}";
            object payload;

            if (serviceData != null)
            {
                payload = serviceData;
            }
            else if (!string.IsNullOrWhiteSpace(entityId))
            {
                payload = new { entity_id = entityId };
            }
            else
            {
                payload = new { };
            }

            string json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveDomain(string entityId)
    {
        int dot = entityId.IndexOf('.');
        if (dot > 0)
        {
            return entityId.Substring(0, dot);
        }
        return "homeassistant";
    }

    private void StartPollingTimer()
    {
        StopPollingTimer();
        _pollTimer = new System.Threading.Timer(async _ =>
        {
            if (IsConnected)
            {
                await FetchEntitiesAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void StopPollingTimer()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private void StartWebSocketConnection()
    {
        StopWebSocket();

        _wsCts = new CancellationTokenSource();
        var token = _wsCts.Token;

        _wsReceiveTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && !_isDisposed)
            {
                try
                {
                    string wsScheme = _serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "wss://" : "ws://";
                    string hostAndPort = _serverUrl.Replace("https://", "").Replace("http://", "");
                    var wsUri = new Uri($"{wsScheme}{hostAndPort}/api/websocket");

                    using var ws = new ClientWebSocket();
                    _webSocket = ws;
                    await ws.ConnectAsync(wsUri, token).ConfigureAwait(false);

                    // 1. Odbiór auth_required
                    string authReqJson = await ReceiveWebSocketTextAsync(ws, token).ConfigureAwait(false);
                    if (authReqJson.Contains("auth_required"))
                    {
                        // 2. Wysłanie auth
                        string authMsg = JsonSerializer.Serialize(new { type = "auth", access_token = _accessToken });
                        await SendWebSocketTextAsync(ws, authMsg, token).ConfigureAwait(false);

                        // 3. Odbiór auth_ok
                        string authRes = await ReceiveWebSocketTextAsync(ws, token).ConfigureAwait(false);
                        if (!authRes.Contains("auth_ok"))
                        {
                            break;
                        }
                    }

                    // 4. Subskrypcja zdarzeń state_changed
                    string subMsg = JsonSerializer.Serialize(new { id = 1, type = "subscribe_events", event_type = "state_changed" });
                    await SendWebSocketTextAsync(ws, subMsg, token).ConfigureAwait(false);

                    // Pętla nasłuchu zdarzeń
                    while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                    {
                        string msg = await ReceiveWebSocketTextAsync(ws, token).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(msg)) continue;

                        try
                        {
                            using var doc = JsonDocument.Parse(msg);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("event", out var evProp) &&
                                evProp.TryGetProperty("data", out var dataProp) &&
                                dataProp.TryGetProperty("entity_id", out var idProp) &&
                                dataProp.TryGetProperty("new_state", out var stateProp))
                            {
                                string entityId = idProp.GetString() ?? "";
                                var updatedEntity = JsonSerializer.Deserialize<HomeAssistantEntityModel>(stateProp.GetRawText());
                                if (updatedEntity != null && !string.IsNullOrWhiteSpace(entityId))
                                {
                                    _entities[entityId] = updatedEntity;
                                    EntityStateChanged?.Invoke(this, updatedEntity);
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch
                {
                    // W razie utraty połączenia WebSocket, odczekaj 5s i spróbuj ponownie
                    try
                    {
                        await Task.Delay(5000, token).ConfigureAwait(false);
                    }
                    catch { }
                }
            }
        }, token);
    }

    private void StopWebSocket()
    {
        try
        {
            _wsCts?.Cancel();
            if (_webSocket?.State == WebSocketState.Open)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
                _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", closeCts.Token).GetAwaiter().GetResult();
            }
            _webSocket?.Dispose();
        }
        catch { }
        finally
        {
            _wsCts = null;
            _webSocket = null;
        }
    }

    private static async Task SendWebSocketTextAsync(ClientWebSocket ws, string text, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private static async Task<string> ReceiveWebSocketTextAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            var res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (res.MessageType == WebSocketMessageType.Close) break;
            ms.Write(buffer, 0, res.Count);
            if (res.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopPollingTimer();
        StopWebSocket();
        _httpClient.Dispose();
    }
}
