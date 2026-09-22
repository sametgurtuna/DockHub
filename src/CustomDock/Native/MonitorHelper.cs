using System.Runtime.InteropServices;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Native;

public sealed record MonitorInfo(IntPtr Handle, string DeviceName, RECT Bounds, RECT WorkArea, bool IsPrimary)
{
    public int Index { get; init; }

    public string DisplayName =>
        $"Display {Index} — {Bounds.Width}×{Bounds.Height}{(IsPrimary ? " (Primary)" : "")}";

    /// <summary>Monitor DPI scale (1.0 = 100%).</summary>
    public double DpiScale =>
        Handle != IntPtr.Zero && GetDpiForMonitor(Handle, 0 /* MDT_EFFECTIVE_DPI */, out uint dpi, out _) == 0 && dpi > 0
            ? dpi / 96.0
            : 1.0;
}

/// <summary>Monitor information in physical pixels.</summary>
public static class MonitorHelper
{
    public static List<MonitorInfo> GetAll()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr _, ref RECT _, IntPtr _) =>
        {
            if (TryGet(handle) is { } info)
                list.Add(info);
            return true;
        }, IntPtr.Zero);

        // Primary display first, then left to right.
        return list
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Bounds.Left)
            .ThenBy(m => m.Bounds.Top)
            .Select((m, i) => m with { Index = i + 1 })
            .ToList();
    }

    public static MonitorInfo? TryGet(IntPtr handle)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(handle, ref info)) return null;
        return new MonitorInfo(handle, info.szDevice, info.rcMonitor, info.rcWork, (info.dwFlags & MONITORINFOF_PRIMARY) != 0);
    }

    /// <summary>Finds monitor by saved device name; otherwise returns primary display.</summary>
    public static MonitorInfo GetPreferred(string? deviceName)
    {
        var all = GetAll();
        return all.FirstOrDefault(m => deviceName is not null && string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
               ?? all.FirstOrDefault(m => m.IsPrimary)
               ?? all.FirstOrDefault()
               ?? new MonitorInfo(IntPtr.Zero, "", new RECT(0, 0, 1920, 1080), new RECT(0, 0, 1920, 1040), true);
    }
}
