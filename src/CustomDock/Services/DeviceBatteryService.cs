using System.Runtime.InteropServices;
using System.Windows;
using CustomDock.Core;
using CustomDock.Native;
using Windows.Devices.Enumeration;

namespace CustomDock.Services;

public sealed record BatteryDeviceInfo(string Id, string Name, int BatteryPercent, bool IsCharging, bool IsWirelessDongle)
{
    public string FormattedPercent => $"{BatteryPercent}%";

    public bool IsHeadset => Name.Contains("cloud", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("headset", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("kulak", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("headphone", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("buds", StringComparison.OrdinalIgnoreCase);

    public bool IsMouse => Name.Contains("mouse", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("fare", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("lamzu", StringComparison.OrdinalIgnoreCase);

    public bool IsKeyboard => Name.Contains("keyboard", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("klavye", StringComparison.OrdinalIgnoreCase);
}

public sealed class DeviceBatteryService
{
    private const string BluetoothProtocolId = "{e0cbf06c-cdb3-4642-a93e-05a9c0ef2567}";
    private const string BatteryPropertyKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    private readonly System.Windows.Threading.DispatcherTimer _timer;

    public DeviceBatteryService()
    {
        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        // Initial scan
        Refresh();
    }

    public IReadOnlyList<BatteryDeviceInfo> Devices { get; private set; } = Array.Empty<BatteryDeviceInfo>();

    public BatteryDeviceInfo? PrimaryDevice => Devices.FirstOrDefault();

    public event EventHandler? Updated;

    private bool _isRefreshing;

    public Task RefreshAsync() => Task.Run(async () =>
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            var list = new List<BatteryDeviceInfo>();

            // 1. HyperX Cloud II Wireless and 2.4 GHz USB Dongle scan
            var hidPaths = EnumerateHidPaths();
            list.AddRange(ScanUsbDongles(hidPaths));

            // 2. Compx-based mice (LAMZU Atlantis Mini) over their vendor HID collections
            list.AddRange(ScanCompxMice(hidPaths));

            // 3. Windows Bluetooth connected devices scan
            var btDevices = await ScanBluetoothDevicesAsync();
            list.AddRange(btDevices);

            Devices = list;
            Application.Current?.Dispatcher.BeginInvoke(() => Updated?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to scan device batteries");
        }
        finally
        {
            _isRefreshing = false;
        }
    });

    public void Refresh() => _ = RefreshAsync();

    private static async Task<List<BatteryDeviceInfo>> ScanBluetoothDevicesAsync()
    {
        var result = new List<BatteryDeviceInfo>();
        try
        {
            string aqs = $"System.Devices.Aep.ProtocolId:=\"{BluetoothProtocolId}\" AND System.Devices.Aep.IsConnected:=System.StructuredQueryType.Boolean#True";
            var properties = new[] { "System.ItemNameDisplay", "System.Devices.Aep.IsConnected", BatteryPropertyKey };
            var collection = await DeviceInformation.FindAllAsync(aqs, properties);

            foreach (var dev in collection)
            {
                if (dev.Properties.TryGetValue(BatteryPropertyKey, out var val) && val is not null)
                {
                    int percent = Convert.ToInt32(val);
                    if (percent >= 0 && percent <= 100)
                    {
                        string name = string.IsNullOrWhiteSpace(dev.Name) ? "Bluetooth Device" : dev.Name;
                        result.Add(new BatteryDeviceInfo(dev.Id, name, percent, false, false));
                    }
                }
            }
        }
        catch { }
        return result;
    }

    /// <summary>Device interface paths of every HID collection currently present.</summary>
    private static List<string> EnumerateHidPaths()
    {
        var result = new List<string>();

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
                            if (!string.IsNullOrEmpty(devicePath)) result.Add(devicePath);
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

    /// <summary>Scans HyperX Cloud II Wireless and similar 2.4GHz RF USB Dongle devices via HID.</summary>
    private static List<BatteryDeviceInfo> ScanUsbDongles(IEnumerable<string> hidPaths)
    {
        var result = new List<BatteryDeviceInfo>();
        foreach (var devicePath in hidPaths)
        {
            var dev = CheckHyperXDevice(devicePath);
            if (dev is not null && !result.Any(d => d.Id == dev.Id))
                result.Add(dev);
        }
        return result;
    }

    // ------------------------------------------------------------------ Compx-based mice (LAMZU)

    /// <summary>Mice built on the Compx wireless chipset. Wired entries mean the mouse is plugged in (charging).</summary>
    private static readonly (ushort Vid, ushort Pid, string Id, string Name, bool Wired)[] s_compxMice =
    {
        (0x25A7, 0xFA7B, "lamzu-atlantis-mini", "LAMZU Atlantis Mini", true),
        (0x25A7, 0xFA7C, "lamzu-atlantis-mini", "LAMZU Atlantis Mini", false),
    };

    private const byte CompxCommandReport = 0x08;
    private const byte CompxResponseReport = 0x09;
    private const byte CompxBatteryCommand = 0x04;
    private const int CompxReportLength = 17;

    private static readonly Dictionary<string, (int Battery, bool IsCharging)> s_lastKnownCompx = new();

    /// <summary>
    /// Compx protocol: a 17-byte feature report (ID 0x08, byte 1 = command, last byte = 0x55 minus the sum of the
    /// others) is answered on the vendor input collection with report 0x09 echoing the command. For the battery
    /// query (0x04) byte 6 is the level in percent and byte 7 the charging flag.
    /// </summary>
    private static List<BatteryDeviceInfo> ScanCompxMice(IReadOnlyList<string> hidPaths)
    {
        var result = new List<BatteryDeviceInfo>();
        foreach (var mouse in s_compxMice)
        {
            if (result.Any(d => d.Id == mouse.Id)) continue;
            string tag = $"vid_{mouse.Vid:x4}&pid_{mouse.Pid:x4}";
            var paths = hidPaths.Where(p => p.Contains(tag, StringComparison.OrdinalIgnoreCase)).ToList();
            if (paths.Count == 0) continue;

            try
            {
                if (QueryCompxBattery(paths) is { Battery: > 0 and <= 100 } reading)
                {
                    bool charging = mouse.Wired || reading.IsCharging;
                    s_lastKnownCompx[mouse.Id] = (reading.Battery, charging);
                    result.Add(new BatteryDeviceInfo(mouse.Id, mouse.Name, reading.Battery, charging, true));
                }
                else if (s_lastKnownCompx.TryGetValue(mouse.Id, out var cached))
                {
                    result.Add(new BatteryDeviceInfo(mouse.Id, mouse.Name, cached.Battery, cached.IsCharging, true));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Compx battery query failed: {mouse.Name}");
            }
        }
        return result;
    }

    private static (int Battery, bool IsCharging)? QueryCompxBattery(List<string> paths)
    {
        IntPtr featureHandle = IntPtr.Zero, inputHandle = IntPtr.Zero;
        try
        {
            foreach (var path in paths)
            {
                IntPtr handle = HidInterop.CreateFile(path,
                    HidInterop.GENERIC_READ | HidInterop.GENERIC_WRITE,
                    HidInterop.FILE_SHARE_READ | HidInterop.FILE_SHARE_WRITE,
                    IntPtr.Zero, HidInterop.OPEN_EXISTING, HidInterop.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) continue;

                var caps = GetCaps(handle);
                bool vendor = caps is { UsagePage: >= 0xFF00 };
                if (vendor && featureHandle == IntPtr.Zero && caps!.Value.FeatureReportByteLength == CompxReportLength)
                    featureHandle = handle;
                else if (vendor && inputHandle == IntPtr.Zero && caps!.Value.InputReportByteLength == CompxReportLength)
                    inputHandle = handle;
                else
                    HidInterop.CloseHandle(handle);
            }
            if (featureHandle == IntPtr.Zero || inputHandle == IntPtr.Zero) return null;

            // The input handle is open before the command goes out, so the answer lands in its report queue.
            var command = new byte[CompxReportLength];
            command[0] = CompxCommandReport;
            command[1] = CompxBatteryCommand;
            int sum = 0;
            for (int i = 0; i < command.Length - 1; i++) sum += command[i];
            command[^1] = (byte)(0x55 - sum);
            if (!HidInterop.HidD_SetFeature(featureHandle, command, command.Length)) return null;

            var response = new byte[CompxReportLength];
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (!ReadInputReport(inputHandle, response, 300)) return null;
                if (response[0] == CompxResponseReport && response[1] == CompxBatteryCommand)
                    return (response[6], response[7] != 0);
            }
            return null;
        }
        finally
        {
            if (featureHandle != IntPtr.Zero) HidInterop.CloseHandle(featureHandle);
            if (inputHandle != IntPtr.Zero) HidInterop.CloseHandle(inputHandle);
        }
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

    private static readonly Dictionary<ushort, (int Battery, bool IsCharging)> s_lastKnownBattery = new();

    private static BatteryDeviceInfo? CheckHyperXDevice(string devicePath)
    {
        string pathLower = devicePath.ToLowerInvariant();
        if (pathLower.Contains("col01") || pathLower.Contains("col02")) return null;

        IntPtr handle = HidInterop.CreateFile(devicePath,
            HidInterop.GENERIC_READ | HidInterop.GENERIC_WRITE,
            HidInterop.FILE_SHARE_READ | HidInterop.FILE_SHARE_WRITE,
            IntPtr.Zero,
            HidInterop.OPEN_EXISTING,
            HidInterop.FILE_FLAG_OVERLAPPED,
            IntPtr.Zero);

        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;

        try
        {
            var attrs = new HidInterop.HIDD_ATTRIBUTES();
            attrs.Size = Marshal.SizeOf(attrs);
            if (!HidInterop.HidD_GetAttributes(handle, ref attrs)) return null;

            ushort vid = attrs.VendorID;
            ushort pid = attrs.ProductID;

            bool isHyperX = vid == 0x0951 || vid == 0x03F0;
            if (!isHyperX) return null;

            string devName = pid switch
            {
                0x1718 => "HyperX Cloud II Wireless",
                0x018B => "HyperX Cloud II Wireless",
                0x017B => "HyperX Cloud II Wireless",
                0x0b92 or 0x16EA or 0x16EB or 0x0D93 or 0x0696 => "HyperX Cloud II Wireless",
                0x0186 => "HyperX Cloud Core Wireless",
                0x0188 => "HyperX Cloud Alpha Wireless",
                0x0914 or 0x0185 => "HyperX Cloud Flight",
                _ => $"HyperX Headset ({pid:X4})",
            };

            int battery = -1;
            bool isCharging = false;

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

                    if (bytesRead > 0 && rawBuf[0] == 0x0B && rawBuf[2] == 0xBB && rawBuf[3] == 0x02)
                    {
                        int level = rawBuf[7];
                        if (level >= 0 && level <= 100)
                        {
                            battery = level;
                            isCharging = rawBuf[4] == 1 || rawBuf[4] == 2;
                            Log.Debug($"HyperX battery read: {devName} -> {battery}% (Charging: {isCharging})");
                            break;
                        }
                    }
                }
            }
            finally
            {
                pin.Free();
                HidInterop.CloseHandle(readEv);
            }

            // Strategy 2: Report ID 0x21 (For models like Cloud Flight / Alpha supporting Feature Report)
            if (battery < 0)
            {
                byte[] report = new byte[32];
                report[0] = 0x21;
                report[1] = 0xbb;
                report[2] = 0x0b;

                HidInterop.HidD_SetFeature(handle, report, report.Length);
                byte[] response = new byte[32];
                response[0] = 0x21;
                if (HidInterop.HidD_GetFeature(handle, response, response.Length))
                {
                    for (int offset = 2; offset < Math.Min(20, response.Length); offset++)
                    {
                        if (response[offset] > 0 && response[offset] <= 100)
                        {
                            battery = response[offset];
                            if (offset + 1 < response.Length)
                                isCharging = response[offset + 1] == 1 || response[offset + 1] == 2;
                            break;
                        }
                    }
                }
            }

            if (battery >= 0 && battery <= 100)
            {
                s_lastKnownBattery[pid] = (battery, isCharging);
                return new BatteryDeviceInfo($"hyperx-{pid:X4}", devName, battery, isCharging, true);
            }
            else if (s_lastKnownBattery.TryGetValue(pid, out var cached))
            {
                return new BatteryDeviceInfo($"hyperx-{pid:X4}", devName, cached.Battery, cached.IsCharging, true);
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"CheckHyperXDevice error: {devicePath}");
            return null;
        }
        finally
        {
            HidInterop.CloseHandle(handle);
        }
    }
}
