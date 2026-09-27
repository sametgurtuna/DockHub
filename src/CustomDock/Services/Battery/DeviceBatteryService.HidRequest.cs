using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Services;

public sealed partial class DeviceBatteryService
{
    /// <summary>Devices described entirely in the catalog (<see cref="HidRequestSpec"/>): one request, one answer.</summary>
    private static List<BatteryDeviceInfo> ScanHidRequests(IReadOnlyList<HidPath> hidPaths, BatteryDeviceCatalog catalog)
    {
        var result = new List<BatteryDeviceInfo>();
        foreach (var entry in catalog.ForProtocol(BatteryProtocols.HidRequest))
        {
            if (entry.Pid is not { } pid || entry.Request is not { } spec) continue;
            string id = entry.Id ?? $"hid-{entry.Vid:x4}-{pid:x4}";
            if (result.Any(d => d.Id == id)) continue;
            var paths = hidPaths.Where(p => p.Vid == entry.Vid && p.Pid == pid).Select(p => p.Path).ToList();
            if (paths.Count == 0) continue;

            try
            {
                if (paths.Select(path => ReadHidRequest(path, spec)).FirstOrDefault(r => r is not null) is { } reading)
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
                Log.Error(ex, $"Battery request failed: {entry.Name}");
            }
        }
        return result;
    }

    private static BatteryReading? ReadHidRequest(string path, HidRequestSpec spec)
    {
        IntPtr handle = OpenHid(path);
        if (handle == IntPtr.Zero) return null;
        try
        {
            if (GetCaps(handle) is not { } caps) return null;
            if (spec.UsagePage is { } page && caps.UsagePage != page) return null;

            if (spec.Feature)
            {
                int length = caps.FeatureReportByteLength;
                if (length < spec.Request.Length) return null;
                var request = new byte[length];
                spec.Request.CopyTo(request, 0);
                if (!HidInterop.HidD_SetFeature(handle, request, length)) return null;

                Thread.Sleep(20);
                var response = new byte[length];
                response[0] = spec.Request[0];
                return HidInterop.HidD_GetFeature(handle, response, length) ? BatteryProtocols.ParseHidRequest(spec, response) : null;
            }

            if (caps.OutputReportByteLength < spec.Request.Length || caps.InputReportByteLength == 0) return null;
            if (!WriteOutputReport(handle, spec.Request, caps.OutputReportByteLength, 300)) return null;

            var input = new byte[caps.InputReportByteLength];
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (!ReadInputReport(handle, input, 300)) return null;
                if (BatteryProtocols.ParseHidRequest(spec, input) is { } reading) return reading;
            }
            return null;
        }
        finally
        {
            HidInterop.CloseHandle(handle);
        }
    }
}
