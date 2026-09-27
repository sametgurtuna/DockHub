using System.Text.RegularExpressions;

namespace CustomDock.Services;

/// <summary>A battery reading: level in percent and whether the device is charging.</summary>
public readonly record struct BatteryReading(int Percent, bool IsCharging);

/// <summary>
/// Packet building and parsing for the hardware battery protocols. Kept free of Windows calls so every byte layout
/// is covered by unit tests; <see cref="DeviceBatteryService"/> does the I/O.
/// </summary>
public static class BatteryProtocols
{
    public const string HyperX = "hyperx";
    public const string Compx = "compx";

    /// <summary>Protocols a catalog entry may name.</summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { HyperX, Compx };

    // ------------------------------------------------------------------ Device paths

    private static readonly Regex UsbIds = new(@"vid_([0-9a-f]{4})&pid_([0-9a-f]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Bluetooth HID paths carry the vendor id source in front of the id: "vid&0002046d_pid&b01a" (classic) or "vid&02046d" (LE).
    private static readonly Regex BluetoothIds = new(@"vid&([0-9a-f]{6}|[0-9a-f]{8})_pid&([0-9a-f]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Vendor and product id from a HID device path, without opening the device.</summary>
    public static (ushort Vid, ushort Pid)? ParseVidPid(string devicePath)
    {
        if (UsbIds.Match(devicePath) is { Success: true } usb)
            return (Convert.ToUInt16(usb.Groups[1].Value, 16), Convert.ToUInt16(usb.Groups[2].Value, 16));
        if (BluetoothIds.Match(devicePath) is { Success: true } bt)
            return (Convert.ToUInt16(bt.Groups[1].Value[^4..], 16), Convert.ToUInt16(bt.Groups[2].Value, 16));
        return null;
    }

    private static readonly Regex ColonAddress = new(@"([0-9a-f]{2}(?::[0-9a-f]{2}){5})\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LeDevAddress = new(@"dev_([0-9a-f]{12})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ClassicAddress = new(@"&([0-9a-f]{12})_c[0-9a-f]{8}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Bluetooth address (12 upper-case hex digits) from an association endpoint id ("Bluetooth#Bluetooth…-aa:bb:…")
    /// or a device instance id ("BTHLE\DEV_AABBCC…" or "BTHENUM\…&amp;AABBCC…_C00000000").
    /// </summary>
    public static string? ParseBluetoothAddress(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var match = ColonAddress.Match(id);
        if (match.Success) return match.Groups[1].Value.Replace(":", "").ToUpperInvariant();
        match = LeDevAddress.Match(id);
        if (match.Success) return match.Groups[1].Value.ToUpperInvariant();
        match = ClassicAddress.Match(id);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>Normalizes an address in any common spelling ("AA:BB:…", "aabb…", 0xAABB…) to 12 upper-case hex digits.</summary>
    public static string? NormalizeBluetoothAddress(object? value) => value switch
    {
        ulong number => number.ToString("X12"),
        string text when text.Replace(":", "").Replace("-", "") is { Length: 12 } hex && IsHex(hex) => hex.ToUpperInvariant(),
        _ => null,
    };

    private static bool IsHex(string text) => text.All(Uri.IsHexDigit);

    // ------------------------------------------------------------------ Compx (LAMZU)

    public const int CompxReportLength = 17;
    public const byte CompxCommandReport = 0x08;
    public const byte CompxResponseReport = 0x09;
    public const byte CompxBatteryCommand = 0x04;

    /// <summary>
    /// A 17-byte Compx feature report: report id 0x08, the command in byte 1, and a last byte that makes the sum of
    /// all bytes 0x55.
    /// </summary>
    public static byte[] CompxCommand(byte command)
    {
        var report = new byte[CompxReportLength];
        report[0] = CompxCommandReport;
        report[1] = command;
        int sum = 0;
        for (int i = 0; i < report.Length - 1; i++) sum += report[i];
        report[^1] = (byte)(0x55 - sum);
        return report;
    }

    /// <summary>The battery answer: report 0x09 echoing command 0x04, level in byte 6, charging flag in byte 7.</summary>
    public static bool IsCompxBatteryResponse(ReadOnlySpan<byte> response)
        => response.Length >= 8 && response[0] == CompxResponseReport && response[1] == CompxBatteryCommand;

    /// <summary>The reading from a battery answer, or null when the level is outside 1-100 (mouse asleep).</summary>
    public static BatteryReading? ParseCompxBattery(ReadOnlySpan<byte> response)
        => IsCompxBatteryResponse(response) && response[6] is > 0 and <= 100
            ? new BatteryReading(response[6], response[7] != 0)
            : null;

    // ------------------------------------------------------------------ HyperX

    /// <summary>Cloud II Wireless status input report: 0x0B, ?, 0xBB, 0x02, charging (1 or 2), ?, ?, level.</summary>
    public static BatteryReading? ParseHyperXInput(ReadOnlySpan<byte> report)
        => report.Length >= 8 && report[0] == 0x0B && report[2] == 0xBB && report[3] == 0x02 && report[7] <= 100
            ? new BatteryReading(report[7], report[4] is 1 or 2)
            : null;

    /// <summary>Feature report 0x21 answer (Cloud Flight, Cloud Alpha): the first byte from offset 2 in 1-100 is the level.</summary>
    public static BatteryReading? ParseHyperXFeature(ReadOnlySpan<byte> response)
    {
        for (int offset = 2; offset < Math.Min(20, response.Length); offset++)
        {
            if (response[offset] is > 0 and <= 100)
            {
                bool charging = offset + 1 < response.Length && response[offset + 1] is 1 or 2;
                return new BatteryReading(response[offset], charging);
            }
        }
        return null;
    }

    /// <summary>The feature request that makes Cloud Flight / Cloud Alpha report their battery.</summary>
    public static byte[] HyperXFeatureRequest()
    {
        var report = new byte[32];
        report[0] = 0x21;
        report[1] = 0xBB;
        report[2] = 0x0B;
        return report;
    }
}
