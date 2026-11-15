using System.Net.WebSockets;
using System.Text.Json;
using MasterHub.Core.Integrations.HomeAssistant;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MasterHub.Core.Tests.Integrations;

public class HomeAssistantIntegrationTests
{
    private sealed class HaMock
    {
        public const string Token = "long-lived-token";
        public readonly List<(string Path, string Body)> ServiceCalls = new();
        public WebSocket? Socket;
        public int WsAuthOk;
    }

    private static bool Authorized(HttpContext c) => c.Request.Headers.Authorization == $"Bearer {HaMock.Token}";

    private static Task<MockServer> Start(HaMock mock) => MockServer.StartAsync(app =>
    {
        app.MapGet("/api/", (HttpContext c) => Authorized(c) ? Results.Json(new { message = "API running." }) : Results.StatusCode(401));
        app.MapGet("/api/states", (HttpContext c) => !Authorized(c) ? Results.StatusCode(401) : Results.Json(new object[]
        {
            new { entity_id = "light.desk", state = "on", attributes = new { friendly_name = "Desk lamp", brightness = 100 } },
            new { entity_id = "scene.movie", state = "scening", attributes = new { friendly_name = "Movie" } }
        }));
        app.MapPost("/api/services/{domain}/{service}", async (HttpContext c, string domain, string service) =>
        {
            if (!Authorized(c)) return Results.StatusCode(401);
            using var reader = new StreamReader(c.Request.Body);
            string body = await reader.ReadToEndAsync();
            lock (mock.ServiceCalls) mock.ServiceCalls.Add(($"{domain}/{service}", body));
            return Results.Json(Array.Empty<object>());
        });
        app.Map("/api/websocket", async (HttpContext c) =>
        {
            if (!c.WebSockets.IsWebSocketRequest) { c.Response.StatusCode = 400; return; }
            using var ws = await c.WebSockets.AcceptWebSocketAsync();
            mock.Socket = ws;
            await MockServer.SendJsonAsync(ws, new { type = "auth_required", ha_version = "2025.1.0" });
            var auth = await MockServer.ReceiveJsonAsync(ws);
            if (auth?.GetProperty("type").GetString() != "auth" || auth.Value.GetProperty("access_token").GetString() != HaMock.Token)
            {
                await MockServer.SendJsonAsync(ws, new { type = "auth_invalid", message = "Invalid access token" });
                return;
            }
            await MockServer.SendJsonAsync(ws, new { type = "auth_ok", ha_version = "2025.1.0" });
            Interlocked.Increment(ref mock.WsAuthOk);
            var sub = await MockServer.ReceiveJsonAsync(ws);
            await MockServer.SendJsonAsync(ws, new { id = sub!.Value.GetProperty("id").GetInt32(), type = "result", success = true, result = (object?)null });
            while (ws.State == WebSocketState.Open)
            {
                try { if (await MockServer.ReceiveJsonAsync(ws, 30000) == null) break; } catch { break; }
            }
        });
    });

    [Fact]
    public async Task Connect_ValidToken_LoadsEntities_AndReceivesLiveStateChanges()
    {
        var mock = new HaMock();
        await using var server = await Start(mock);
        using var ha = new HomeAssistantService();
        var changes = new List<HomeAssistantEntityModel>();
        ha.EntityStateChanged += (_, e) => { lock (changes) changes.Add(e); };

        Assert.True(await ha.ConnectAsync(server.BaseUrl, HaMock.Token));
        Assert.True(await ObsIntegrationTests.WaitFor(() => ha.CachedEntities.Count == 2));
        Assert.Equal("Desk lamp", ha.GetEntity("light.desk")!.FriendlyName);
        Assert.Equal(100, ha.GetEntity("light.desk")!.Brightness);

        // WebSocket: zdarzenie state_changed z serwera
        Assert.True(await ObsIntegrationTests.WaitFor(() => mock.Socket != null && mock.WsAuthOk == 1));
        await Task.Delay(100);
        await MockServer.SendJsonAsync(mock.Socket!, new
        {
            id = 1,
            type = "event",
            @event = new
            {
                event_type = "state_changed",
                data = new
                {
                    entity_id = "light.desk",
                    new_state = new { entity_id = "light.desk", state = "off", attributes = new { friendly_name = "Desk lamp" } }
                }
            }
        });
        Assert.True(await ObsIntegrationTests.WaitFor(() => ha.GetEntity("light.desk")!.State == "off"));
        Assert.Contains(changes, e => e.EntityId == "light.desk" && !e.IsOn);
    }

    [Fact]
    public async Task Connect_InvalidToken_ReturnsFalse()
    {
        await using var server = await Start(new HaMock());
        using var ha = new HomeAssistantService();

        Assert.False(await ha.ConnectAsync(server.BaseUrl, "wrong-token"));
        Assert.False(ha.IsConnected);
    }

    [Fact]
    public async Task ServiceCalls_UseCorrectDomainServiceAndPayload()
    {
        var mock = new HaMock();
        await using var server = await Start(mock);
        using var ha = new HomeAssistantService();
        Assert.True(await ha.ConnectAsync(server.BaseUrl, HaMock.Token));
        await ObsIntegrationTests.WaitFor(() => ha.CachedEntities.Count == 2);

        Assert.True(await ha.ToggleAsync("light.desk"));
        Assert.True(await ha.ToggleAsync("scene.movie"));      // scena -> turn_on, nie toggle
        Assert.True(await ha.TurnOnAsync("light.desk", 999));  // jasność przycięta do 255
        Assert.True(await ha.TurnOffAsync("light.desk"));

        lock (mock.ServiceCalls)
        {
            Assert.Equal("light/toggle", mock.ServiceCalls[0].Path);
            Assert.Equal("light.desk", JsonDocument.Parse(mock.ServiceCalls[0].Body).RootElement.GetProperty("entity_id").GetString());
            Assert.Equal("scene/turn_on", mock.ServiceCalls[1].Path);
            Assert.Equal("light/turn_on", mock.ServiceCalls[2].Path);
            Assert.Equal(255, JsonDocument.Parse(mock.ServiceCalls[2].Body).RootElement.GetProperty("brightness").GetInt32());
            Assert.Equal("light/turn_off", mock.ServiceCalls[3].Path);
        }
    }

    [Fact]
    public async Task Poll_ReconnectsAfterConnectionWasLost()
    {
        await using var server = await Start(new HaMock());
        using var ha = new HomeAssistantService();
        Assert.True(await ha.ConnectAsync(server.BaseUrl, HaMock.Token));
        await ha.DisconnectAsync();
        Assert.False(ha.IsConnected);

        await ha.PollOnceAsync();   // cykl odpytywania ma sam przywrócić połączenie

        Assert.True(ha.IsConnected);
    }

    [Fact]
    public async Task CallService_WhenNotConnected_ReturnsFalse()
    {
        using var ha = new HomeAssistantService();
        Assert.False(await ha.ToggleAsync("light.desk"));
    }
}
