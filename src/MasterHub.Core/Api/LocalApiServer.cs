using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Api.Client;
using MasterHub.Core.Devices;
using MasterHub.Core.Profiles;
using MasterHub.Core.Security;

namespace MasterHub.Core.Api;

/// <summary>Loopback-only HTTP API of the running app (T-04): a CLI, scripts or an MCP server can read and drive the deck.</summary>
public interface ILocalApiServer : IDisposable
{
    bool IsRunning { get; }

    /// <summary>Port the server listens on (0 when stopped).</summary>
    int Port { get; }

    /// <summary>Starts listening and publishes <c>api-port</c> and <c>api-token</c> in the data folder.</summary>
    void Start();

    /// <summary>Stops listening and removes the port/token files.</summary>
    void Stop();
}

public sealed class LocalApiOptions
{
    /// <summary>First port to try; 0 = pick a free port (tests).</summary>
    public int PreferredPort { get; set; } = 28900;

    /// <summary>How many consecutive ports are tried when the preferred one is taken.</summary>
    public int PortRange { get; set; } = 20;

    /// <summary>Largest accepted request body (16 MB).</summary>
    public long MaxBodyBytes { get; set; } = 16L * 1024 * 1024;

    /// <summary>Folder that receives <c>api-port</c> / <c>api-token</c> (default <c>%APPDATA%\MasterHub</c>).</summary>
    public string? BaseDirectory { get; set; }
}

/// <inheritdoc cref="ILocalApiServer"/>
/// <remarks>
/// Security: only <c>127.0.0.1</c>; every request needs <c>Authorization: Bearer &lt;token&gt;</c> (a per-install random token,
/// stored DPAPI-protected – a CLI of the same Windows user can unprotect it); a request with an <c>Origin</c> header is
/// rejected (a web page must never drive the deck, cf. F-04) and so is any <c>Host</c> other than the loopback address
/// with this port (DNS rebinding); bodies are capped; only JSON is accepted.
/// </remarks>
public sealed class LocalApiServer : ILocalApiServer
{
    private readonly IProfileService _profiles;
    private readonly ISecretProtectionService _secrets;
    private readonly IMasterHubManager? _hardware;
    private readonly ISmartProfileService? _smart;
    private readonly LocalApiOptions _options;
    private readonly object _lock = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private string _token = "";
    private string _portFile = "";
    private string _tokenFile = "";

    public LocalApiServer(IProfileService profiles, ISecretProtectionService secrets, IMasterHubManager? hardware = null,
        ISmartProfileService? smartProfiles = null, LocalApiOptions? options = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _hardware = hardware;
        _smart = smartProfiles;
        _options = options ?? new LocalApiOptions();
    }

    /// <summary>The running app plugs itself in here (UI-thread work, the Studio's in-memory edits).</summary>
    public ILocalApiHost? Host { get; set; }

    /// <summary>What connected apps report (T-25). Without it the status routes answer 503.</summary>
    public MasterHub.Core.Status.IStatusBoard? Status { get; set; }

    /// <summary>The Claude Code sessions (T-25).</summary>
    public MasterHub.Core.Status.IAgentTracker? Agents { get; set; }

    public bool IsRunning
    {
        get { lock (_lock) { return _listener?.IsListening == true; } }
    }

    public int Port { get; private set; }

    /// <summary>The bearer token (for tests; the app itself never shows it).</summary>
    internal string Token => _token;

