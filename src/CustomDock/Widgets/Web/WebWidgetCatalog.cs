using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CustomDock.Core;

namespace CustomDock.Widgets.Web;

/// <summary>A setting a web widget declares in its manifest (shown in DockHub's widget settings).</summary>
public sealed class WebWidgetSetting
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    /// <summary>"text", "number", "toggle" or "choice".</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("default")] public JsonElement? Default { get; set; }
    [JsonPropertyName("min")] public double? Min { get; set; }
    [JsonPropertyName("max")] public double? Max { get; set; }
    [JsonPropertyName("options")] public List<string>? Options { get; set; }
}

public sealed class WebWidgetPermissions
{
    /// <summary>Host names the widget may fetch from (exact names or "*.example.com").</summary>
    [JsonPropertyName("network")] public List<string> Network { get; set; } = new();
    [JsonPropertyName("notifications")] public bool Notifications { get; set; }

    /// <summary>
    /// Keys of text settings that hold a server address (for example a Home Assistant URL): the host the user enters
    /// there may be reached too.
    /// </summary>
    [JsonPropertyName("networkFromSettings")] public List<string> NetworkFromSettings { get; set; } = new();
}

public sealed class WebWidgetVariant
{
    [JsonPropertyName("id")] public string Id { get; set; } = "default";
    [JsonPropertyName("name")] public string Name { get; set; } = "Default";
    /// <summary>"compact" (square), "standard" or "wide".</summary>
    [JsonPropertyName("size")] public string Size { get; set; } = "standard";
}

/// <summary>manifest.json of a web widget (see docs/widget-sdk.md).</summary>
public sealed class WebWidgetManifest
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "1.0.0";
    [JsonPropertyName("author")] public string? Author { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("entry")] public string Entry { get; set; } = "index.html";
    [JsonPropertyName("minDockHubVersion")] public string? MinDockHubVersion { get; set; }
    [JsonPropertyName("variants")] public List<WebWidgetVariant> Variants { get; set; } = new() { new() };
    [JsonPropertyName("settings")] public List<WebWidgetSetting> Settings { get; set; } = new();
    [JsonPropertyName("permissions")] public WebWidgetPermissions Permissions { get; set; } = new();
    /// <summary>Extra files (relative paths) fetched next to manifest.json when installed from a link.</summary>
    [JsonPropertyName("files")] public List<string> Files { get; set; } = new();

    [JsonIgnore] public string Folder { get; set; } = "";

    /// <summary>The link it was downloaded from (null for a package opened from disk); kept for update checks.</summary>
    [JsonIgnore] public string? SourceLink { get; set; }

    /// <summary>Id of the DockHub widget type ("web." + manifest id).</summary>
    [JsonIgnore] public string WidgetId => "web." + Id;

    /// <summary>Virtual host name the widget's files are served from.</summary>
    [JsonIgnore] public string HostName => Id.Replace('.', '-') + ".widget.dockhub";
}

