using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CustomDock.Core;

/// <summary>
/// What settings sync shares between PCs, on the settings as JSON (config.json's camelCase names). Shared: the look,
/// the dock's items and their widget settings, profiles, presets, shortcuts and the rest. Kept on each PC: the display
/// and tray choices, starting with Windows, how the taskbar is replaced, the language, which display each widget is on, and every
/// secret (encrypted values such as the Todoist token, whose names start with "protected"), which are never written.
/// </summary>
public static class SyncMerge
{
    /// <summary>Settings that stay on each PC (top-level names).</summary>
    public static readonly IReadOnlySet<string> LocalOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "version", "taskbarMode", "startWithWindows", "explorerPinMenu", "monitorDevice", "displaySizes", "pinnedTrayIcons",
        "knownTrayIcons", "welcomeShown", "debugLogging", "isFirstRun", "removedWidgetSettings",
        // Changing the language asks for a restart; each PC keeps its own.
        "language",
        "syncFolder", "syncDeviceId", "syncAppliedAt",
        // Settings from before 0.3, read once and never written back.
        "widgets", "widgetSettings", "reserveSpace",
    };

    /// <summary>An item's own field that stays on each PC: the display its widget is on.</summary>
    private const string DisplayField = "display";

    /// <summary>Secrets are stored encrypted for this Windows user only, under names starting with this.</summary>
    private const string SecretPrefix = "protected";

    /// <summary>The part of the settings that is shared: no local-only setting, no widget display, no secret.</summary>
    public static JsonObject Extract(JsonObject config)
    {
        var shared = (JsonObject)config.DeepClone();
        foreach (var name in shared.Select(p => p.Key).Where(LocalOnly.Contains).ToList()) shared.Remove(name);
        Walk(shared, item => item.Remove(DisplayField));
        RemoveSecrets(shared);
        return shared;
    }

    /// <summary>
    /// The local settings with the shared ones from another PC laid over them. This PC keeps its local-only settings,
    /// the displays of the widgets it knows, and its own secrets for the items it has.
    /// </summary>
    public static JsonObject Merge(JsonObject local, JsonObject shared)
    {
        var merged = (JsonObject)local.DeepClone();
        foreach (var (name, value) in shared)
        {
            if (LocalOnly.Contains(name)) continue;
            var copy = value?.DeepClone();
            // Something another PC should never have sent.
            RemoveSecrets(copy);
            merged.Remove(name);
            merged[name] = copy;
        }

        var displays = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var secrets = new Dictionary<string, List<(string Name, JsonNode? Value)>>(StringComparer.Ordinal);
        Walk(local, item =>
        {
            if (IdOf(item) is not { } id) return;
            if (item.TryGetPropertyValue(DisplayField, out var display)) displays[id] = display;
            if (item["settings"] is JsonObject settings)
            {
                var own = settings.Where(p => IsSecret(p.Key)).Select(p => (p.Key, p.Value)).ToList();
                if (own.Count > 0) secrets[id] = own;
            }
        });
        Walk(merged, item =>
        {
            if (IdOf(item) is not { } id) return;
            item.Remove(DisplayField);
            if (displays.TryGetValue(id, out var display)) item[DisplayField] = display?.DeepClone();
            if (secrets.TryGetValue(id, out var own))
            {
                if (item["settings"] is not JsonObject settings) item["settings"] = settings = new JsonObject();
                foreach (var (name, value) in own)
                {
                    settings.Remove(name);
                    settings[name] = value?.DeepClone();
                }
            }
        });
        return merged;
    }

    private static bool IsSecret(string name) => name.StartsWith(SecretPrefix, StringComparison.OrdinalIgnoreCase);

    private static string? IdOf(JsonObject item)
        => item["id"] is JsonValue value && value.TryGetValue(out string? id) ? id : null;

    /// <summary>Every dock item: in "items", in folders ("children") and in the profiles' saved items.</summary>
    private static void Walk(JsonObject config, Action<JsonObject> visit)
    {
        void Items(JsonNode? node)
        {
            if (node is not JsonArray array) return;
            foreach (var entry in array)
            {
                if (entry is not JsonObject item) continue;
                visit(item);
                Items(item["children"]);
            }
        }
        Items(config["items"]);
        if (config["profiles"] is JsonArray profiles)
            foreach (var profile in profiles.OfType<JsonObject>())
                Items(profile["items"]);
    }

    /// <summary>Takes every secret out, at any depth.</summary>
    private static void RemoveSecrets(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Select(p => p.Key).Where(IsSecret).ToList()) obj.Remove(name);
                foreach (var (_, child) in obj) RemoveSecrets(child);
                break;
            case JsonArray array:
                foreach (var child in array) RemoveSecrets(child);
                break;
        }
    }
}

/// <summary>
/// The shared file in the sync folder (DockHub-sync.json): which PC wrote it and when, and the shared settings.
/// </summary>
public sealed record SyncFile(string DeviceId, string DeviceName, DateTime UpdatedAt, JsonObject Settings)
{
    public const string FileName = "DockHub-sync.json";

    /// <summary>The file's layout; a newer DockHub writing a newer one isn't read by this one.</summary>
    public const int Format = 1;

    public string ToJson() => new JsonObject
    {
        ["app"] = "DockHub",
        ["format"] = Format,
        ["deviceId"] = DeviceId,
        ["deviceName"] = DeviceName,
        ["updatedAt"] = UpdatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        ["settings"] = Settings.DeepClone(),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>The file, or null when it is damaged, half written, or from a newer DockHub.</summary>
    public static SyncFile? Parse(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root) return null;
            if (root["format"] is not JsonValue format || !format.TryGetValue(out int version) || version != Format) return null;
            if (root["deviceId"]?.GetValue<string>() is not { Length: > 0 } device) return null;
            if (root["updatedAt"]?.GetValue<string>() is not { } stamp ||
                !DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var updated))
                return null;
            if (root["settings"] is not JsonObject settings) return null;
            return new SyncFile(device, root["deviceName"]?.GetValue<string>() ?? "", updated, (JsonObject)settings.DeepClone());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether this PC should take the file: another PC wrote it, after the last change this PC wrote or took (the
    /// last writer wins).
    /// </summary>
    public bool IsNewFor(string deviceId, DateTime? lastSynced)
        => !string.Equals(DeviceId, deviceId, StringComparison.Ordinal) && (lastSynced is null || UpdatedAt > lastSynced.Value.ToUniversalTime());
}
