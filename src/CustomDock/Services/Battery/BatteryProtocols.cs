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
    public const string PlayStation = "playstation";
    public const string Razer = "razer";
    public const string Logitech = "hidpp";
    public const string HidRequest = "hidrequest";

    /// <summary>Protocols a catalog entry may name.</summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        HyperX, Compx, PlayStation, Razer, Logitech, HidRequest,
    };

    /// <summary>True for HID paths of Bluetooth devices (classic HID profile or HID over GATT).</summary>
    public static bool IsBluetoothHidPath(string devicePath)
        => devicePath.Contains("00001124-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase)
           || devicePath.Contains("00001812-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase);

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

    // ------------------------------------------------------------------ PlayStation controllers

    /// <summary>
    /// DualShock 4 status byte (byte 30 of USB report 0x01, byte 32 of Bluetooth report 0x11): battery in the low
    /// nibble (0-10, 11 = full), cable in bit 4. Over Bluetooth the short report 0x01 has no battery, so it is skipped.
    /// </summary>
    public static BatteryReading? ParseDualShock4(ReadOnlySpan<byte> report, bool bluetooth)
    {
        int offset = (bluetooth, report.Length > 0 ? report[0] : -1) switch
        {
            (false, 0x01) => 30,
            (true, 0x11) => 32,
            _ => -1,
        };
        if (offset < 0 || report.Length <= offset) return null;

        int level = report[offset] & 0x0F;
        bool cable = (report[offset] & 0x10) != 0;
        if (level > 11) return null;
        if (!cable) return new BatteryReading(Math.Min(level * 10 + 5, 100), false);
        return level >= 10 ? new BatteryReading(100, level == 10) : new BatteryReading(Math.Min(level * 10 + 5, 100), true);
    }

    /// <summary>
    /// DualSense status byte (byte 53 of USB report 0x01, byte 54 of Bluetooth report 0x31): battery 0-10 in the low
    /// nibble, charging state in the high nibble (0 discharging, 1 charging, 2 full, others are errors).
    /// </summary>
    public static BatteryReading? ParseDualSense(ReadOnlySpan<byte> report, bool bluetooth)
    {
        int offset = (bluetooth, report.Length > 0 ? report[0] : -1) switch
        {
            (false, 0x01) => 53,
            (true, 0x31) => 54,
            _ => -1,
        };
        if (offset < 0 || report.Length <= offset) return null;

        int level = report[offset] & 0x0F;
        int state = report[offset] >> 4;
        int percent = Math.Min(level * 10 + 5, 100);
        return state switch
        {
            0x0 => new BatteryReading(percent, false),
            0x1 => new BatteryReading(percent, true),
            0x2 => new BatteryReading(100, false),
            _ => null,
        };
    }

    // ------------------------------------------------------------------ Razer

    public const int RazerReportLength = 90;
    public const byte RazerStatusNew = 0x00;
    public const byte RazerStatusBusy = 0x01;
    public const byte RazerStatusOk = 0x02;
    public const byte RazerBatteryClass = 0x07;
    public const byte RazerBatteryLevel = 0x80;
    public const byte RazerChargingStatus = 0x84;

    /// <summary>Transaction ids used by Razer mice, newest first (OpenRazer); the first one that answers is kept.</summary>
    public static readonly byte[] RazerTransactionIds = { 0x1F, 0x3F, 0xFF };

    /// <summary>
    /// A 90-byte Razer report: status, transaction id, remaining packets (2), protocol type, data size, command class,
    /// command id, 80 argument bytes, CRC (XOR of bytes 2-87) and a reserved byte.
    /// </summary>
    public static byte[] RazerRequest(byte transactionId, byte commandClass, byte commandId, byte dataSize)
    {
        var report = new byte[RazerReportLength];
        report[0] = RazerStatusNew;
        report[1] = transactionId;
        report[5] = dataSize;
        report[6] = commandClass;
        report[7] = commandId;
        report[88] = RazerCrc(report);
        return report;
    }

    public static byte RazerCrc(ReadOnlySpan<byte> report)
    {
        byte crc = 0;
        for (int i = 2; i < 88 && i < report.Length; i++) crc ^= report[i];
        return crc;
    }

    /// <summary>
    /// The first argument bytes of a successful answer to <paramref name="commandClass"/>/<paramref name="commandId"/>,
    /// or null (busy, failed, another command, bad CRC). <paramref name="busy"/> tells the caller to ask again.
    /// </summary>
    public static byte[]? ParseRazerResponse(ReadOnlySpan<byte> response, byte commandClass, byte commandId, out bool busy)
    {
        busy = response.Length >= RazerReportLength && response[0] == RazerStatusBusy;
        if (response.Length < RazerReportLength || response[0] != RazerStatusOk) return null;
        if (response[6] != commandClass || response[7] != commandId) return null;
        if (response[88] != RazerCrc(response)) return null;
        return response.Slice(8, 4).ToArray();
    }

    /// <summary>Razer reports the level as 0-255.</summary>
    public static int RazerPercent(byte raw) => (int)Math.Round(raw * 100 / 255.0);

    // ------------------------------------------------------------------ Logitech HID++ 2.0

    public const byte HidppShortReport = 0x10;
    public const byte HidppLongReport = 0x11;
    public const int HidppLongLength = 20;
    public const byte HidppSoftwareId = 0x0B;
    public const byte HidppDirectDevice = 0xFF;
    public const ushort HidppFeatureDeviceName = 0x0005;
    public const ushort HidppFeatureBatteryStatus = 0x1000;
    public const ushort HidppFeatureBatteryVoltage = 0x1001;
    public const ushort HidppFeatureUnifiedBattery = 0x1004;

    /// <summary>A long HID++ request: report 0x11, device index, feature index, function in the high nibble with our software id.</summary>
    public static byte[] HidppRequest(byte deviceIndex, byte featureIndex, byte function, params byte[] parameters)
    {
        var report = new byte[HidppLongLength];
        report[0] = HidppLongReport;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)((function << 4) | HidppSoftwareId);
        parameters.AsSpan(0, Math.Min(parameters.Length, HidppLongLength - 4)).CopyTo(report.AsSpan(4));
        return report;
    }

    public enum HidppAnswer { Unrelated, Reply, Error }

    /// <summary>
    /// Classifies an input report against a request: our reply (parameters from byte 4), an error for it (HID++ 2.0
    /// error 0xFF or HID++ 1.0 error 0x8F), or something else (notifications, other requests).
    /// </summary>
    public static HidppAnswer MatchHidpp(ReadOnlySpan<byte> report, byte deviceIndex, byte featureIndex, byte function, out byte[] parameters)
    {
        parameters = Array.Empty<byte>();
        if (report.Length < 7 || report[0] is not (HidppShortReport or HidppLongReport) || report[1] != deviceIndex) return HidppAnswer.Unrelated;

        byte functionAndId = (byte)((function << 4) | HidppSoftwareId);
        if (report[2] == 0xFF && report[3] == featureIndex && report[4] == functionAndId) return HidppAnswer.Error;
        if (report[2] == 0x8F && report[3] == featureIndex && report[4] == functionAndId) return HidppAnswer.Error;
        if (report[2] != featureIndex || report[3] != functionAndId) return HidppAnswer.Unrelated;

        parameters = report[4..].ToArray();
        return HidppAnswer.Reply;
    }

    /// <summary>DEVICE_NAME (0x0005) getDeviceType: 0 keyboard, 3 mouse, 4 trackpad, 5 trackball, 8 headset, 0x0C gamepad.</summary>
    public static BatteryDeviceKind HidppDeviceKind(byte type) => type switch
    {
        0x00 or 0x02 => BatteryDeviceKind.Keyboard,
        0x03 or 0x04 or 0x05 => BatteryDeviceKind.Mouse,
        0x08 => BatteryDeviceKind.Headset,
        0x0B or 0x0C => BatteryDeviceKind.Controller,
        _ => BatteryDeviceKind.Unknown,
    };

    /// <summary>UNIFIED_BATTERY (0x1004) get status: percent, level flags, charging state.</summary>
    public static BatteryReading? ParseHidppUnifiedBattery(ReadOnlySpan<byte> p)
    {
        if (p.Length < 3) return null;
        int percent = p[0] is > 0 and <= 100 ? p[0] : ApproximateHidppLevel(p[1]);
        if (percent < 0) return null;
        bool charging = p[2] is 1 or 2;
        return new BatteryReading(p[2] == 3 ? 100 : percent, charging);
    }

    /// <summary>Level flags of UNIFIED_BATTERY for devices without a percentage (Solaar's approximations).</summary>
    private static int ApproximateHidppLevel(byte flags)
    {
        if ((flags & 0x08) != 0) return 90;
        if ((flags & 0x04) != 0) return 50;
        if ((flags & 0x02) != 0) return 20;
        if ((flags & 0x01) != 0) return 5;
        return -1;
    }

    /// <summary>BATTERY_STATUS (0x1000) get level: percent, next level, status (1, 2, 4 charging; 3 full).</summary>
    public static BatteryReading? ParseHidppBatteryStatus(ReadOnlySpan<byte> p)
    {
        if (p.Length < 3) return null;
        if (p[2] == 3) return new BatteryReading(100, false);
        if (p[0] is 0 or > 100) return null;
        return new BatteryReading(p[0], p[2] is 1 or 2 or 4);
    }

    /// <summary>
    /// BATTERY_VOLTAGE (0x1001) get info: millivolts (big-endian) and flags. Bit 7 means external power; then the low
    /// bits say charging (0), full (1) or not charging (2).
    /// </summary>
    public static BatteryReading? ParseHidppBatteryVoltage(ReadOnlySpan<byte> p)
    {
        if (p.Length < 3) return null;
        int millivolts = (p[0] << 8) | p[1];
        if (millivolts is < 2500 or > 5000) return null;
        bool external = (p[2] & 0x80) != 0;
        int state = p[2] & 0x07;
        if (external && state == 0x01) return new BatteryReading(100, false);
        return new BatteryReading(VoltageToPercent(millivolts), external && state == 0x00);
    }

    // Li-ion discharge curve used by Solaar for devices that only report a voltage.
    private static readonly (int MilliVolts, int Percent)[] VoltageCurve =
    {
        (4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60), (3811, 50),
        (3778, 40), (3751, 30), (3717, 20), (3671, 10), (3646, 5), (3579, 2), (3500, 0),
    };

    public static int VoltageToPercent(int millivolts)
    {
        if (millivolts >= VoltageCurve[0].MilliVolts) return 100;
        for (int i = 1; i < VoltageCurve.Length; i++)
        {
            var (highV, highP) = VoltageCurve[i - 1];
            var (lowV, lowP) = VoltageCurve[i];
            if (millivolts >= lowV)
                return (int)Math.Round(lowP + (double)(millivolts - lowV) / (highV - lowV) * (highP - lowP));
        }
        return 0;
    }

    // ------------------------------------------------------------------ Generic request / response

    /// <summary>
    /// Reading from the answer to a catalog-defined request: the answer must start with <see cref="HidRequestSpec.Match"/>,
    /// the level byte is scaled from 0-<see cref="HidRequestSpec.LevelMax"/> to percent.
    /// </summary>
    public static BatteryReading? ParseHidRequest(HidRequestSpec spec, ReadOnlySpan<byte> response)
    {
        if (response.Length <= spec.LevelOffset || !response.StartsWith(spec.Match)) return null;
        int raw = response[spec.LevelOffset];
        if (spec.OfflineValue is { } offline && raw == offline) return null;
        int percent = Math.Clamp((int)Math.Round(raw * 100.0 / spec.LevelMax), 0, 100);
        bool charging = spec.ChargingOffset is { } offset && offset < response.Length && response[offset] == spec.ChargingValue;
        return new BatteryReading(percent, charging);
    }

    // ------------------------------------------------------------------ Game controllers (Windows.Gaming.Input)

    /// <summary>Percent from a controller battery report, or null when it reports no capacity.</summary>
    public static int? GamepadPercent(int? remainingMilliwattHours, int? fullMilliwattHours)
        => remainingMilliwattHours is { } remaining && fullMilliwattHours is { } full && full > 0
            ? Math.Clamp((int)Math.Round(remaining * 100.0 / full), 0, 100)
            : null;
}
