using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace CustomDock.Widgets.Web;

/// <summary>
/// dockhub.http.request: a request DockHub makes for a web widget. Because it doesn't run in the page, it isn't
/// subject to CORS or to the https page's ban on plain http, which is how a widget reaches a server on the local
/// network (for example Home Assistant). The host must still be allowed by the widget's manifest.
/// </summary>
public static class WebWidgetHttp
{
    public const int MaxResponseBytes = 1024 * 1024;
    public const int MaxBodyBytes = 64 * 1024;
    private static readonly string[] Methods = { "GET", "POST", "PUT", "DELETE" };
    private static readonly HashSet<string> ForbiddenHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Cookie", "Connection", "Content-Length", "Transfer-Encoding", "Upgrade", "Proxy-Authorization",
    };

    // No cookies and no redirects: a redirect could lead to a host the widget isn't allowed to reach.
    private static readonly HttpClient Http = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    /// <summary>A parsed and checked request, or the reason it is refused.</summary>
    public sealed record Request(Uri Url, string Method, IReadOnlyList<KeyValuePair<string, string>> Headers, string? Body);

    internal static Request Parse(JsonObject? args, Func<string, bool> isHostAllowed)
    {
        string url = args?["url"]?.GetValue<string>() ?? throw new InvalidOperationException("url is required");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Only http and https addresses can be requested.");
        if (!isHostAllowed(uri.Host))
            throw new InvalidOperationException($"{uri.Host} is not in the widget's permissions.");

        string method = (args?["method"]?.GetValue<string>() ?? "GET").ToUpperInvariant();
        if (!Methods.Contains(method)) throw new InvalidOperationException($"Method {method} is not supported.");

        var headers = new List<KeyValuePair<string, string>>();
        if (args?["headers"] is JsonObject given)
        {
            foreach (var (name, value) in given)
            {
                if (ForbiddenHeaders.Contains(name)) throw new InvalidOperationException($"The {name} header can't be set.");
                headers.Add(new(name, value?.GetValue<string>() ?? ""));
            }
        }

        string? body = args?["body"]?.GetValue<string>();
        if (body is not null && Encoding.UTF8.GetByteCount(body) > MaxBodyBytes)
            throw new InvalidOperationException("The request body is larger than 64 KB.");
        return new Request(uri, method, headers, body);
    }

    /// <summary>Sends the request; the answer is { status, contentType, body } with a text body of at most 1 MB.</summary>
    internal static async Task<JsonObject> SendAsync(Request request)
    {
        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
        string? contentType = null;
        foreach (var (name, value) in request.Headers)
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = value;
            else message.Headers.TryAddWithoutValidation(name, value);
        }
        if (request.Body is not null)
        {
            message.Content = new StringContent(request.Body, Encoding.UTF8);
            if (contentType is not null) message.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(true);
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new InvalidOperationException("The answer is larger than 1 MB.");
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(true);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk).ConfigureAwait(true)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) throw new InvalidOperationException("The answer is larger than 1 MB.");
            buffer.Write(chunk, 0, read);
        }
        return new JsonObject
        {
            ["status"] = (int)response.StatusCode,
            ["ok"] = response.IsSuccessStatusCode,
            ["contentType"] = response.Content.Headers.ContentType?.ToString(),
            ["body"] = Encoding.UTF8.GetString(buffer.ToArray()),
        };
    }
}
