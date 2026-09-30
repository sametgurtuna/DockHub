using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using CustomDock.Core;

namespace CustomDock.Widgets.Web;

/// <summary>
/// Where an installed web widget came from: source.json in its folder, written when it is installed from a link (a
/// package opened from disk has none and isn't checked for updates).
/// </summary>
public sealed class WidgetSource
{
    public const string FileName = "source.json";

    [JsonPropertyName("link")] public string Link { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("installedAt")] public DateTime InstalledAt { get; set; }

    public static WidgetSource? Read(string folder)
    {
        try
        {
            string path = Path.Combine(folder, FileName);
            if (!File.Exists(path)) return null;
            var source = JsonSerializer.Deserialize<WidgetSource>(File.ReadAllText(path));
            return source is { Link.Length: > 0 } ? source : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write(string folder)
        => File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>Widget versions ("1.2.0", "1.3.0-beta.1"): numbers part by part, a pre-release before its release.</summary>
public static class WidgetVersion
{
    public static int Compare(string? a, string? b)
    {
        var (aCore, aPre) = Split(a);
        var (bCore, bPre) = Split(b);
        for (int i = 0; i < Math.Max(aCore.Length, bCore.Length); i++)
        {
            long x = i < aCore.Length ? aCore[i] : 0, y = i < bCore.Length ? bCore[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        if (aPre is null || bPre is null) return aPre is null ? (bPre is null ? 0 : 1) : -1;
        var aParts = aPre.Split('.');
        var bParts = bPre.Split('.');
        for (int i = 0; i < Math.Max(aParts.Length, bParts.Length); i++)
        {
            if (i >= aParts.Length) return -1;
            if (i >= bParts.Length) return 1;
            bool aNumber = long.TryParse(aParts[i], out long an), bNumber = long.TryParse(bParts[i], out long bn);
            int order = aNumber && bNumber ? an.CompareTo(bn)
                : aNumber ? -1 : bNumber ? 1
                : string.CompareOrdinal(aParts[i], bParts[i]);
            if (order != 0) return order;
        }
        return 0;
    }

    private static (long[] Core, string? PreRelease) Split(string? version)
    {
        string text = (version ?? "").Trim().TrimStart('v', 'V');
        int plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];
        int dash = text.IndexOf('-');
        string? pre = dash >= 0 ? text[(dash + 1)..] : null;
        string core = dash >= 0 ? text[..dash] : text;
        var numbers = core.Split('.').Select(p => long.TryParse(p, out long n) ? n : 0).ToArray();
        return (numbers, string.IsNullOrEmpty(pre) ? null : pre);
    }
}

/// <summary>What a new version of a widget may reach that the installed one couldn't.</summary>
public sealed record PermissionChange(IReadOnlyList<string> Hosts, IReadOnlyList<string> ServerSettings, bool Notifications)
{
    public bool Any => Hosts.Count > 0 || ServerSettings.Count > 0 || Notifications;

    public static PermissionChange Between(WebWidgetManifest installed, WebWidgetManifest update)
    {
        var old = installed.Permissions;
        var hosts = update.Permissions.Network
            .Where(host => !old.Network.Contains(host, StringComparer.OrdinalIgnoreCase) && !CoveredBy(host, old.Network))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var servers = update.Permissions.NetworkFromSettings
            .Where(key => !old.NetworkFromSettings.Contains(key, StringComparer.Ordinal))
            .Select(key => update.Settings.FirstOrDefault(s => s.Key == key)?.Label is { Length: > 0 } label ? label : key)
            .ToList();
        return new PermissionChange(hosts, servers, update.Permissions.Notifications && !old.Notifications);
    }

    /// <summary>"api.example.com" is covered by an old "*.example.com"; a new wildcard only by the same or a wider one.</summary>
    private static bool CoveredBy(string host, IEnumerable<string> allowed)
    {
        string name = host.StartsWith("*.", StringComparison.Ordinal) ? host[2..] : host;
        return allowed.Any(a => a.StartsWith("*.", StringComparison.Ordinal)
                                && (name.Equals(a[2..], StringComparison.OrdinalIgnoreCase) && !host.StartsWith("*.", StringComparison.Ordinal)
                                    || name.EndsWith(a[1..], StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>A newer version of an installed web widget.</summary>
public sealed record WidgetUpdate(string Id, string Name, string InstalledVersion, string Version, string Link);

/// <summary>
/// Looks for newer versions of the web widgets installed from a manifest or GitHub folder link, at most once a day:
/// only their manifest.json is fetched. A version that needs a newer DockHub isn't offered. The result is kept in
/// %AppData%\DockHub\data\widget-updates.json, and each new version is announced once.
/// </summary>
public static class WebWidgetUpdates
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);
    private const int MaxManifestBytes = 256 * 1024;

    private sealed class State
    {
        public DateTime CheckedAt { get; set; }
        public List<WidgetUpdate> Available { get; set; } = new();
        /// <summary>"id@version" already announced.</summary>
        public List<string> Announced { get; set; } = new();
    }

    /// <summary>The updates found by the last check, for widgets still installed at an older version.</summary>
    public static IReadOnlyList<WidgetUpdate> Available()
    {
        var state = JsonStore.LoadData<State>("widget-updates");
        return state.Available
            .Where(u => WebWidgetCatalog.Installed.FirstOrDefault(m => m.Id == u.Id) is { } installed && WidgetVersion.Compare(u.Version, installed.Version) > 0)
            .ToList();
    }

    public static event Action? Changed;

    /// <summary>Checks when the last check is older than a day; returns the updates not announced before.</summary>
    public static async Task<IReadOnlyList<WidgetUpdate>> CheckAsync(HttpClient http, bool force = false)
    {
        var state = JsonStore.LoadData<State>("widget-updates");
        if (!force && DateTime.UtcNow - state.CheckedAt < MaxAge) return Array.Empty<WidgetUpdate>();

        var found = new List<WidgetUpdate>();
        foreach (var installed in WebWidgetCatalog.Installed.ToList())
        {
            if (WidgetSource.Read(installed.Folder) is not { } source || ManifestUri(source.Link) is not { } uri) continue;
            try
            {
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(true);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxManifestBytes) continue;
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
                if (json.Length > MaxManifestBytes || WebWidgetCatalog.Parse(json, out _) is not { } latest || latest.Id != installed.Id) continue;
                if (WidgetVersion.Compare(latest.Version, installed.Version) <= 0 || !Supported(latest)) continue;
                found.Add(new WidgetUpdate(installed.Id, installed.Name, installed.Version, latest.Version, source.Link));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                Log.Debug($"Update check of {installed.Id} failed: {ex.Message}");
            }
        }

        var fresh = found.Where(u => !state.Announced.Contains($"{u.Id}@{u.Version}")).ToList();
        state.CheckedAt = DateTime.UtcNow;
        state.Available = found;
        state.Announced.AddRange(fresh.Select(u => $"{u.Id}@{u.Version}"));
        // Only the latest announcements matter.
        if (state.Announced.Count > 200) state.Announced.RemoveRange(0, state.Announced.Count - 200);
        JsonStore.SaveData("widget-updates", state);
        Changed?.Invoke();
        return fresh;
    }

    /// <summary>The manifest.json behind a link; null for a package (its version is known only after downloading it).</summary>
    public static Uri? ManifestUri(string link)
    {
        if (WebWidgetDownloader.Normalize(link) is not { } uri) return null;
        string path = uri.AbsolutePath;
        if (path.EndsWith(".dockwidget", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return null;
        return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? uri : new Uri(uri, path.TrimEnd('/') + "/manifest.json");
    }

    /// <summary>Whether this DockHub can run a version (its minDockHubVersion).</summary>
    public static bool Supported(WebWidgetManifest manifest)
        => manifest.MinDockHubVersion is not { } min || !Version.TryParse(min, out var required)
           || !Version.TryParse(AppInfo.Version, out var current) || current >= required;

    /// <summary>After an update is installed: it is no longer offered.</summary>
    public static void MarkInstalled(string id, string version)
    {
        var state = JsonStore.LoadData<State>("widget-updates");
        if (state.Available.RemoveAll(u => u.Id == id && WidgetVersion.Compare(u.Version, version) <= 0) > 0)
        {
            JsonStore.SaveData("widget-updates", state);
            Changed?.Invoke();
        }
    }
}
