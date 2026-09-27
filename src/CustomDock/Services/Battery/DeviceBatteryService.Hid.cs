using System.Runtime.InteropServices;
using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Services;

public sealed partial class DeviceBatteryService
{
    /// <summary>A HID collection's device path with the vendor and product id read from it.</summary>
    private readonly record struct HidPath(string Path, ushort Vid, ushort Pid);

    /// <summary>Every HID collection currently present whose path carries a vendor and product id.</summary>
    private static List<HidPath> EnumerateHidPaths()
    {
        var result = new List<HidPath>();

        try
        {
            HidInterop.HidD_GetHidGuid(out var hidGuid);
            IntPtr devInfo = HidInterop.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, HidInterop.DIGCF_PRESENT | HidInterop.DIGCF_DEVICEINTERFACE);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return result;

            try
            {
                var ifData = new HidInterop.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint index = 0;

                while (HidInterop.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, index++, ref ifData))
                {
                    int reqSize = 0;
                    HidInterop.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, ref reqSize, IntPtr.Zero);
                    if (reqSize <= 0) continue;

                    IntPtr detailData = Marshal.AllocHGlobal(reqSize);
                    try
                    {
                        // In x64, SP_DEVICE_INTERFACE_DETAIL_DATA cbSize = 8 (5 or 6 in x86)
                        Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
                        if (HidInterop.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailData, reqSize, ref reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailData.ToInt64() + 4);
                            string? devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (!string.IsNullOrEmpty(devicePath) && BatteryProtocols.ParseVidPid(devicePath) is { } ids)
                                result.Add(new HidPath(devicePath, ids.Vid, ids.Pid));
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailData);
                    }
                }
            }
            finally
            {
                HidInterop.SetupDiDestroyDeviceInfoList(devInfo);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during HID enumeration");
        }

        return result;
    }

    private static IntPtr OpenHid(string path)
    {
        IntPtr handle = HidInterop.CreateFile(path,
            HidInterop.GENERIC_READ | HidInterop.GENERIC_WRITE,
            HidInterop.FILE_SHARE_READ | HidInterop.FILE_SHARE_WRITE,
            IntPtr.Zero, HidInterop.OPEN_EXISTING, HidInterop.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        return handle == new IntPtr(-1) ? IntPtr.Zero : handle;
    }

    private static HidInterop.HIDP_CAPS? GetCaps(IntPtr handle)
    {
        if (!HidInterop.HidD_GetPreparsedData(handle, out var preparsed)) return null;
        try
        {
            return HidInterop.HidP_GetCaps(preparsed, out var caps) == HidInterop.HIDP_STATUS_SUCCESS ? caps : null;
        }
        finally
        {
            HidInterop.HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>Reads one input report from an overlapped HID handle; false on timeout or error.</summary>
    private static bool ReadInputReport(IntPtr handle, byte[] buffer, uint timeoutMs)
    {
        IntPtr native = Marshal.AllocHGlobal(buffer.Length);
        IntPtr readEvent = HidInterop.CreateEvent(IntPtr.Zero, true, false, null);
        try
        {
            var overlapped = new HidInterop.OVERLAPPED { hEvent = readEvent };
            bool ok = HidInterop.ReadFile(handle, native, (uint)buffer.Length, out uint bytesRead, ref overlapped);
            if (!ok)
            {
                if (Marshal.GetLastWin32Error() != 997) return false; // ERROR_IO_PENDING
                if (HidInterop.WaitForSingleObject(readEvent, timeoutMs) != 0)
                {
                    HidInterop.CancelIoEx(handle, ref overlapped);
                    HidInterop.GetOverlappedResult(handle, ref overlapped, out _, true);
                    return false;
                }
                if (!HidInterop.GetOverlappedResult(handle, ref overlapped, out bytesRead, false)) return false;
            }
            Marshal.Copy(native, buffer, 0, (int)Math.Min(bytesRead, (uint)buffer.Length));
            return bytesRead > 0;
        }
        finally
        {
            HidInterop.CloseHandle(readEvent);
            Marshal.FreeHGlobal(native);
        }
    }

    // ------------------------------------------------------------------ Compx-based mice (LAMZU)

    private static readonly Dictionary<string, BatteryReading> s_lastKnownCompx = new();

    /// <summary>
    /// Compx protocol: a 17-byte feature report (see <see cref="BatteryProtocols.CompxCommand"/>) is answered on the
    /// vendor input collection with report 0x09 echoing the command.
    /// </summary>
    private static List<BatteryDeviceInfo> ScanCompxMice(IReadOnlyList<HidPath> hidPaths, BatteryDeviceCatalog catalog)
    {
        var result = new List<BatteryDeviceInfo>();
        foreach (var mouse in catalog.ForProtocol(BatteryProtocols.Compx))
        {
            if (mouse.Pid is not { } pid) continue; // the protocol is only spoken by known products
            string id = mouse.Id ?? $"compx-{mouse.Vid:x4}-{pid:x4}";
            if (result.Any(d => d.Id == id)) continue;
            var paths = hidPaths.Where(p => p.Vid == mouse.Vid && p.Pid == pid).Select(p => p.Path).ToList();
            if (paths.Count == 0) continue;

            try
            {
                string name = mouse.DisplayName(pid);
                if (QueryCompxBattery(paths) is { } reading)
                {
                    var stored = reading with { IsCharging = mouse.Wired || reading.IsCharging };
                    s_lastKnownCompx[id] = stored;
                    result.Add(new BatteryDeviceInfo(id, name, stored.Percent, stored.IsCharging, true, mouse.Kind));
                }
                else if (s_lastKnownCompx.TryGetValue(id, out var cached))
                {
                    result.Add(new BatteryDeviceInfo(id, name, cached.Percent, cached.IsCharging, true, mouse.Kind));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Compx battery query failed: {mouse.Name}");
            }
        }
        return result;
    }

    private static BatteryReading? QueryCompxBattery(List<string> paths)
    {
        IntPtr featureHandle = IntPtr.Zero, inputHandle = IntPtr.Zero;
        try
        {
            foreach (var path in paths)
            {
                IntPtr handle = OpenHid(path);
                if (handle == IntPtr.Zero) continue;

                var caps = GetCaps(handle);
                bool vendor = caps is { UsagePage: >= 0xFF00 };
                if (vendor && featureHandle == IntPtr.Zero && caps!.Value.FeatureReportByteLength == BatteryProtocols.CompxReportLength)
                    featureHandle = handle;
                else if (vendor && inputHandle == IntPtr.Zero && caps!.Value.InputReportByteLength == BatteryProtocols.CompxReportLength)
                    inputHandle = handle;
                else
                    HidInterop.CloseHandle(handle);
            }
            if (featureHandle == IntPtr.Zero || inputHandle == IntPtr.Zero) return null;

            // The input handle is open before the command goes out, so the answer lands in its report queue.
            var command = BatteryProtocols.CompxCommand(BatteryProtocols.CompxBatteryCommand);
            if (!HidInterop.HidD_SetFeature(featureHandle, command, command.Length)) return null;

            var response = new byte[BatteryProtocols.CompxReportLength];
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (!ReadInputReport(inputHandle, response, 300)) return null;
                if (BatteryProtocols.IsCompxBatteryResponse(response))
                    return BatteryProtocols.ParseCompxBattery(response);
            }
            return null;
        }
        finally
        {
            if (featureHandle != IntPtr.Zero) HidInterop.CloseHandle(featureHandle);
            if (inputHandle != IntPtr.Zero) HidInterop.CloseHandle(inputHandle);
        }
    }

    // ------------------------------------------------------------------ HyperX headsets

    private static readonly Dictionary<ushort, BatteryReading> s_lastKnownHyperX = new();

    /// <summary>HyperX Cloud II Wireless and similar 2.4 GHz dongles from the catalog.</summary>
    private static List<BatteryDeviceInfo> ScanHyperX(IEnumerable<HidPath> hidPaths, BatteryDeviceCatalog catalog)
    {
        var result = new List<BatteryDeviceInfo>();
        foreach (var hid in hidPaths)
        {
            if (catalog.Find(BatteryProtocols.HyperX, hid.Vid, hid.Pid) is not { } entry) continue;
            string pathLower = hid.Path.ToLowerInvariant();
            if (pathLower.Contains("col01") || pathLower.Contains("col02")) continue;
            if (result.Any(d => d.Id == HyperXId(hid.Pid))) continue;

            if (ReadHyperX(hid.Path, hid.Pid, entry) is { } dev)
                result.Add(dev);
        }
        return result;
    }

    private static string HyperXId(ushort pid) => $"hyperx-{pid:X4}";

    private static BatteryDeviceInfo? ReadHyperX(string devicePath, ushort pid, BatteryCatalogEntry entry)
    {
        IntPtr handle = OpenHid(devicePath);
        if (handle == IntPtr.Zero) return null;

        string devName = entry.DisplayName(pid);
        try
        {
            BatteryReading? reading = null;

            // Strategy 1: Cloud II Wireless hardware handshake and Overlapped ReadFile for real battery reading
            HidInterop.HidD_SetNumInputBuffers(handle, 64);
            byte[] rep6 = new byte[62];
            rep6[0] = 6;
            HidInterop.HidD_GetInputReport(handle, rep6, 62);

            byte[] rawBuf = new byte[128];
            GCHandle pin = GCHandle.Alloc(rawBuf, GCHandleType.Pinned);
            IntPtr pBuf = pin.AddrOfPinnedObject();

            IntPtr readEv = HidInterop.CreateEvent(IntPtr.Zero, true, false, null);
            try
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    HidInterop.ResetEvent(readEv);
                    var readOl = new HidInterop.OVERLAPPED { hEvent = readEv };

                    bool rOk = HidInterop.ReadFile(handle, pBuf, 62, out uint bytesRead, ref readOl);
                    int rErr = Marshal.GetLastWin32Error();

                    if (!rOk && rErr == 997) // ERROR_IO_PENDING
                    {
                        uint waitRes = HidInterop.WaitForSingleObject(readEv, 400);
                        if (waitRes == 0) // WAIT_OBJECT_0
                        {
                            HidInterop.GetOverlappedResult(handle, ref readOl, out bytesRead, false);
                        }
                        else
                        {
                            // Timeout: cancel pending I/O and wait for completion
                            HidInterop.CancelIoEx(handle, ref readOl);
                            HidInterop.GetOverlappedResult(handle, ref readOl, out _, true);
                            continue;
                        }
                    }
                    else if (rOk && bytesRead == 0)
                    {
                        bytesRead = 62;
                    }

                    if (bytesRead > 0 && BatteryProtocols.ParseHyperXInput(rawBuf) is { } level)
                    {
                        reading = level;
                        Log.Debug($"HyperX battery read: {devName} -> {level.Percent}% (Charging: {level.IsCharging})");
                        break;
                    }
                }
            }
            finally
            {
                pin.Free();
                HidInterop.CloseHandle(readEv);
            }

            // Strategy 2: Report ID 0x21 (For models like Cloud Flight / Alpha supporting Feature Report)
            if (reading is null)
            {
                var request = BatteryProtocols.HyperXFeatureRequest();
                HidInterop.HidD_SetFeature(handle, request, request.Length);
                byte[] response = new byte[32];
                response[0] = 0x21;
                if (HidInterop.HidD_GetFeature(handle, response, response.Length))
                    reading = BatteryProtocols.ParseHyperXFeature(response);
            }

            if (reading is { } read)
            {
                s_lastKnownHyperX[pid] = read;
                return new BatteryDeviceInfo(HyperXId(pid), devName, read.Percent, read.IsCharging, true, entry.Kind);
            }
            if (s_lastKnownHyperX.TryGetValue(pid, out var cached))
                return new BatteryDeviceInfo(HyperXId(pid), devName, cached.Percent, cached.IsCharging, true, entry.Kind);

            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"HyperX battery read failed: {devicePath}");
            return null;
        }
        finally
        {
            HidInterop.CloseHandle(handle);
        }
    }
}
