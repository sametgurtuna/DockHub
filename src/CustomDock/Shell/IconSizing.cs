using Microsoft.Win32;
using CustomDock.Native;

namespace CustomDock.Shell;

/// <summary>
/// Pixel size to ask Windows for when it loads an app icon. The icon is shown at 30 DIP, scaled up with the large
/// dock size and the hover magnification, so on a 250% display a fixed 96 px request was stretched and looked soft.
/// </summary>
public static class IconSizing
{
    private const double IconDip = 30;
    private const double LargestDockScale = 54.0 / 46.0;
    private const double HoverBoost = 1.16;
    private static readonly int[] Steps = { 96, 128, 192, 256 };

    private static int? s_pixels;

    static IconSizing()
    {
        SystemEvents.DisplaySettingsChanged += (_, _) => s_pixels = null;
    }

    /// <summary>Smallest standard size that covers the sharpest display without upscaling (never below 96).</summary>
    public static int Pixels => s_pixels ??= For(HighestDpiScale());

    internal static int For(double dpiScale)
    {
        double needed = IconDip * LargestDockScale * HoverBoost * Math.Max(dpiScale, 1);
        foreach (var step in Steps)
            if (step >= needed) return step;
        return Steps[^1];
    }

    private static double HighestDpiScale()
    {
        try
        {
            return MonitorHelper.GetAll().Select(m => m.DpiScale).DefaultIfEmpty(1).Max();
        }
        catch
        {
            return 1;
        }
    }
}
