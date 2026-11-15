using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Security;

namespace MasterHub.Core.Api.Client;

/// <summary>MasterHub is not running (or not reachable): the CLI says so plainly and exits with 1.</summary>
public sealed class MasterHubNotRunningException : Exception
{
    public MasterHubNotRunningException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The app answered, but not with success. <see cref="Exception.Message"/> already carries the reason the app gave.</summary>
public sealed class ApiException : Exception
{
    public ApiException(int status, string message, IReadOnlyList<string>? errors = null, IReadOnlyList<string>? warnings = null) : base(message)
    {
        Status = status;
        Errors = errors ?? Array.Empty<string>();
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>HTTP status (0 = no answer in time).</summary>
    public int Status { get; }

    /// <summary>Everything the validator found wrong (422): the request was refused as a whole.</summary>
    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>Where the running app listens and the token it wants (<c>api-port</c> and <c>api-token</c> in its data folder).</summary>
public sealed record ApiConnection(int Port, string Token)
{
    /// <summary>The data folder the app writes its port and token to.</summary>
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub");

    /// <summary>
    /// Reads the connection fresh (never cached: the app picks a new token at every start). Throws <see cref="MasterHubNotRunningException"/>
    /// when the files are missing or unreadable, which is what a closed app looks like.
    /// </summary>
    public static ApiConnection Read(string? directory = null, ISecretProtectionService? secrets = null)
    {
        directory ??= DefaultDirectory;
        string portFile = Path.Combine(directory, "api-port");
        string tokenFile = Path.Combine(directory, "api-token");

        if (!File.Exists(portFile) || !File.Exists(tokenFile))
            throw new MasterHubNotRunningException($"MasterHub is not running (no api-port / api-token in {directory}). Start the app first.");

        try
        {
            if (!int.TryParse(File.ReadAllText(portFile).Trim(), out int port) || port is < 1 or > 65535)
                throw new MasterHubNotRunningException("MasterHub is not running (the api-port file is not a port number).");

            string token = (secrets ?? new SecretProtectionService()).Unprotect(File.ReadAllText(tokenFile).Trim()) ?? "";
            if (token.Length == 0)
                throw new MasterHubNotRunningException("MasterHub is not running (the api-token file cannot be read by this Windows user).");

            return new ApiConnection(port, token);
        }
        catch (IOException ex)
        {
            throw new MasterHubNotRunningException("MasterHub is not running (its api files are being written or removed).", ex);
        }
    }
}

/// <summary>What a client of the local API can do (T-24). The CLI, and later the MCP server, use only this.</summary>
public interface ILocalApiClient : IDisposable
{
    /// <summary>The connection it talks over (the port shows in <c>doctor</c>).</summary>
    ApiConnection Connection { get; }

    Task<ApiVersion> GetVersionAsync(CancellationToken ct = default);
    Task<ApiState> GetStateAsync(CancellationToken ct = default);
    Task<ApiModules> GetModulesAsync(CancellationToken ct = default);
    Task ActivateProfileAsync(string profileId, CancellationToken ct = default);
    Task<ControlsResult> SetControlsAsync(string profileId, IReadOnlyList<ControlChange> changes, CancellationToken ct = default);
    Task SetLightsAsync(string mode, string? color, CancellationToken ct = default);

    /// <summary>Tells the app what a Claude Code hook reported (T-25). Only the fields of <see cref="MasterHub.Core.Status.AgentRequest"/> exist.</summary>
    Task PostAgentAsync(MasterHub.Core.Status.AgentRequest request, CancellationToken ct = default);

    /// <summary>One profile as the app exports it (credentials removed), as raw JSON.</summary>
    Task<JsonElement> GetProfileAsync(string profileId, CancellationToken ct = default);
}

/// <inheritdoc cref="ILocalApiClient"/>
public sealed class LocalApiClient : ILocalApiClient
{
    private readonly HttpClient _http;

    /// <param name="connection">Port and token of the running app.</param>
    /// <param name="handler">Tests give a fake handler; the CLI uses the default one.</param>
    /// <param name="timeout">How long one call may take (default 10 s: the app answers in milliseconds or not at all).</param>
    /// <param name="assistant">Set by the MCP server: every request then says it comes from an assistant, and the app applies the assistant's limits (<c>assistant</c> or <c>assistant-power</c>).</param>
    public LocalApiClient(ApiConnection connection, HttpMessageHandler? handler = null, TimeSpan? timeout = null, string? assistant = null)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri($"http://127.0.0.1:{connection.Port}/");
        _http.Timeout = timeout ?? TimeSpan.FromSeconds(10);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
        if (!string.IsNullOrEmpty(assistant)) _http.DefaultRequestHeaders.Add(MasterHub.Core.Api.AssistantLimits.Header, assistant);
    }

    public ApiConnection Connection { get; }

    public Task<ApiVersion> GetVersionAsync(CancellationToken ct = default) => SendAsync<ApiVersion>(HttpMethod.Get, "api/version", null, ct);
    public Task<ApiState> GetStateAsync(CancellationToken ct = default) => SendAsync<ApiState>(HttpMethod.Get, "api/state", null, ct);
    public Task<ApiModules> GetModulesAsync(CancellationToken ct = default) => SendAsync<ApiModules>(HttpMethod.Get, "api/modules", null, ct);

    public Task ActivateProfileAsync(string profileId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, $"api/profiles/{Uri.EscapeDataString(profileId)}/activate", new { }, ct);

    public Task<ControlsResult> SetControlsAsync(string profileId, IReadOnlyList<ControlChange> changes, CancellationToken ct = default) =>
        SendAsync<ControlsResult>(HttpMethod.Post, $"api/profiles/{Uri.EscapeDataString(profileId)}/controls", new ControlsRequest { Controls = changes.ToList() }, ct);

    public Task SetLightsAsync(string mode, string? color, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "api/lights", new LightsRequest { Mode = mode, Color = color }, ct);

    public Task PostAgentAsync(MasterHub.Core.Status.AgentRequest request, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Post, "api/agent", request, ct);

    public Task<JsonElement> GetProfileAsync(string profileId, CancellationToken ct = default) =>
        SendAsync<JsonElement>(HttpMethod.Get, $"api/profiles/{Uri.EscapeDataString(profileId)}", null, ct);

    public void Dispose() => _http.Dispose();

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = JsonContent.Create(body, body.GetType(), options: ApiJson.Options);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // refused / reset: the files of a closed app can outlive it (a crash), and nothing listens on that port any more
            throw new MasterHubNotRunningException($"MasterHub is not running (nothing answers on port {Connection.Port}).", ex);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(0, $"MasterHub did not answer within {_http.Timeout.TotalSeconds:0} seconds.");
        }

        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw Describe((int)response.StatusCode, text);

            try
            {
                return JsonSerializer.Deserialize<T>(text, ApiJson.Options)
                       ?? throw new ApiException((int)response.StatusCode, "MasterHub sent an empty answer.");
            }
            catch (JsonException ex)
            {
                throw new ApiException((int)response.StatusCode, "MasterHub sent an answer this version does not understand: " + ex.Message);
            }
        }
    }

    /// <summary>Turns an error answer (<c>{ error, errors[], warnings[] }</c>) into one exception that carries the reason the app gave.</summary>
    private static ApiException Describe(int status, string body)
    {
        string message = status switch
        {
            401 => "The app refused the token (it changes at every start: was the app restarted?). Try again.",
            403 => "The app refused the request (host or origin).",
            _ => $"The app answered {status}."
        };
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) message = e.GetString() ?? message;
                if (root.TryGetProperty("errors", out var es) && es.ValueKind == JsonValueKind.Array)
                    errors.AddRange(es.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
                if (root.TryGetProperty("warnings", out var ws) && ws.ValueKind == JsonValueKind.Array)
                    warnings.AddRange(ws.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
            }
        }
        catch (JsonException)
        {
            // not JSON: the generic text above is all there is
        }

        return new ApiException(status, message, errors, warnings);
    }
}
