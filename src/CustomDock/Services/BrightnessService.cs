using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using CustomDock.Core;
using CustomDock.Native;
using Microsoft.Win32;

namespace CustomDock.Services;

/// <summary>
/// Screen brightness: the built-in panel through WMI (laptops) and external monitors through DDC/CI (most desktop
/// monitors allow it). One level for all screens, 0-100 %. Night light state is read from Windows' settings.
/// All hardware calls run off the UI thread (DDC/CI is slow).
/// </summary>
public sealed class BrightnessService
{
    private const string NightLightKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private RegistryWatcher? _nightLightWatcher;
    private bool _started;
    private int _pending = -1;

    /// <summary>Current level (0-100), or null when no screen allows changing it.</summary>
    public int? Level { get; private set; }

    public bool NightLight { get; private set; }

    public event Action? Changed;

    public void EnsureStarted()
    {
        if (_started) return;
        _started = true;
        NightLight = ReadNightLight();
        _nightLightWatcher = new RegistryWatcher(RegistryHive.CurrentUser, NightLightKey, subtree: true);
        _nightLightWatcher.Changed += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            bool on = ReadNightLight();
            if (on == NightLight) return;
            NightLight = on;
            Changed?.Invoke();
        });
        _nightLightWatcher.Start();
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        int? level = await Task.Run(ReadLevel);
        if (level == Level) return;
        Level = level;
        Changed?.Invoke();
    }

    /// <summary>Sets every screen; rapid changes (wheel, slider) collapse into the latest value.</summary>
    public async Task SetAsync(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        Level = percent;
        Changed?.Invoke();
        Interlocked.Exchange(ref _pending, percent);
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            int value;
            while ((value = Interlocked.Exchange(ref _pending, -1)) >= 0)
            {
                int target = value;
                await Task.Run(() => Apply(target));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool ReadNightLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(NightLightKey);
            // Byte 18 of the stored state is 0x15 while night light is on (0x13 when off).
            return key?.GetValue("Data") is byte[] data && data.Length > 18 && data[18] == 0x15;
        }
        catch
        {
            return false;
        }
    }

    private static int? ReadLevel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject monitor in searcher.Get())
                using (monitor)
                    return Convert.ToInt32(monitor["CurrentBrightness"]);
        }
        catch (ManagementException) { /* no built-in panel */ }
        catch (COMException) { /* WMI unavailable */ }

        foreach (var (handle, min, max) in PhysicalMonitors())
        {
            try
            {
                if (GetMonitorBrightness(handle.Handle, out uint low, out uint current, out uint high) && high > low)
                    return (int)Math.Round((current - low) * 100.0 / (high - low));
            }
            finally
            {
                handle.Dispose();
            }
        }
        return null;
    }

    private static void Apply(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject monitor in searcher.Get())
                using (monitor)
                    monitor.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
        }
        catch (ManagementException) { /* no built-in panel */ }
        catch (COMException) { /* WMI unavailable */ }

        foreach (var (handle, _, _) in PhysicalMonitors())
        {
            try
            {
                if (GetMonitorBrightness(handle.Handle, out uint low, out _, out uint high) && high > low)
                    SetMonitorBrightness(handle.Handle, (uint)Math.Round(low + (high - low) * percent / 100.0));
            }
            catch (Exception ex)
            {
                Log.Debug($"DDC/CI brightness failed: {ex.Message}");
            }
            finally
            {
                handle.Dispose();
            }
        }
    }

    /// <summary>DDC/CI handles of every external monitor (callers dispose them).</summary>
    private static List<(PhysicalMonitor Monitor, uint Min, uint Max)> PhysicalMonitors()
    {
        var list = new List<(PhysicalMonitor, uint, uint)>();
        foreach (var monitor in MonitorHelper.GetAll())
        {
            try
            {
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.Handle, out uint count) || count == 0) continue;
                var handles = new PHYSICAL_MONITOR[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor.Handle, count, handles)) continue;
                foreach (var handle in handles)
                    list.Add((new PhysicalMonitor(handle.hPhysicalMonitor), 0, 100));
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                break;
            }
        }
        return list;
    }

    private sealed class PhysicalMonitor : IDisposable
    {
        public PhysicalMonitor(IntPtr handle) => Handle = handle;

        public IntPtr Handle { get; }

        public void Dispose() => DestroyPhysicalMonitor(Handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitor(IntPtr monitor);
}