    public void Start()
    {
        lock (_lock)
        {
            if (_listener != null) return;

            string baseDir = _options.BaseDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub");
            Directory.CreateDirectory(baseDir);
            _portFile = Path.Combine(baseDir, "api-port");
            _tokenFile = Path.Combine(baseDir, "api-token");

            (_listener, Port) = Listen();
            _token = Base64Url(RandomNumberGenerator.GetBytes(32));

            // Token: DPAPI-protected, readable by processes of the same Windows user only.
            File.WriteAllText(_tokenFile, _secrets.Protect(_token) ?? _token);
            File.WriteAllText(_portFile, Port.ToString());

            _cts = new CancellationTokenSource();
            var listener = _listener;
            var token = _cts.Token;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, token));
        }
    }

    public void Stop()
    {
        HttpListener? listener;
        CancellationTokenSource? cts;
        Task? loop;
        lock (_lock)
        {
            listener = _listener;
            cts = _cts;
            loop = _acceptLoop;
            _listener = null;
            _cts = null;
            _acceptLoop = null;
            Port = 0;
        }

        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        try { listener?.Close(); } catch { }
        try { loop?.Wait(1000); } catch { }
        cts?.Dispose();

        TryDelete(_portFile);
        TryDelete(_tokenFile);
    }

    public void Dispose() => Stop();

    // ── listening ───────────────────────────────────────────────────────────────────────────

    private (HttpListener Listener, int Port) Listen()
    {
        int first = _options.PreferredPort > 0 ? _options.PreferredPort : FreePort();
        int count = _options.PreferredPort > 0 ? Math.Max(1, _options.PortRange) : 1;

        for (int port = first; port < first + count; port++)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                return (listener, port);
            }
            catch (HttpListenerException)
            {
                try { listener.Close(); } catch { }
            }
        }

        throw new IOException($"The local API could not bind to 127.0.0.1 ports {first}–{first + count - 1}.");
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                break; // listener stopped: graceful shutdown, nothing to report
            }

            _ = Task.Run(() => HandleAsync(context, ct), CancellationToken.None);
        }
    }

    // ── one request ─────────────────────────────────────────────────────────────────────────

    private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        var response = context.Response;
        try
        {
            var (status, payload) = await ProcessAsync(context.Request, ct).ConfigureAwait(false);
            await WriteJsonAsync(response, status, payload).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
            // client went away
        }
        catch (Exception ex)
        {
            // The details (file paths, IO errors) go to the log, not to whoever called the API.
            MasterHub.Core.Diagnostics.AppLog.Write("[API] internal error: " + ex.Message);
            try { await WriteJsonAsync(response, 500, new { error = "internal error" }).ConfigureAwait(false); } catch { }
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    private async Task<(int Status, object? Payload)> ProcessAsync(HttpListenerRequest request, CancellationToken ct)
    {
        // 1. Host: only the loopback address with this port (DNS rebinding).
        string host = request.Headers["Host"] ?? "";
        if (host != $"127.0.0.1:{Port}" && host != $"localhost:{Port}")
            return (403, new { error = "forbidden host" });

        // 2. Origin: a browser page must never reach the API.
        if (!string.IsNullOrEmpty(request.Headers["Origin"]))
            return (403, new { error = "requests with an Origin header are rejected" });

        // 3. Bearer token, compared in constant time.
        string auth = request.Headers["Authorization"] ?? "";
        const string prefix = "Bearer ";
        bool authorised = auth.StartsWith(prefix, StringComparison.Ordinal) &&
                          CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(auth[prefix.Length..]), Encoding.UTF8.GetBytes(_token));
        if (!authorised)
        {
            return (401, new { error = "missing or wrong bearer token" });
        }

        // 4. Body: size cap and JSON only.
        string? body = null;
        if (request.HasEntityBody)
        {
            if (request.ContentLength64 > _options.MaxBodyBytes) return (413, new { error = "request body too large" });
            string contentType = request.ContentType ?? "";
            if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                return (415, new { error = "Content-Type must be application/json" });

            var (bytes, tooLarge) = await ReadBodyAsync(request, _options.MaxBodyBytes, ct).ConfigureAwait(false);
            if (tooLarge) return (413, new { error = "request body too large" });
            body = Encoding.UTF8.GetString(bytes!);
        }

        var segments = request.Url!.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var assistant = AssistantLimits.FromHeader(request.Headers[AssistantLimits.Header]);
        return Route(request.HttpMethod.ToUpperInvariant(), segments, body, assistant);
    }

    private static async Task<(byte[]? Bytes, bool TooLarge)> ReadBodyAsync(HttpListenerRequest request, long max, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await request.InputStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > max) return (null, true);
            ms.Write(buffer, 0, read);
        }
        return (ms.ToArray(), false);
    }

    // ── routes (v1) ─────────────────────────────────────────────────────────────────────────

    private (int Status, object? Payload) Route(string method, string[] seg, string? body, AssistantLimits? assistant)
    {
        if (seg.Length < 2 || seg[0] != "api") return (404, new { error = "not found" });

        switch (seg[1])
        {
            case "state" when seg.Length == 2 && method == "GET":
                return (200, GetState());
            case "modules" when seg.Length == 2 && method == "GET":
                return (200, GetModules());
            case "controls" when seg.Length == 2 && method == "GET":
                return (200, GetControls());
            case "leds" when seg.Length == 2 && method == "GET":
                return (200, new { brightness = _hardware?.CurrentBrightness, standby = _hardware?.IsStandby });
            case "version" when seg.Length == 2 && method == "GET":
                return (200, new ApiVersion("MasterHub", AppVersion(), ApiVersion.Current));
            case "status" when seg.Length == 2 && method == "GET":
                return GetStatus();
            case "state" when seg.Length == 2 && method == "POST":
                return PostState(body);
            case "agent" when seg.Length == 2 && method == "POST":
                return PostAgent(body);
            case "lights" when seg.Length == 2:
                return method == "POST" ? PostLights(body) : (405, new { error = "method not allowed" });
            case "profiles":
                return RouteProfiles(method, seg, body, assistant);
        }

        return (404, new { error = "not found" });
    }

    private (int Status, object? Payload) RouteProfiles(string method, string[] seg, string? body, AssistantLimits? assistant)
    {
        if (seg.Length == 2)
        {
            return method == "GET"
                ? (200, new { profiles = _profiles.GetAllProfiles().Select(p => new { id = p.Id, name = p.Name, isDefault = p.IsDefault }) })
                : (405, new { error = "method not allowed" });
        }

        string id = Uri.UnescapeDataString(seg[2]);
        var profile = _profiles.GetAllProfiles().FirstOrDefault(p => p.Id == id);
        if (profile == null) return (404, new { error = $"profile '{id}' not found" });

        if (seg.Length == 3)
        {
            if (method != "GET") return (405, new { error = "method not allowed" });
            // The export strips every credential, so a token holder never reads another service's secrets.
            using var doc = JsonDocument.Parse(_profiles.ExportProfileToJson(id));
            return (200, doc.RootElement.Clone());
        }

        if (seg.Length == 4 && seg[3] == "activate" && method == "POST")
        {
            bool ok = Host?.ActivateProfile(id) ?? false;
            if (!ok && Host == null)
            {
                _profiles.SetActiveProfile(id);
                ok = true;
            }
            return ok ? (200, new { activated = id }) : (404, new { error = $"profile '{id}' not found" });
        }

        if (seg.Length == 4 && seg[3] == "controls" && method == "POST")
            return PostControls(profile, body, assistant);

        return (404, new { error = "not found" });
    }

    private (int Status, object? Payload) PostControls(ProfileModel profile, string? body, AssistantLimits? assistant)
    {
        ControlsRequest? request;
        try { request = body == null ? null : JsonSerializer.Deserialize<ControlsRequest>(body, ApiJson.Options); }
        catch (JsonException ex) { return (400, new { error = "invalid JSON", detail = ex.Message }); }

        var validation = ProfileValidator.Validate(request?.Controls, profile.Pages?.Count ?? 1, assistant);
        if (!validation.IsValid)
            return (422, new { error = "validation failed – nothing was changed", errors = validation.Errors, warnings = validation.Warnings });

        // Flush the Studio first, snapshot, then write: all-or-nothing and always undoable from Settings > Backups.
        Host?.FlushActiveProfile();
        profile = _profiles.GetAllProfiles().FirstOrDefault(p => p.Id == profile.Id) ?? profile;
        _profiles.Backups.CreateSnapshot("before-replace");

        ProfileValidator.Apply(profile, request!.Controls);
        _profiles.SaveProfile(profile);
        Host?.ProfileChanged(profile.Id);

        return (200, new { changed = request.Controls.Count, warnings = validation.Warnings });
    }

    private (int Status, object? Payload) GetStatus()
    {
        if (Status == null) return (503, new { error = "the status board is not available" });

        return (200, new
        {
            items = Status.Items.Select(i => new { source = i.Source, id = i.Id, state = i.State.ToString().ToLowerInvariant(), label = i.Label, text = i.Text, value = i.Value }),
            waiting = Status.Count(MasterHub.Core.Status.AppState.Waiting)
        });
    }

    private (int Status, object? Payload) PostState(string? body)
    {
        if (Status == null) return (503, new { error = "the status board is not available" });

        MasterHub.Core.Status.StateRequest? request;
        try { request = body == null ? null : JsonSerializer.Deserialize<MasterHub.Core.Status.StateRequest>(body, ApiJson.Options); }
        catch (JsonException ex) { return (400, new { error = "invalid JSON", detail = ex.Message }); }

        var (items, source, errors) = MasterHub.Core.Status.StatusRequests.Validate(request, DateTime.UtcNow);
        if (items == null) return (422, new { error = "validation failed – nothing was changed", errors });

        Status.Replace(source!, items, MasterHub.Core.Status.StatusRequests.PushedLifetime);
        return (200, new { source, items = items.Count });
    }

    private (int Status, object? Payload) PostAgent(string? body)
    {
        if (Agents == null) return (503, new { error = "the Claude Code status is not available" });

        MasterHub.Core.Status.AgentRequest? request;
        try { request = body == null ? null : JsonSerializer.Deserialize<MasterHub.Core.Status.AgentRequest>(body, ApiJson.Options); }
        catch (JsonException ex) { return (400, new { error = "invalid JSON", detail = ex.Message }); }

        var (agentEvent, errors) = MasterHub.Core.Status.StatusRequests.Validate(request);
        if (agentEvent == null || !Agents.Apply(agentEvent)) return (422, new { error = "validation failed – nothing was changed", errors });

        return (200, new { sessions = Agents.Sessions.Count });
    }

    private (int Status, object? Payload) PostLights(string? body)
    {
        LightsRequest? request;
        try { request = body == null ? null : JsonSerializer.Deserialize<LightsRequest>(body, ApiJson.Options); }
        catch (JsonException ex) { return (400, new { error = "invalid JSON", detail = ex.Message }); }

        var (mode, color, validation) = ProfileValidator.ValidateLights(request);
        if (!validation.IsValid)
            return (422, new { error = "validation failed – nothing was changed", errors = validation.Errors, warnings = validation.Warnings });

        if (Host == null || !Host.SetLights(mode, color))
            return (409, new { error = "the app could not change the lights (no deck connected, or the app is busy)" });

        return (200, new { lights = mode.ToString().ToLowerInvariant(), color });
    }

    private static string AppVersion() =>
        typeof(LocalApiServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private object GetState()
    {
        var active = _profiles.GetActiveProfile();
        return new
        {
            activeProfileId = active.Id,
            profiles = _profiles.GetAllProfiles().Select(p => new { id = p.Id, name = p.Name, isDefault = p.IsDefault }),
            foreground = _smart == null ? null : new { process = _smart.LastForegroundProcess, title = _smart.LastForegroundTitle },
            autoSwitch = _smart?.IsEnabled
        };
    }

    private object GetModules() => new
    {
        keys = _hardware?.IsKeypadConnected ?? false,
        faders = _hardware?.IsSliderConnected ?? false,
        rollers = _hardware?.IsRollerConnected ?? false,
        knobs = _hardware?.IsKnobConnected ?? false,
        dial = _hardware?.IsDialConnected ?? false,
        baseStation = _hardware?.IsBaseConnected ?? false,
        devices = (_hardware?.ConnectedDevices ?? new()).Select(d => new
        {
            name = d.Name,
            // Same rule as the diagnostics report: serial numbers are reduced to their last 4 characters.
            serial = string.IsNullOrEmpty(d.SerialNumber) ? "" : "…" + d.SerialNumber[^Math.Min(4, d.SerialNumber.Length)..],
            connected = d.IsConnected
        })
    };

    private object GetControls() => new
    {
        faders = _hardware?.CurrentSliderValues.Select(v => (int)v) ?? Enumerable.Empty<int>(),
        keysPressed = _hardware?.CurrentKeyStates ?? Array.Empty<bool>() as IReadOnlyList<bool>,
        standby = _hardware?.IsStandby ?? false
    };

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    private static async Task WriteJsonAsync(HttpListenerResponse response, int status, object? payload)
    {
        response.StatusCode = status;
        if (status == 401) response.AddHeader("WWW-Authenticate", "Bearer");
        response.ContentType = "application/json; charset=utf-8";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload ?? new { }, ApiJson.Options);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void TryDelete(string path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
    }
}
