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

    [JsonIgnore] public string Folder { get; set; } = "";

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
        if (!File.Exists(path)) { error = "manifest.json is missing."; return null; }
        WebWidgetManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<WebWidgetManifest>(File.ReadAllText(path), Options); }
        catch (Exception ex) { error = $"manifest.json is invalid: {ex.Message}"; return null; }
        if (manifest is null) { error = "manifest.json is empty."; return null; }
        if (!IdRx.IsMatch(manifest.Id)) { error = "The id must use lower-case letters, digits, dots and dashes."; return null; }
        if (string.IsNullOrWhiteSpace(manifest.Name)) { error = "The name is missing."; return null; }
        string entry = Path.GetFullPath(Path.Combine(folder, manifest.Entry));
        if (!entry.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) || !File.Exists(entry))
        {
            error = $"The entry file {manifest.Entry} doesn't exist.";
            return null;
        }
        if (manifest.MinDockHubVersion is { } min && Version.TryParse(min, out var required) &&
            Version.TryParse(AppInfo.Version, out var current) && current < required)
        {
            error = $"Needs DockHub {min} or newer.";
            return null;
        }
        if (manifest.Variants.Count == 0) manifest.Variants.Add(new WebWidgetVariant());
        manifest.Folder = folder;
        return manifest;
    }

    /// <summary>Reads a .dockwidget package (zip) without installing it.</summary>
    public static WebWidgetManifest? Inspect(string packagePath, out string extractedTo, out string? error)
    {
        extractedTo = Path.Combine(Path.GetTempPath(), "dockhub-widget-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            ZipFile.ExtractToDirectory(packagePath, extractedTo);
        }
        catch (Exception ex)
        {
            error = $"The package can't be opened: {ex.Message}";
            return null;
        }
        // Packages may contain the files directly or inside one folder.
        string root = File.Exists(Path.Combine(extractedTo, "manifest.json"))
            ? extractedTo
            : Directory.EnumerateDirectories(extractedTo).FirstOrDefault(d => File.Exists(Path.Combine(d, "manifest.json"))) ?? extractedTo;
        extractedTo = root;
        return Read(root, out error);
    }

    /// <summary>Copies an inspected package into the widgets folder (replacing an older version) and registers it.</summary>
    public static void Install(WebWidgetManifest manifest)
    {
        string target = Path.Combine(WidgetsDir, manifest.Id);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        CopyDirectory(manifest.Folder, target);
        var installed = Read(target, out _) ?? throw new InvalidOperationException("The installed widget can't be read.");
        Installed.RemoveAll(m => m.Id == installed.Id);
        Installed.Add(installed);
        WidgetRegistry.Register(WebWidget.CreateDescriptor(installed));
        Log.Info($"Web widget installed: {installed.Id} {installed.Version}");
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

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
