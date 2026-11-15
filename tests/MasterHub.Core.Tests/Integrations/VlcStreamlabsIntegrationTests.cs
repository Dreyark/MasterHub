using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Profiles;
using MasterHub.Core.SystemControl;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MasterHub.Core.Tests.Integrations;

public class VlcStreamlabsIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_int_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>ISystemControlService, który niczego nie robi (VLC używa go tylko w trybie awaryjnym).</summary>
    public class NoopProxy : DispatchProxy
    {
        public int Calls;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Interlocked.Increment(ref Calls);
            var rt = targetMethod!.ReturnType;
            if (rt == typeof(void)) return null;
            if (rt == typeof(Task)) return Task.CompletedTask;
            return rt.IsValueType ? Activator.CreateInstance(rt) : null;
        }
    }

    // ---------------------------------------------------------------- VLC

    private sealed class VlcMock
    {
        public const string Password = "vlcpass";
        public readonly List<string?> Commands = new();
        public string State = "playing";
    }

    private static Task<MockServer> StartVlc(VlcMock mock) => MockServer.StartAsync(app =>
    {
        app.MapGet("/requests/status.json", (HttpContext c) =>
        {
            string expected = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + VlcMock.Password));
            if (c.Request.Headers.Authorization != expected) return Results.StatusCode(401);
            string? cmd = c.Request.Query["command"];
            if (cmd != null)
            {
                lock (mock.Commands) mock.Commands.Add(cmd);
                if (cmd == "pl_pause") mock.State = mock.State == "playing" ? "paused" : "playing";
                if (cmd == "pl_stop") mock.State = "stopped";
            }
            return Results.Json(new
            {
                version = "3.0.20 Vetinari",
                state = mock.State,
                fullscreen = false,
                volume = 256,
                information = new { category = new { meta = new { title = "Song A", artist = "Artist X" } } }
            });
        });
    });

    private VlcService CreateVlc(int port, string password)
    {
        var profiles = new ProfileService(_dir);
        profiles.SaveVlcSettings(new VlcSettingsModel
        {
            Host = MockServer.Host,
            Port = port,
            Password = password,
            AutoConnect = false,
            FallbackToDesktopHotkeys = false
        });
        var sys = DispatchProxy.Create<ISystemControlService, NoopProxy>();
        return new VlcService(profiles, sys);
    }

    [Fact]
    public async Task Vlc_TestConnection_ParsesStatus_AndUsesEmptyUserBasicAuth()
    {
        var mock = new VlcMock();
        await using var server = await StartVlc(mock);
        using var vlc = CreateVlc(server.Port, VlcMock.Password);

        var (ok, message) = await vlc.TestConnectionDetailedAsync();

        Assert.True(ok, message);
        Assert.True(vlc.IsConnected);
        Assert.True(vlc.IsPlaying);
        Assert.Equal("Song A", vlc.CurrentTitle);
        Assert.Equal("Artist X", vlc.CurrentArtist);
        Assert.Equal(100, vlc.VolumePercent);   // VLC: 256 = 100%
    }

    [Fact]
    public async Task Vlc_WrongPassword_ReportsUnauthorized()
    {
        await using var server = await StartVlc(new VlcMock());
        using var vlc = CreateVlc(server.Port, "bad");

        var (ok, message) = await vlc.TestConnectionDetailedAsync();

        Assert.False(ok);
        Assert.False(vlc.IsConnected);
        Assert.Contains("401", message);
    }

    [Fact]
    public async Task Vlc_Commands_AreSentAsHttpInterfaceCommands()
    {
        var mock = new VlcMock();
        await using var server = await StartVlc(mock);
        using var vlc = CreateVlc(server.Port, VlcMock.Password);

        Assert.True(await vlc.PlayPauseAsync());
        Assert.True(await vlc.NextTrackAsync());
        Assert.True(await vlc.PreviousTrackAsync());
        Assert.True(await vlc.StopAsync());
        Assert.True(await vlc.ToggleFullscreenAsync());

        Assert.Equal(new[] { "pl_pause", "pl_next", "pl_previous", "pl_stop", "fullscreen" }, mock.Commands.ToArray());
    }

    // ---------------------------------------------------------------- Streamlabs

    private sealed class SlobsMock
    {
        public const string Token = "slobs-token";
        public readonly List<string> Methods = new();
        public string Active = "scene_1";
        public WebSocket? Socket;
    }

    private static readonly (string Id, string Name)[] Scenes = { ("scene_1", "Gameplay"), ("scene_2", "Just Chatting") };

    private static Task<MockServer> StartSlobs(SlobsMock mock) => MockServer.StartAsync(app =>
    {
        app.Map("/api/websocket", async (HttpContext c) =>
        {
            if (!c.WebSockets.IsWebSocketRequest) { c.Response.StatusCode = 400; return; }
            using var ws = await c.WebSockets.AcceptWebSocketAsync();
            mock.Socket = ws;
            while (ws.State == WebSocketState.Open)
            {
                JsonElement? msg;
                try { msg = await MockServer.ReceiveJsonAsync(ws, 30000); } catch { break; }
                if (msg == null) break;
                var root = msg.Value;
                int id = root.GetProperty("id").GetInt32();
                string method = root.GetProperty("method").GetString()!;
                var p = root.GetProperty("params");
                string resource = p.GetProperty("resource").GetString()!;
                var args = p.TryGetProperty("args", out var a) ? a : default;
                lock (mock.Methods) mock.Methods.Add($"{resource}.{method}");

                object? result = (resource, method) switch
                {
                    ("TcpServerService", "auth") => args.GetArrayLength() > 0 && args[0].GetString() == SlobsMock.Token,
                    ("ScenesService", "getScenes") => Scenes.Select(s => new { id = s.Id, name = s.Name }).ToArray(),
                    ("ScenesService", "activeScene") => Scenes.Where(s => s.Id == mock.Active).Select(s => new { id = s.Id, name = s.Name }).First(),
                    ("ScenesService", "makeSceneActive") => SwitchScene(mock, args[0].GetString()!),
                    ("ScenesService", "sceneSwitched") => new { _type = "SUBSCRIPTION", resourceId = "ScenesService.sceneSwitched" },
                    _ => null
                };
                await MockServer.SendJsonAsync(ws, new { jsonrpc = "2.0", id, result });
            }
        });
    });

    private static bool SwitchScene(SlobsMock mock, string id)
    {
        if (Scenes.All(s => s.Id != id)) return false;
        mock.Active = id;
        return true;
    }

    [Fact]
    public async Task Streamlabs_Connect_Auth_ListsScenes_AndSwitchesByNameOrId()
    {
        var mock = new SlobsMock();
        await using var server = await StartSlobs(mock);
        using var slobs = new StreamlabsService();

        Assert.True(await slobs.ConnectAsync(MockServer.Host, server.Port, SlobsMock.Token));
        Assert.Equal("Gameplay", await slobs.GetActiveSceneAsync());
        Assert.Contains("TcpServerService.auth", mock.Methods);
        Assert.Contains("ScenesService.sceneSwitched", mock.Methods);   // subskrypcja zdarzeń

        Assert.True(await slobs.SwitchSceneAsync("Just Chatting"));      // po nazwie -> id sceny
        Assert.Equal("scene_2", mock.Active);
        Assert.True(await slobs.SwitchSceneAsync("scene_1"));            // po id
        Assert.Equal("scene_1", mock.Active);
        Assert.False(await slobs.SwitchSceneAsync("Nie ma takiej"));
    }

    [Fact]
    public async Task Streamlabs_ServerSceneSwitchedEvent_UpdatesActiveScene()
    {
        var mock = new SlobsMock();
        await using var server = await StartSlobs(mock);
        using var slobs = new StreamlabsService();
        string? changedTo = null;
        slobs.ActiveSceneChanged += n => changedTo = n;
        Assert.True(await slobs.ConnectAsync(MockServer.Host, server.Port, SlobsMock.Token));

        await MockServer.SendJsonAsync(mock.Socket!, new
        {
            jsonrpc = "2.0",
            result = new { _type = "EVENT", resourceId = "ScenesService.sceneSwitched", data = new { id = "scene_2", name = "Just Chatting" } }
        });

        Assert.True(await ObsIntegrationTests.WaitFor(() => changedTo == "Just Chatting"));
    }

    [Fact]
    public async Task Streamlabs_WrongToken_IsRejected()
    {
        await using var server = await StartSlobs(new SlobsMock());
        using var slobs = new StreamlabsService();

        Assert.False(await slobs.ConnectAsync(MockServer.Host, server.Port, "wrong"));
        Assert.False(slobs.IsConnected);
    }
}
