using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Services;

public sealed partial class DeviceBatteryService
{
    /// <summary>What a HID++ device offers, found once per connection: the battery feature and its index, name, kind.</summary>
    private sealed record HidppDevice(ushort BatteryFeature, byte BatteryIndex, string? Name, BatteryDeviceKind Kind);

    private static readonly Dictionary<string, HidppDevice> s_hidppDevices = new();

    private static readonly System.Text.RegularExpressions.Regex CollectionSuffix =
        new(@"&col[0-9a-f]{2}", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>HID++ long-report collection: 0xFF00/0x0002 on USB receivers and cables, 0xFF43/0x0202 over Bluetooth LE.</summary>
    private static bool IsHidppLong(HidInterop.HIDP_CAPS caps)
        => ((caps.UsagePage == 0xFF00 && caps.Usage == 0x0002) || (caps.UsagePage == 0xFF43 && caps.Usage == 0x0202))
           && caps.InputReportByteLength == BatteryProtocols.HidppLongLength
           && caps.OutputReportByteLength == BatteryProtocols.HidppLongLength;

    /// <summary>HID++ short-report collection, where receivers send their HID++ 1.0 errors (empty slot, device off).</summary>
    private static bool IsHidppShort(HidInterop.HIDP_CAPS caps)
        => caps.UsagePage == 0xFF00 && caps.Usage == 0x0001 && caps.InputReportByteLength > 0;

    /// <summary>
    /// Logitech HID++ 2.0: devices paired to Unifying, Lightspeed and Bolt receivers (slots 1-6), and devices on a
    /// cable or Bluetooth (index 0xFF). Only feature lookups and battery/name reads are sent.
    /// </summary>
    private static List<BatteryDeviceInfo> ScanLogitech(IReadOnlyList<HidPath> hidPaths, BatteryDeviceCatalog catalog)
    {
        var result = new List<BatteryDeviceInfo>();
        // The collections of one interface differ only in their "&colNN" part.
        var interfaces = hidPaths
            .Where(h => catalog.Find(BatteryProtocols.Logitech, h.Vid, h.Pid) is not null)
            .GroupBy(h => CollectionSuffix.Replace(h.Path, ""), StringComparer.OrdinalIgnoreCase);

        foreach (var collections in interfaces)
        {
            var first = collections.First();
            var entry = catalog.Find(BatteryProtocols.Logitech, first.Vid, first.Pid)!;
            IntPtr longHandle = IntPtr.Zero, shortHandle = IntPtr.Zero;
            int shortLength = 0;
            var reads = new HidReadGroup();
            try
            {
                foreach (var collection in collections)
                {
                    IntPtr handle = OpenHid(collection.Path);
                    if (handle == IntPtr.Zero) continue;
                    var caps = GetCaps(handle);
                    if (caps is { } c && longHandle == IntPtr.Zero && IsHidppLong(c))
                        longHandle = handle;
                    else if (caps is { } d && shortHandle == IntPtr.Zero && IsHidppShort(d))
                        (shortHandle, shortLength) = (handle, d.InputReportByteLength);
                    else
                        HidInterop.CloseHandle(handle);
                }
                if (longHandle == IntPtr.Zero) continue;

                reads.Add(longHandle, BatteryProtocols.HidppLongLength);
                if (shortHandle != IntPtr.Zero) reads.Add(shortHandle, shortLength);

                bool receiver = entry.Variant == "receiver";
                var slots = receiver ? new byte[] { 1, 2, 3, 4, 5, 6 } : new[] { BatteryProtocols.HidppDirectDevice };
                foreach (byte slot in slots)
                {
                    string id = receiver ? $"logi-{first.Pid:x4}-{slot}" : $"logi-{first.Pid:x4}";
                    if (result.Any(d => d.Id == id)) continue;
                    var link = new HidppLink(longHandle, reads, slot, collections.Key + "|" + slot);

                    var device = ReadLogitech(link, entry.DisplayName(first.Pid), id, entry.Kind);
                    if (device is not null) result.Add(Remember(device));
                    else if (Recall(id) is { } cached) result.Add(cached); // asleep or switched off: keep the last level
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Logitech battery read failed: {first.Path}");
            }
            finally
            {
                reads.Dispose(); // cancels the pending reads before their handles close
                if (longHandle != IntPtr.Zero) HidInterop.CloseHandle(longHandle);
                if (shortHandle != IntPtr.Zero) HidInterop.CloseHandle(shortHandle);
            }
        }
        return result;
    }

    /// <summary>One HID++ device: where requests are written, where answers are read, its slot and cache key.</summary>
    private sealed record HidppLink(IntPtr Handle, HidReadGroup Reads, byte Slot, string Key);

    private static BatteryDeviceInfo? ReadLogitech(HidppLink link, string fallbackName, string id, BatteryDeviceKind fallbackKind)
    {
        if (!s_hidppDevices.TryGetValue(link.Key, out var device))
        {
            device = DiscoverLogitech(link);
            if (device is null) return null; // empty slot, switched off, or not HID++ 2.0
            s_hidppDevices[link.Key] = device;
        }

        var reading = ReadLogitechBattery(link, device);
        if (reading is null)
        {
            // The device may have been replaced in this slot: look its features up again next time.
            s_hidppDevices.Remove(link.Key);
            return null;
        }

        var kind = device.Kind != BatteryDeviceKind.Unknown ? device.Kind : fallbackKind;
        return new BatteryDeviceInfo(id, device.Name ?? fallbackName, reading.Value.Percent, reading.Value.IsCharging,
            link.Slot != BatteryProtocols.HidppDirectDevice, kind);
    }

    private static HidppDevice? DiscoverLogitech(HidppLink link)
    {
        foreach (ushort feature in new[] { BatteryProtocols.HidppFeatureUnifiedBattery, BatteryProtocols.HidppFeatureBatteryStatus, BatteryProtocols.HidppFeatureBatteryVoltage })
        {
            var index = HidppFeatureIndex(link, feature, out bool unreachable);
            if (unreachable) return null;
            if (index is not { } batteryIndex) continue;

            string? name = null;
            var kind = BatteryDeviceKind.Unknown;
            if (HidppFeatureIndex(link, BatteryProtocols.HidppFeatureDeviceName, out _) is { } nameIndex)
            {
                name = HidppDeviceName(link, nameIndex);
                if (HidppCall(link, nameIndex, 2) is { Length: > 0 } type) kind = BatteryProtocols.HidppDeviceKind(type[0]);
            }
            return new HidppDevice(feature, batteryIndex, name, kind);
        }
        return null;
    }

    private static BatteryReading? ReadLogitechBattery(HidppLink link, HidppDevice device) => device.BatteryFeature switch
    {
        BatteryProtocols.HidppFeatureUnifiedBattery => HidppCall(link, device.BatteryIndex, 1) is { } p ? BatteryProtocols.ParseHidppUnifiedBattery(p) : null,
        BatteryProtocols.HidppFeatureBatteryStatus => HidppCall(link, device.BatteryIndex, 0) is { } p ? BatteryProtocols.ParseHidppBatteryStatus(p) : null,
        _ => HidppCall(link, device.BatteryIndex, 0) is { } p ? BatteryProtocols.ParseHidppBatteryVoltage(p) : null,
    };

    /// <summary>
    /// The index of a feature through the root feature's getFeature; null when the device doesn't have it.
    /// <paramref name="unreachable"/> is set when the device didn't answer at all.
    /// </summary>
    private static byte? HidppFeatureIndex(HidppLink link, ushort feature, out bool unreachable)
    {
        var reply = HidppCall(link, 0x00, 0, (byte)(feature >> 8), (byte)feature);
        unreachable = reply is null;
        return reply is { Length: > 0 } && reply[0] != 0 ? reply[0] : null;
    }

    private static string? HidppDeviceName(HidppLink link, byte nameIndex)
    {
        if (HidppCall(link, nameIndex, 0) is not { Length: > 0 } count || count[0] is 0 or > 64) return null;
        var chars = new List<byte>();
        while (chars.Count < count[0])
        {
            if (HidppCall(link, nameIndex, 1, (byte)chars.Count) is not { Length: > 0 } part) break;
            int take = Math.Min(part.Length, count[0] - chars.Count);
            chars.AddRange(part.Take(take));
            if (take == 0) break;
        }
        string name = System.Text.Encoding.UTF8.GetString(chars.ToArray()).TrimEnd('\0', ' ');
        return name.Length > 0 ? name : null;
    }

    /// <summary>Sends one long HID++ request and waits for its reply; null on error or silence.</summary>
    private static byte[]? HidppCall(HidppLink link, byte featureIndex, byte function, params byte[] parameters)
    {
        var request = BatteryProtocols.HidppRequest(link.Slot, featureIndex, function, parameters);
        if (!WriteOutputReport(link.Handle, request, BatteryProtocols.HidppLongLength, 300)) return null;

        long deadline = Environment.TickCount64 + 500;
        while (Environment.TickCount64 < deadline)
        {
            uint wait = (uint)Math.Max(1, deadline - Environment.TickCount64);
            if (link.Reads.Next(wait) is not { } report) return null;
            switch (BatteryProtocols.MatchHidpp(report, link.Slot, featureIndex, function, out var reply))
            {
                case BatteryProtocols.HidppAnswer.Reply:
                    return reply;
                case BatteryProtocols.HidppAnswer.Error:
                    return null;
            }
        }
        return null;
    }
}
