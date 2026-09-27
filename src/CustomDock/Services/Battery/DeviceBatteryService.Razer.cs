using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Services;

public sealed partial class DeviceBatteryService
{
    private const int RazerFeatureLength = BatteryProtocols.RazerReportLength + 1; // report id 0 in front

    /// <summary>Transaction id that answered for each device path, so later scans ask with it first.</summary>
    private static readonly Dictionary<string, byte> s_razerTransaction = new();

    /// <summary>Razer mice (receiver or cable): battery level and charging state over 90-byte feature reports.</summary>
    private static List<BatteryDeviceInfo> ScanRazer(IReadOnlyList<HidPath> hidPaths, BatteryDeviceCatalog catalog)
    {
        var result = new List<BatteryDeviceInfo>();
        foreach (var entry in catalog.ForProtocol(BatteryProtocols.Razer))
        {
            if (entry.Pid is not { } pid) continue;
            string id = entry.Id ?? $"razer-{pid:x4}";
            if (result.Any(d => d.Id == id)) continue;
            var paths = hidPaths.Where(p => p.Vid == entry.Vid && p.Pid == pid).Select(p => p.Path).ToList();
            if (paths.Count == 0) continue;

            try
            {
                if (ReadRazer(paths) is { } reading)
                {
                    bool charging = reading.IsCharging || entry.Wired;
                    result.Add(Remember(new BatteryDeviceInfo(id, entry.DisplayName(pid), reading.Percent, charging, true, entry.Kind)));
                }
                else if (Recall(id) is { } cached)
                {
                    result.Add(cached);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Razer battery read failed: {entry.Name}");
            }
        }
        return result;
    }

    private static BatteryReading? ReadRazer(List<string> paths)
    {
        foreach (var path in paths)
        {
            IntPtr handle = OpenHid(path);
            if (handle == IntPtr.Zero) continue;
            try
            {
                if (GetCaps(handle) is not { FeatureReportByteLength: RazerFeatureLength }) continue;

                var order = s_razerTransaction.TryGetValue(path, out byte known)
                    ? BatteryProtocols.RazerTransactionIds.OrderBy(t => t != known)
                    : BatteryProtocols.RazerTransactionIds.AsEnumerable();
                foreach (byte transaction in order)
                {
                    var level = RazerQuery(handle, transaction, BatteryProtocols.RazerBatteryLevel);
                    if (level is null || level[1] == 0) continue; // 0: asleep, or the wrong transaction id
                    s_razerTransaction[path] = transaction;

                    bool charging = RazerQuery(handle, transaction, BatteryProtocols.RazerChargingStatus) is { } status && status[1] == 1;
                    return new BatteryReading(BatteryProtocols.RazerPercent(level[1]), charging);
                }
            }
            finally
            {
                HidInterop.CloseHandle(handle);
            }
        }
        return null;
    }

    /// <summary>One battery-class request; retries while the device says it is busy.</summary>
    private static byte[]? RazerQuery(IntPtr handle, byte transaction, byte commandId)
    {
        var request = new byte[RazerFeatureLength];
        BatteryProtocols.RazerRequest(transaction, BatteryProtocols.RazerBatteryClass, commandId, 0x02).CopyTo(request, 1);
        if (!HidInterop.HidD_SetFeature(handle, request, request.Length)) return null;

        var response = new byte[RazerFeatureLength];
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Thread.Sleep(attempt == 0 ? 5 : 30);
            Array.Clear(response);
            if (!HidInterop.HidD_GetFeature(handle, response, response.Length)) return null;
            var arguments = BatteryProtocols.ParseRazerResponse(response.AsSpan(1), BatteryProtocols.RazerBatteryClass, commandId, out bool busy);
            if (arguments is not null) return arguments;
            if (!busy) return null;
        }
        return null;
    }
}
