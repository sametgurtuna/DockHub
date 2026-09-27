using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Services;

public sealed partial class DeviceBatteryService
{
    /// <summary>
    /// DualShock 4 and DualSense controllers: the battery is part of the input report they stream all the time, so
    /// reading one report is enough and nothing is sent to the controller. Over Bluetooth only the full reports carry
    /// it; switching a controller to them would break DirectInput games, so a controller in basic mode is skipped.
    /// </summary>
    private static List<BatteryDeviceInfo> ScanPlayStation(IReadOnlyList<HidPath> hidPaths, BatteryDeviceCatalog catalog)
    {
        var result = new List<BatteryDeviceInfo>();
        foreach (var hid in hidPaths)
        {
            if (catalog.Find(BatteryProtocols.PlayStation, hid.Vid, hid.Pid) is not { Variant: { } variant } entry) continue;

            bool bluetooth = BatteryProtocols.IsBluetoothHidPath(hid.Path);
            string id = entry.Id ?? $"ps-{hid.Pid:x4}-{StableHash(hid.Path)}";
            if (result.Any(d => d.Id == id)) continue;

            try
            {
                if (ReadPlayStation(hid.Path, variant, bluetooth) is { } reading)
                    result.Add(Remember(new BatteryDeviceInfo(id, entry.DisplayName(hid.Pid), reading.Percent, reading.IsCharging, false, entry.Kind)));
                else if (Recall(id) is { } cached)
                    result.Add(cached);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"PlayStation battery read failed: {entry.Name}");
            }
        }
        return result;
    }

    private static BatteryReading? ReadPlayStation(string path, string variant, bool bluetooth)
    {
        IntPtr handle = OpenHid(path, write: false);
        if (handle == IntPtr.Zero) return null;
        try
        {
            if (GetCaps(handle) is not { UsagePage: 0x01, InputReportByteLength: >= 32 } caps) return null;

            var report = new byte[caps.InputReportByteLength];
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (!ReadInputReport(handle, report, 250)) return null;
                var reading = variant == "dualsense"
                    ? BatteryProtocols.ParseDualSense(report, bluetooth)
                    : BatteryProtocols.ParseDualShock4(report, bluetooth);
                if (reading is not null) return reading;
            }
            return null;
        }
        finally
        {
            HidInterop.CloseHandle(handle);
        }
    }
}