/// <summary>Discovers web widgets in %AppData%\DockHub\widgets and installs .dockwidget packages.</summary>
public static class WebWidgetCatalog
{
    private static readonly Regex IdRx = new(@"^[a-z0-9][a-z0-9.\-]{2,63}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Options = new() { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public const string Category = "Web widgets";

    /// <summary>Generic puzzle-piece icon used for web widgets in menus and compact tiles.</summary>
    public const string Icon = "M9,4 H12 V6 A1.5,1.5 0 0 0 15,6 V4 H20 V9 H18 A1.5,1.5 0 0 0 18,12 H20 V20 H12 V18 A1.5,1.5 0 0 0 9,18 V20 H4 V12 H6 A1.5,1.5 0 0 0 6,9 H4 V4 Z";

    public static string WidgetsDir => Path.Combine(AppPaths.Root, "widgets");

    public static List<WebWidgetManifest> Installed { get; } = new();

    /// <summary>Reads every installed widget and adds it to the widget registry.</summary>
    public static void LoadAll()
    {
        Installed.Clear();
        if (!Directory.Exists(WidgetsDir)) return;
        foreach (var folder in Directory.EnumerateDirectories(WidgetsDir))
        {
            if (Read(folder, out var error) is { } manifest)
            {
                Installed.Add(manifest);
                WidgetRegistry.Register(WebWidget.CreateDescriptor(manifest));
            }
            else
            {
                Log.Warn($"Web widget in {folder} skipped: {error}");
            }
        }
        if (Installed.Count > 0) Log.Info($"{Installed.Count} web widget(s) loaded.");
    }

    /// <summary>Reads and validates a widget folder. Returns null with a reason when it can't be used.</summary>
    public static WebWidgetManifest? Read(string folder, out string? error)
    {
        error = null;
        string path = Path.Combine(folder, "manifest.json");
        if (!File.Exists(path)) { error = L.T("manifest.json is missing."); return null; }
        var manifest = Parse(File.ReadAllText(path), out error);
        if (manifest is null) return null;
        string entry = Path.GetFullPath(Path.Combine(folder, manifest.Entry));
        if (!entry.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) || !File.Exists(entry))
        {
            error = L.T("The entry file {0} doesn't exist.", manifest.Entry);
            return null;
        }
        if (manifest.MinDockHubVersion is { } min && Version.TryParse(min, out var required) &&
            Version.TryParse(AppInfo.Version, out var current) && current < required)
        {
            error = L.T("Needs DockHub {0} or newer.", min);
            return null;
        }
        if (manifest.Variants.Count == 0) manifest.Variants.Add(new WebWidgetVariant());
        manifest.Folder = folder;
        return manifest;
    }

