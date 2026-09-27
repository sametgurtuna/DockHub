using System.Net;
using System.Net.Http;
using CustomDock.Core;

namespace CustomDock.Widgets.Web;

/// <summary>A widget offered in the gallery's "Featured" list.</summary>
public sealed record FeaturedWidget(string Id, string EnglishName, string EnglishDescription, string SamplePath)
{
    public string Name => L.T(EnglishName);

    public string Description => L.T(EnglishDescription);

    public IReadOnlyList<string> Links => WebWidgetDownloader.SampleLinks(SamplePath);
}

/// <summary>
/// Installs web widgets from a link: a .dockwidget/.zip package, a manifest.json (its entry and "files" are fetched
/// next to it) or a GitHub folder link. Only HTTPS, bounded sizes, and every file must stay beside the manifest.
/// </summary>
public static class WebWidgetDownloader
{
    private const long MaxPackageBytes = WebWidgetCatalog.MaxPackageBytes;
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private const int MaxFiles = 64;

    private static readonly HttpClient Http = CreateClient();

    /// <summary>The community list built into this version (see <see cref="WebWidgetIndex"/> for the live one).</summary>
    public static IReadOnlyList<FeaturedWidget> Featured { get; } = new[]
    {
        new FeaturedWidget("dev.dockhub.github-pulls", "GitHub pull requests",
            "Pull requests waiting for your review, your open pull requests or unread notifications.", "github-pulls"),
        new FeaturedWidget("dev.dockhub.github-actions", "GitHub Actions",
            "The latest workflow run of a repository: passing, failing or running, with a notification when it fails.", "github-actions"),
        new FeaturedWidget("dev.dockhub.home-assistant", "Home Assistant",
            "The state of a Home Assistant entity, such as a temperature; click to switch lights and plugs.", "home-assistant"),
        new FeaturedWidget("dev.dockhub.github-stars", "GitHub stars", "Star count of a GitHub repository.", "github-stars"),
        new FeaturedWidget("dev.dockhub.hello-world", "Hello world", "The SDK starter: settings, storage, a menu item and a notification.", "hello-world"),
    };

    /// <summary>A sample of DockHub's repository: at this version's tag first, then on the main branch (for development builds).</summary>
    public static IReadOnlyList<string> SampleLinks(string samplePath) => new[]
    {
        $"https://raw.githubusercontent.com/sametgurtuna/DockHub/v{AppInfo.Version}/samples/widgets/{samplePath}/manifest.json",
        $"https://raw.githubusercontent.com/sametgurtuna/DockHub/master/samples/widgets/{samplePath}/manifest.json",
    };

    /// <summary>The HTTP client for widget downloads (DockHub user agent, 30 s timeout).</summary>
    internal static HttpClient Client => Http;

    public sealed class DownloadException : Exception
    {
        public DownloadException(string message, bool notFound = false) : base(message) => NotFound = notFound;

        public bool NotFound { get; }
    }

    /// <summary>Turns GitHub page links into raw file links; null when the link can't be used.</summary>
    public static Uri? Normalize(string link)
    {
        link = link.Trim();
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            // github.com/owner/repo/tree/ref/path → raw.githubusercontent.com/owner/repo/ref/path/manifest.json
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length >= 4 && parts[2] is "tree" or "blob")
            {
                string rest = string.Join('/', parts.Skip(3));
                string raw = $"https://raw.githubusercontent.com/{parts[0]}/{parts[1]}/{rest}";
                if (parts[2] == "tree") raw = raw.TrimEnd('/') + "/manifest.json";
                return new Uri(raw);
            }
        }
        return uri;
    }

    private static bool IsPackage(Uri uri)
        => uri.AbsolutePath.EndsWith(".dockwidget", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>Downloads a widget into a temporary folder and validates it (nothing is installed yet).</summary>
    public static async Task<WebWidgetManifest> DownloadAsync(string link, CancellationToken cancellation = default)
    {
        var uri = Normalize(link) ?? throw new DownloadException(L.T("Use an https:// link to a manifest.json, a .dockwidget file or a GitHub folder."));

        if (IsPackage(uri))
        {
            string file = Path.Combine(Path.GetTempPath(), "dockhub-widget-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
            await File.WriteAllBytesAsync(file, await GetBytesAsync(uri, MaxPackageBytes, cancellation), cancellation);
            try
            {
                return WebWidgetCatalog.Inspect(file, out _, out var packageError) ?? throw new DownloadException(packageError ?? "");
            }
            finally
            {
                TryDelete(file);
            }
        }

        if (!uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            uri = new Uri(uri, uri.AbsolutePath.TrimEnd('/') + "/manifest.json");

        byte[] manifestBytes = await GetBytesAsync(uri, 256 * 1024, cancellation);
        var manifest = WebWidgetCatalog.Parse(System.Text.Encoding.UTF8.GetString(manifestBytes), out var parseError)
                       ?? throw new DownloadException(parseError ?? "");

        string folder = Path.Combine(Path.GetTempPath(), "dockhub-widget-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "manifest.json"), manifestBytes, cancellation);

        var files = new[] { manifest.Entry }.Concat(manifest.Files).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count > MaxFiles) throw new DownloadException(L.T("The widget lists too many files (at most {0}).", MaxFiles));
        foreach (string relative in files)
        {
            if (!IsSafeRelativePath(relative)) throw new DownloadException(L.T("The widget lists a file outside its folder: {0}", relative));
            var fileUri = new Uri(uri, relative);
            if (fileUri.Scheme != Uri.UriSchemeHttps || !fileUri.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase))
                throw new DownloadException(L.T("The widget lists a file outside its folder: {0}", relative));
            string target = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new DownloadException(L.T("The widget lists a file outside its folder: {0}", relative));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, await GetBytesAsync(fileUri, MaxFileBytes, cancellation), cancellation);
        }

        return WebWidgetCatalog.Read(folder, out var error) ?? throw new DownloadException(error ?? "");
    }

    /// <summary>Tries each link in turn; a missing file (404) moves on to the next one.</summary>
    public static async Task<WebWidgetManifest> DownloadFirstAsync(IReadOnlyList<string> links, CancellationToken cancellation = default)
    {
        DownloadException? last = null;
        foreach (string link in links)
        {
            try
            {
                return await DownloadAsync(link, cancellation);
            }
            catch (DownloadException ex) when (ex.NotFound)
            {
                last = ex;
            }
        }
        throw last ?? new DownloadException(L.T("The widget wasn't found."), notFound: true);
    }

    /// <summary>"img/icon.png" is fine; absolute paths, "..", drive letters and URLs are not.</summary>
    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 200) return false;
        if (path.Contains(':') || path.Contains('\\') || path.StartsWith('/') || path.Contains('?') || path.Contains('#')) return false;
        return path.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
    }

    private static async Task<byte[]> GetBytesAsync(Uri uri, long limit, CancellationToken cancellation)
    {
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new DownloadException(L.T("Not found: {0}", uri.ToString()), notFound: true);
        if (!response.IsSuccessStatusCode)
            throw new DownloadException(L.T("Download failed ({0}): {1}", (int)response.StatusCode, uri.ToString()));
        if (response.Content.Headers.ContentLength > limit)
            throw new DownloadException(L.T("The file is too large: {0}", uri.ToString()));

        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellation)) > 0)
        {
            if (buffer.Length + read > limit) throw new DownloadException(L.T("The file is too large: {0}", uri.ToString()));
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch { /* temp file */ }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DockHub (+https://github.com/sametgurtuna/DockHub)");
        return client;
    }
}
