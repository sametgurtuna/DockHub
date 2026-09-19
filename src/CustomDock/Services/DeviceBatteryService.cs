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
        || Name.Contains("fare", StringComparison.OrdinalIgnoreCase);

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
            var dongleDevices = ScanUsbDongles();
            list.AddRange(dongleDevices);

            // 2. Windows Bluetooth connected devices scan
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

    /// <summary>Scans HyperX Cloud II Wireless and similar 2.4GHz RF USB Dongle devices via HID.</summary>
    private static List<BatteryDeviceInfo> ScanUsbDongles()
    {
        var result = new List<BatteryDeviceInfo>();

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
                            if (string.IsNullOrEmpty(devicePath)) continue;

                            var dev = CheckHyperXDevice(devicePath);
                            if (dev is not null && !result.Any(d => d.Id == dev.Id))
                            {
                                result.Add(dev);
                            }
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
            Log.Error(ex, "Error during USB Dongle HID scan");
        }

        return result;
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
                            Log.Info($"HyperX battery read: {devName} -> {battery}% (Charging: {isCharging})");
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
