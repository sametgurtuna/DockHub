using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>
/// One device the hardware battery scan can read. A null <see cref="Pid"/> matches every product of the vendor;
/// "{pid}" in <see cref="Name"/> is replaced with the product id.
/// </summary>
public sealed record BatteryCatalogEntry(string Protocol, ushort Vid, ushort? Pid, string Name,
    BatteryDeviceKind Kind = BatteryDeviceKind.Unknown, string? Id = null, bool Wired = false)
{
    public string DisplayName(ushort pid) => Name.Replace("{pid}", pid.ToString("X4", CultureInfo.InvariantCulture));
}

/// <summary>
/// The devices DockHub reads over HID, from <c>Resources/battery-devices.json</c> plus an optional
/// <c>%AppData%\DockHub\battery-devices.json</c> in the same format. A user entry replaces the built-in entry with the
/// same protocol, vendor and product id, so a new model can be added without a DockHub update.
/// </summary>
public sealed class BatteryDeviceCatalog
{
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);

    public BatteryDeviceCatalog(IEnumerable<BatteryCatalogEntry> entries) => Entries = entries.ToList();

    public IReadOnlyList<BatteryCatalogEntry> Entries { get; }

    public static string UserFile => Path.Combine(AppPaths.Root, "battery-devices.json");

    private static BatteryDeviceCatalog? s_builtIn;

    /// <summary>The catalog shipped with DockHub.</summary>
    public static BatteryDeviceCatalog BuiltIn => s_builtIn ??= LoadBuiltIn();

    private static BatteryDeviceCatalog LoadBuiltIn()
    {
        using var stream = typeof(BatteryDeviceCatalog).Assembly.GetManifestResourceStream("CustomDock.Resources.battery-devices.json");
        if (stream is null) return new(Array.Empty<BatteryCatalogEntry>());
        using var reader = new StreamReader(stream);
        var errors = new List<string>();
        var catalog = Parse(reader.ReadToEnd(), errors);
        foreach (var error in errors) Log.Warn($"Built-in battery catalog: {error}");
        return catalog;
    }

    /// <summary>The built-in catalog with the user's file (when present) merged over it.</summary>
    public static BatteryDeviceCatalog Load()
    {
        string path = UserFile;
        if (!File.Exists(path)) return BuiltIn;
        try
        {
            var errors = new List<string>();
            var user = Parse(File.ReadAllText(path), errors);
            foreach (var error in errors) Log.Warn($"{path}: {error}");
            return BuiltIn.MergedWith(user);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to read {path}");
            return BuiltIn;
        }
    }

    /// <summary>Parses a catalog file; invalid entries are skipped and described in <paramref name="errors"/>.</summary>
    public static BatteryDeviceCatalog Parse(string json, List<string>? errors = null)
    {
        var entries = new List<BatteryCatalogEntry>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            errors?.Add($"not valid JSON ({ex.Message})");
            return new(entries);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("devices", out var devices)
                || devices.ValueKind != JsonValueKind.Array)
            {
                errors?.Add("expected an object with a \"devices\" array");
                return new(entries);
            }

            int index = 0;
            foreach (var element in devices.EnumerateArray())
            {
                if (ParseEntry(element, out string? error) is { } entry) entries.Add(entry);
                else errors?.Add($"devices[{index}]: {error}");
                index++;
            }
        }
        return new(entries);
    }

    private static BatteryCatalogEntry? ParseEntry(JsonElement element, out string? error)
    {
        error = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = "not an object";
            return null;
        }

        string? protocol = String(element, "protocol");
        if (protocol is null || !BatteryProtocols.Known.Contains(protocol))
        {
            error = $"unknown protocol \"{protocol}\"";
            return null;
        }
        if (Hex(String(element, "vid")) is not { } vid)
        {
            error = "\"vid\" must be four hex digits";
            return null;
        }
        ushort? pid = null;
        if (String(element, "pid") is { } pidText)
        {
            if (Hex(pidText) is not { } parsed)
            {
                error = "\"pid\" must be four hex digits";
                return null;
            }
            pid = parsed;
        }
        string? name = String(element, "name")?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            error = "\"name\" is required";
            return null;
        }

        var kind = BatteryDeviceKind.Unknown;
        if (String(element, "kind") is { } kindText && !Enum.TryParse(kindText, ignoreCase: true, out kind))
        {
            error = $"unknown kind \"{kindText}\"";
            return null;
        }
        string? id = String(element, "id");
        if (id is not null && !IdPattern.IsMatch(id))
        {
            error = "\"id\" may only use lower-case letters, digits and dashes";
            return null;
        }
        bool wired = element.TryGetProperty("wired", out var w) && w.ValueKind == JsonValueKind.True;

        return new BatteryCatalogEntry(protocol.ToLowerInvariant(), vid, pid, name, kind, id, wired);
    }

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static ushort? Hex(string? text)
        => text is { Length: 4 } && ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value) ? value : null;

    /// <summary>This catalog with <paramref name="overrides"/> added; an override replaces the entry for the same device.</summary>
    public BatteryDeviceCatalog MergedWith(BatteryDeviceCatalog overrides)
    {
        var keys = overrides.Entries.Select(Key).ToHashSet();
        return new(overrides.Entries.Concat(Entries.Where(e => !keys.Contains(Key(e)))));
    }

    private static (string, ushort, ushort?) Key(BatteryCatalogEntry entry) => (entry.Protocol, entry.Vid, entry.Pid);

    /// <summary>The entry for a device: an exact product match first, then a vendor-wide entry.</summary>
    public BatteryCatalogEntry? Find(string protocol, ushort vid, ushort pid)
    {
        BatteryCatalogEntry? vendorWide = null;
        foreach (var entry in Entries)
        {
            if (!string.Equals(entry.Protocol, protocol, StringComparison.OrdinalIgnoreCase) || entry.Vid != vid) continue;
            if (entry.Pid == pid) return entry;
            if (entry.Pid is null) vendorWide ??= entry;
        }
        return vendorWide;
    }

    /// <summary>Entries of one protocol, in catalog order.</summary>
    public IEnumerable<BatteryCatalogEntry> ForProtocol(string protocol)
        => Entries.Where(e => string.Equals(e.Protocol, protocol, StringComparison.OrdinalIgnoreCase));
}
