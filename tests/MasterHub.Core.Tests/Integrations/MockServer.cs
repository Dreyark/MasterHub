using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace MasterHub.Core.Tests.Integrations;

/// <summary>
/// Lokalny serwer HTTP/WebSocket (Kestrel) udający zewnętrzną usługę. Nasłuchuje na 127.0.0.2, dzięki czemu klienty
/// nie traktują go jako "localhost" (część usług odmawia łączenia, gdy nie widzi lokalnego procesu OBS/VLC).
/// </summary>
public sealed class MockServer : IAsyncDisposable
{
    public const string Host = "127.0.0.2";
    private readonly WebApplication _app;
    public int Port { get; }
    public string BaseUrl => $"http://{Host}:{Port}";

    private MockServer(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    public static async Task<MockServer> StartAsync(Action<WebApplication> map)
    {
        int port = FreePort();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://{Host}:{port}");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.UseWebSockets();
        map(app);
        await app.StartAsync();
        return new MockServer(app, port);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    public async ValueTask DisposeAsync()
    {
        try { using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)); await _app.StopAsync(cts.Token); } catch { }
        await _app.DisposeAsync();
    }

    // ---- pomocnicze operacje WebSocket ----
    public static async Task SendJsonAsync(WebSocket ws, object payload)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    /// <summary>Odbiera jedną wiadomość tekstową jako JSON; null = zamknięcie połączenia.</summary>
    public static async Task<JsonElement?> ReceiveJsonAsync(WebSocket ws, int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buffer, cts.Token);
            if (r.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, r.Count);
        } while (!r.EndOfMessage);
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(ms.ToArray()));
        return doc.RootElement.Clone();
    }
}

/// <summary>Prosty HttpMessageHandler: zapisuje żądania i zwraca odpowiedzi z delegata.</summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    public sealed record Captured(HttpMethod Method, string Url, Dictionary<string, string> Headers, string Body);

    private readonly Func<Captured, (HttpStatusCode, string)> _respond;
    public List<Captured> Requests { get; } = new();

    public RecordingHandler(Func<Captured, (HttpStatusCode, string)> respond) => _respond = respond;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var cap = new Captured(request.Method, request.RequestUri!.ToString(), headers, body);
        lock (Requests) Requests.Add(cap);
        var (status, content) = _respond(cap);
        return new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    }
}