    /// <summary>Parses and checks the id and name of a manifest (the files are checked by <see cref="Read"/>).</summary>
    public static WebWidgetManifest? Parse(string json, out string? error)
    {
        error = null;
        WebWidgetManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<WebWidgetManifest>(json, Options); }
        catch (Exception ex) { error = L.T("manifest.json is invalid: {0}", ex.Message); return null; }
        if (manifest is null) { error = L.T("manifest.json is empty."); return null; }
        if (!IdRx.IsMatch(manifest.Id)) { error = L.T("The id must use lower-case letters, digits, dots and dashes."); return null; }
        if (string.IsNullOrWhiteSpace(manifest.Name)) { error = L.T("The name is missing."); return null; }
        foreach (string key in manifest.Permissions.NetworkFromSettings)
        {
            var setting = manifest.Settings.FirstOrDefault(s => s.Key == key && s.Type == "text");
            if (setting is null)
            {
                error = L.T("networkFromSettings names \"{0}\", which is not a text setting.", key);
                return null;
            }
            // The address must come from the user; a default would let the widget pick its own host.
            if (setting.Default is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) })
            {
                error = L.T("The server setting \"{0}\" can't have a default value.", key);
                return null;
            }
        }
        return manifest;
    }

    public const long MaxPackageBytes = 20 * 1024 * 1024;
    public const long MaxUnpackedBytes = 50 * 1024 * 1024;
    public const int MaxPackageEntries = 256;

    /// <summary>
    /// Reads a .dockwidget package (zip) without installing it. Packages over 20 MB, over 50 MB unpacked or with more
    /// than 256 entries are refused before anything is extracted; entries can't leave the folder (the zip reader
    /// refuses such paths).
    /// </summary>
    public static WebWidgetManifest? Inspect(string packagePath, out string extractedTo, out string? error)
    {
        extractedTo = Path.Combine(Path.GetTempPath(), "dockhub-widget-" + Guid.NewGuid().ToString("N")[..8]);
        if (CheckPackage(packagePath) is { } refused)
        {
            error = refused;
            return null;
        }
        try
        {
            ZipFile.ExtractToDirectory(packagePath, extractedTo);
        }
        catch (Exception ex)
        {
            error = L.T("The package can't be opened: {0}", ex.Message);
            return null;
        }
        // Packages may contain the files directly or inside one folder.
        string root = File.Exists(Path.Combine(extractedTo, "manifest.json"))
            ? extractedTo
            : Directory.EnumerateDirectories(extractedTo).FirstOrDefault(d => File.Exists(Path.Combine(d, "manifest.json"))) ?? extractedTo;
        extractedTo = root;
        return Read(root, out error);
    }

    /// <summary>A reason to refuse a package before extracting it, or null when its size and entries are fine.</summary>
    internal static string? CheckPackage(string packagePath)
    {
        try
        {
            if (new FileInfo(packagePath).Length > MaxPackageBytes) return L.T("The package is larger than {0} MB.", MaxPackageBytes / 1024 / 1024);
            using var zip = ZipFile.OpenRead(packagePath);
            if (zip.Entries.Count > MaxPackageEntries) return L.T("The package has more than {0} files.", MaxPackageEntries);
            long unpacked = zip.Entries.Sum(entry => entry.Length);
            if (unpacked > MaxUnpackedBytes) return L.T("The package is larger than {0} MB.", MaxUnpackedBytes / 1024 / 1024);
            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return L.T("The package can't be opened: {0}", ex.Message);
        }
    }

    /// <summary>Copies an inspected package into the widgets folder (replacing an older version) and registers it.</summary>
    public static void Install(WebWidgetManifest manifest)
    {
        string target = Path.Combine(WidgetsDir, manifest.Id);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        CopyDirectory(manifest.Folder, target);
        // Where it came from, to look for newer versions there; a package from disk has no source.
        File.Delete(Path.Combine(target, WidgetSource.FileName));
        if (manifest.SourceLink is { } link)
            new WidgetSource { Link = link, Version = manifest.Version, InstalledAt = DateTime.UtcNow }.Write(target);
        var installed = Read(target, out _) ?? throw new InvalidOperationException("The installed widget can't be read.");
        Installed.RemoveAll(m => m.Id == installed.Id);
        Installed.Add(installed);
        WidgetRegistry.Register(WebWidget.CreateDescriptor(installed));
        WebWidgetUpdates.MarkInstalled(installed.Id, installed.Version);
        Log.Info($"Web widget installed: {installed.Id} {installed.Version}");
    }

    /// <summary>Takes a re-read manifest of an installed widget (developer mode) into the list and the registry.</summary>
    public static void Refresh(WebWidgetManifest manifest)
    {
        Installed.RemoveAll(m => m.Id == manifest.Id);
        Installed.Add(manifest);
        WidgetRegistry.Register(WebWidget.CreateDescriptor(manifest));
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    /// <summary>
    /// Hosts of the addresses the user entered in the settings named by permissions.networkFromSettings. Pass the
    /// user's saved values only, never the manifest defaults.
    /// </summary>
    public static IReadOnlyList<string> SettingsHosts(WebWidgetManifest manifest, System.Text.Json.Nodes.JsonObject values)
    {
        var hosts = new List<string>();
        foreach (string key in manifest.Permissions.NetworkFromSettings)
        {
            string? text = null;
            try { text = values[key]?.GetValue<string>()?.Trim(); } catch (InvalidOperationException) { }
            if (!string.IsNullOrEmpty(text) && Uri.TryCreate(text, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https" && uri.Host.Length > 0)
                hosts.Add(uri.Host);
        }
        return hosts;
    }

    /// <summary>Whether a request host is allowed by the manifest or is one the user entered in a server setting.</summary>
    public static bool IsHostAllowed(WebWidgetManifest manifest, string host, IReadOnlyCollection<string> settingsHosts)
        => IsHostAllowed(manifest, host) || settingsHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a request host is allowed by the widget's network permission.</summary>
    public static bool IsHostAllowed(WebWidgetManifest manifest, string host)
    {
        if (host.Equals(manifest.HostName, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var allowed in manifest.Permissions.Network)
        {
            if (allowed.StartsWith("*.", StringComparison.Ordinal)
                ? host.EndsWith(allowed[1..], StringComparison.OrdinalIgnoreCase) || host.Equals(allowed[2..], StringComparison.OrdinalIgnoreCase)
                : host.Equals(allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
