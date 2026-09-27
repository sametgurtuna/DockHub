using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using CustomDock.Core;

namespace CustomDock.Widgets.Web;

/// <summary>A widget offered in the gallery's community list.</summary>
public sealed record WidgetIndexEntry(string Id, string EnglishName, string EnglishDescription, string? Author, IReadOnlyList<string> Links)
{
    public string Name => L.T(EnglishName);

    public string Description => L.T(EnglishDescription);
}

/// <summary>
/// The community widget list: <c>samples/widgets/index.json</c> in DockHub's repository, fetched at most once a day
/// and kept in <c>%AppData%\DockHub\data\widget-index.json</c>. Anyone can add a widget to it with a pull request.
/// Without a connection the last copy is used, and before the first one the list built into DockHub.
/// </summary>
public static class WebWidgetIndex
{
    public const string IndexUrl = "https://raw.githubusercontent.com/sametgurtuna/DockHub/master/samples/widgets/index.json";
    public const int MaxEntries = 100;
    private const int MaxIndexBytes = 256 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);
    private static readonly Regex IdRx = new(@"^[a-z0-9][a-z0-9.\-]{2,63}$", RegexOptions.CultureInvariant);

    private sealed class CacheFile
    {
        public DateTime FetchedAt { get; set; }
        public string Json { get; set; } = "";
    }

    /// <summary>The list shipped with this version of DockHub.</summary>
    public static IReadOnlyList<WidgetIndexEntry> BuiltIn { get; } = WebWidgetDownloader.Featured
        .Select(f => new WidgetIndexEntry(f.Id, f.EnglishName, f.EnglishDescription, "DockHub", f.Links))
        .ToList();

    /// <summary>The newest list available without a network request.</summary>
    public static IReadOnlyList<WidgetIndexEntry> Current()
    {
        var cache = JsonStore.LoadData<CacheFile>("widget-index");
        var entries = cache.Json.Length > 0 ? Parse(cache.Json) : Array.Empty<WidgetIndexEntry>();
        return entries.Count > 0 ? entries : BuiltIn;
    }

    /// <summary>Downloads the list when the kept copy is older than a day; null when nothing new was fetched.</summary>
    public static async Task<IReadOnlyList<WidgetIndexEntry>?> RefreshAsync(HttpClient http)
    {
        var cache = JsonStore.LoadData<CacheFile>("widget-index");
        if (cache.Json.Length > 0 && DateTime.UtcNow - cache.FetchedAt < MaxAge) return null;
        try
        {
            using var response = await http.GetAsync(IndexUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(true);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxIndexBytes) return null;
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
            if (json.Length > MaxIndexBytes) return null;

            var errors = new List<string>();
            var entries = Parse(json, errors);
            foreach (var error in errors) Log.Warn($"Widget index: {error}");
            if (entries.Count == 0) return null;
            JsonStore.SaveData("widget-index", new CacheFile { FetchedAt = DateTime.UtcNow, Json = json });
            return entries;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Debug($"Widget index not refreshed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads index.json: { "widgets": [ { "id", "name", "description", "author", and "path" (a folder of
    /// samples/widgets) or "link" (anything Install from link accepts) } ] }. Invalid entries are skipped.
    /// </summary>
    public static IReadOnlyList<WidgetIndexEntry> Parse(string json, List<string>? errors = null)
    {
        var entries = new List<WidgetIndexEntry>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            errors?.Add($"not valid JSON ({ex.Message})");
            return entries;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("widgets", out var widgets) || widgets.ValueKind != JsonValueKind.Array)
            {
                errors?.Add("expected an object with a \"widgets\" array");
                return entries;
            }

            int index = 0;
            foreach (var element in widgets.EnumerateArray())
            {
                if (entries.Count >= MaxEntries)
                {
                    errors?.Add($"only the first {MaxEntries} widgets are listed");
                    break;
                }
                if (ParseEntry(element, out string? error) is { } entry)
                {
                    if (entries.Any(e => e.Id == entry.Id)) errors?.Add($"widgets[{index}]: {entry.Id} is listed twice");
                    else entries.Add(entry);
                }
                else
                {
                    errors?.Add($"widgets[{index}]: {error}");
                }
                index++;
            }
        }
        return entries;
    }

    private static WidgetIndexEntry? ParseEntry(JsonElement element, out string? error)
    {
        error = null;
        if (element.ValueKind != JsonValueKind.Object) { error = "not an object"; return null; }
        string? id = Text(element, "id"), name = Text(element, "name"), description = Text(element, "description");
        if (id is null || !IdRx.IsMatch(id)) { error = "\"id\" must use lower-case letters, digits, dots and dashes"; return null; }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60) { error = "\"name\" is required (at most 60 characters)"; return null; }
        if (string.IsNullOrWhiteSpace(description) || description.Length > 200) { error = "\"description\" is required (at most 200 characters)"; return null; }

        IReadOnlyList<string> links;
        if (Text(element, "path") is { } path)
        {
            if (!WebWidgetDownloader.IsSafeRelativePath(path) || path.Contains('/')) { error = "\"path\" must be a folder name in samples/widgets"; return null; }
            links = WebWidgetDownloader.SampleLinks(path);
        }
        else if (Text(element, "link") is { } link && WebWidgetDownloader.Normalize(link) is not null)
        {
            links = new[] { link };
        }
        else
        {
            error = "an https \"link\" or a \"path\" is required";
            return null;
        }
        return new WidgetIndexEntry(id, name.Trim(), description.Trim(), Text(element, "author")?.Trim(), links);
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
