using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasterHub.Core.Integrations.Obs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MasterHub.Core.Tests.Integrations;

/// <summary>Prawdziwy klient ObsService rozmawiający z atrapą serwera obs-websocket v5.</summary>
public class ObsIntegrationTests
{
    internal static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs = 4000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    private static string SpecAuth(string password, string salt, string challenge)
    {
        // obs-websocket 5.x: Base64(SHA256(Base64(SHA256(password + salt)) + challenge))
        string secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private sealed class ObsMock
    {
        public string Password = "secret";
        public JsonElement? Identify;
        public readonly List<string> RequestTypes = new();
        public WebSocket? Socket;
    }

    private static Task<MockServer> Start(ObsMock mock) => MockServer.StartAsync(app =>
    {
        app.Map("/", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            mock.Socket = ws;
            await MockServer.SendJsonAsync(ws, new
            {
                op = 0,
                d = new { obsWebSocketVersion = "5.5.0", rpcVersion = 1, authentication = new { challenge = "chal-123", salt = "salt-456" } }
            });

            var identify = await MockServer.ReceiveJsonAsync(ws);
            mock.Identify = identify;
            string? sent = identify?.GetProperty("d").TryGetProperty("authentication", out var a) == true ? a.GetString() : null;
            if (sent != SpecAuth(mock.Password, "salt-456", "chal-123"))
            {
                await ws.CloseAsync((WebSocketCloseStatus)4009, "Authentication failed.", CancellationToken.None);
                return;
            }
            await MockServer.SendJsonAsync(ws, new { op = 2, d = new { negotiatedRpcVersion = 1 } });

            while (ws.State == WebSocketState.Open)
            {
                JsonElement? msg;
                try { msg = await MockServer.ReceiveJsonAsync(ws, 30000); } catch { break; }
                if (msg == null) break;
                if (msg.Value.GetProperty("op").GetInt32() != 6) continue;
                var d = msg.Value.GetProperty("d");
                string type = d.GetProperty("requestType").GetString()!;
                string id = d.GetProperty("requestId").GetString()!;
                lock (mock.RequestTypes) mock.RequestTypes.Add(type);

                object data = type switch
                {
                    "GetStreamStatus" => new { outputActive = false },
                    "ToggleStream" => new { outputActive = true },
                    "GetCurrentProgramScene" => new { currentProgramSceneName = "Gameplay" },
                    "GetRecordStatus" => new { outputActive = false },
                    "GetStudioModeEnabled" => new { studioModeEnabled = false },
                    _ => new { }
                };
                await MockServer.SendJsonAsync(ws, new
                {
                    op = 7,
                    d = new { requestType = type, requestId = id, requestStatus = new { result = true, code = 100 }, responseData = data }
                });
            }
        });
    });

    [Fact]
    public async Task Connect_WithPassword_UsesSpecAuth_RunsRequests_AndTracksEvents()
    {
        var mock = new ObsMock();
        await using var server = await Start(mock);
        using var obs = new ObsService();

        bool connected = await obs.ConnectAsync(MockServer.Host, server.Port, "secret");

        Assert.True(connected, obs.LastError);
        Assert.Equal("5.5.0", obs.ObsWebSocketVersion);
        var identifyD = mock.Identify!.Value.GetProperty("d");
        Assert.Equal(1, identifyD.GetProperty("rpcVersion").GetInt32());
        Assert.Equal(2047, identifyD.GetProperty("eventSubscriptions").GetInt32());

        // stan początkowy pobierany asynchronicznie po połączeniu
        Assert.True(await WaitFor(() => obs.CurrentProgramSceneName == "Gameplay"));

        Assert.True(await obs.ToggleStreamAsync());
        Assert.True(obs.IsStreaming);
        Assert.Contains("ToggleStream", mock.RequestTypes);

        // zdarzenie serwera przestawia stan klienta
        await MockServer.SendJsonAsync(mock.Socket!, new
        {
            op = 5,
            d = new { eventType = "StreamStateChanged", eventIntent = 64, eventData = new { outputActive = false, outputState = "OBS_WEBSOCKET_OUTPUT_STOPPED" } }
        });
        Assert.True(await WaitFor(() => !obs.IsStreaming));

        await MockServer.SendJsonAsync(mock.Socket!, new
        {
            op = 5,
            d = new { eventType = "CurrentProgramSceneChanged", eventIntent = 4, eventData = new { sceneName = "Starting Soon" } }
        });
        Assert.True(await WaitFor(() => obs.CurrentProgramSceneName == "Starting Soon"));
    }

    [Fact]
    public async Task Connect_WithWrongPassword_Fails_AndExplainsWhy()
    {
        var mock = new ObsMock { Password = "right-password" };
        await using var server = await Start(mock);
        using var obs = new ObsService();

        bool connected = await obs.ConnectAsync(MockServer.Host, server.Port, "wrong-password");

        Assert.False(connected);
        Assert.False(obs.IsConnected);
        Assert.Contains("4009", obs.LastError ?? "");
    }

    [Fact]
    public async Task Connect_ServerRequiresPassword_ButNoneConfigured_FailsWithMessage()
    {
        var mock = new ObsMock();
        await using var server = await Start(mock);
        using var obs = new ObsService();

        bool connected = await obs.ConnectAsync(MockServer.Host, server.Port, "");

        Assert.False(connected);
        Assert.Contains("hasła", obs.LastError ?? "");
    }

    [Fact]
    public async Task Requests_WhenDisconnected_ReturnFalseWithoutThrowing()
    {
        using var obs = new ObsService();
        Assert.False(await obs.ToggleStreamAsync());
        Assert.False(await obs.StartRecordAsync());
    }
}
