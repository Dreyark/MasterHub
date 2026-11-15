using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Actions;

/// <summary>A parsed "call a URL" request.</summary>
public sealed record HttpActionSpec(HttpMethod Method, Uri Uri, string? Body);

/// <summary>
/// The "Call a URL" key (T-17 i): calls a web address or an app's local API, e.g.
/// <c>POST http://127.0.0.1:8080/scene | {"name":"Live"}</c>. Only <c>http</c> / <c>https</c>; at most
/// <see cref="Timeout"/>; the answer's status decides the key's result (a failure shows the red corner, T-07). The
/// response body is not read or kept.
/// </summary>
public static class HttpAction
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    private static readonly string[] Methods = { "GET", "POST", "PUT", "PATCH", "DELETE" };

    /// <summary>Reads <c>[METHOD] URL [| body]</c>; <c>null</c> for anything else (bad URL, other scheme, unknown method).</summary>
    public static HttpActionSpec? Parse(string? parameter)
    {
        string text = (parameter ?? "").Trim();
        if (text.Length == 0) return null;

        string? body = null;
        int bar = text.IndexOf(" | ", StringComparison.Ordinal);
        if (bar >= 0)
        {
            body = text[(bar + 3)..].Trim();
            text = text[..bar].Trim();
        }

        string method = "GET";
        int space = text.IndexOf(' ');
        if (space > 0 && Array.Exists(Methods, m => m.Equals(text[..space], StringComparison.OrdinalIgnoreCase)))
        {
            method = text[..space].ToUpperInvariant();
            text = text[(space + 1)..].Trim();
        }
        else if (space > 0)
        {
            return null;   // "FOO http://…" or a URL with spaces
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return null;
        return new HttpActionSpec(new HttpMethod(method), uri, string.IsNullOrEmpty(body) ? null : body);
    }

    /// <summary>Sends the request; <c>true</c> for a 2xx answer.</summary>
    public static async Task<bool> SendAsync(HttpClient http, HttpActionSpec spec, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(spec.Method, spec.Uri);
        if (spec.Body != null)
        {
            string type = spec.Body.TrimStart().StartsWith('{') || spec.Body.TrimStart().StartsWith('[') ? "application/json" : "text/plain";
            request.Content = new StringContent(spec.Body, Encoding.UTF8, type);
        }
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }
}
