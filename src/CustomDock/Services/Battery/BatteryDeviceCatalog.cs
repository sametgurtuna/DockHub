using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>
/// One device the hardware battery scan can read. A null <see cref="Pid"/> matches every product of the vendor;
/// "{pid}" in <see cref="Name"/> is replaced with the product id. <see cref="Variant"/> picks a protocol flavor
/// ("ds4" or "dualsense" for PlayStation, "receiver" for Logitech receivers); <see cref="Request"/> describes a
/// <c>hidrequest</c> device.
/// </summary>
public sealed record BatteryCatalogEntry(string Protocol, ushort Vid, ushort? Pid, string Name,
    BatteryDeviceKind Kind = BatteryDeviceKind.Unknown, string? Id = null, bool Wired = false,
    string? Variant = null, HidRequestSpec? Request = null)
{
    public string DisplayName(ushort pid) => Name.Replace("{pid}", pid.ToString("X4", CultureInfo.InvariantCulture));
}

/// <summary>
/// A battery query defined in the catalog: <see cref="Request"/> (first byte = report id) is sent as an output or
/// feature report on the collection with <see cref="UsagePage"/>; the answer must start with <see cref="Match"/> and
/// carries the level at <see cref="LevelOffset"/> (0-<see cref="LevelMax"/>).
/// </summary>
public sealed record HidRequestSpec(bool Feature, byte[] Request, ushort? UsagePage, byte[] Match, int LevelOffset,
    int LevelMax = 100, int? ChargingOffset = null, byte ChargingValue = 1, int? OfflineValue = null);

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

        protocol = protocol.ToLowerInvariant();
        string? variant = String(element, "variant")?.ToLowerInvariant();
        string[]? variants = protocol switch
        {
            BatteryProtocols.PlayStation => new[] { "ds4", "dualsense" },
            BatteryProtocols.Logitech => new[] { "receiver", "device" },
            _ => null,
        };
        if (variants is not null && (variant is null || !variants.Contains(variant)))
        {
            error = $"\"variant\" must be {string.Join(" or ", variants.Select(v => $"\"{v}\""))}";
            return null;
        }

        HidRequestSpec? request = null;
        if (protocol == BatteryProtocols.HidRequest)
        {
            request = ParseRequest(element, out error);
            if (request is null) return null;
        }

        return new BatteryCatalogEntry(protocol, vid, pid, name, kind, id, wired, variant, request);
    }

    private static HidRequestSpec? ParseRequest(JsonElement element, out string? error)
    {
        error = null;
        string method = String(element, "method")?.ToLowerInvariant() ?? "output";
        if (method is not ("output" or "feature"))
        {
            error = "\"method\" must be \"output\" or \"feature\"";
            return null;
        }
        if (Bytes(String(element, "request")) is not { Length: > 0 and <= 64 } request)
        {
            error = "\"request\" must be 1-64 hex bytes, for example \"06 18\"";
            return null;
        }
        ushort? usagePage = null;
        if (String(element, "usagePage") is { } pageText)
        {
            if (Hex(pageText) is not { } page)
            {
                error = "\"usagePage\" must be four hex digits";
                return null;
            }
            usagePage = page;
        }
        byte[] match = Array.Empty<byte>();
        if (String(element, "match") is { } matchText)
        {
            if (Bytes(matchText) is not { } parsedMatch)
            {
                error = "\"match\" must be hex bytes";
                return null;
            }
            match = parsedMatch;
        }

        if (Int(element, "levelOffset") is not { } levelOffset || levelOffset is < 0 or >= 64)
        {
            error = "\"levelOffset\" must be 0-63";
            return null;
        }
        int levelMax = Int(element, "levelMax") ?? 100;
        int? chargingOffset = Int(element, "chargingOffset");
        int chargingValue = Int(element, "chargingValue") ?? 1;
        int? offlineValue = Int(element, "offlineValue");
        if (levelMax is < 1 or > 255 || chargingOffset is < 0 or >= 64 || chargingValue is < 0 or > 255 || offlineValue is < 0 or > 255)
        {
            error = "\"levelMax\" must be 1-255, \"chargingOffset\" 0-63, \"chargingValue\" and \"offlineValue\" 0-255";
            return null;
        }
        return new HidRequestSpec(method == "feature", request, usagePage, match, levelOffset, levelMax,
            chargingOffset, (byte)chargingValue, offlineValue);
    }

    private static int? Int(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;

    /// <summary>Hex bytes separated by spaces (or not): "06 18", "0618".</summary>
    private static byte[]? Bytes(string? text)
    {
        if (text is null) return null;
        string hex = text.Replace(" ", "").Replace("-", "");
        if (hex.Length % 2 != 0 || !hex.All(Uri.IsHexDigit)) return null;
        return Convert.FromHexString(hex);
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
