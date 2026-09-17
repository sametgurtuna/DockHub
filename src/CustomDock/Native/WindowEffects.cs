using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CustomDock.Core;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Native;

/// <summary>
/// Pencere efektleri.
/// Not: Windows 11'in DWM "system backdrop" (Mica/Acrylic) efekti yalnızca etkin pencerede çalışır;
/// dock asla etkin pencere olmadığından dock için SetWindowCompositionAttribute tabanlı blur kullanılır.
/// Bu blur pencere bölgesini (region) yok sayar ama DWM köşe yuvarlamasına uyar.
/// </summary>
public static class WindowEffects
{
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    private static readonly int Build = Environment.OSVersion.Version.Build;

    public static bool IsWindows11 => Build >= 22000;

    public static bool SupportsSystemBackdrop => Build >= 22621;

    public static void SetDarkMode(IntPtr hwnd, bool dark)
    {
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    /// <summary>0: varsayılan, 1: yuvarlama yok, 2: yuvarlak, 3: küçük yuvarlak.</summary>
    public static void SetCornerPreference(IntPtr hwnd, int preference)
    {
        if (!IsWindows11) return;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    /// <summary>DWM kenarlık rengi. null = kenarlık yok.</summary>
    public static void SetBorderColor(IntPtr hwnd, Color? color)
    {
        if (!IsWindows11) return;
        uint value = color is { } c ? (uint)(c.R | (c.G << 8) | (c.B << 16)) : 0xFFFFFFFE;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref value, sizeof(uint));
    }

    /// <summary>Katmansız pencerenin istemci alanını cama çevirir (WPF arka planı saydam çizilir).</summary>
    public static void ExtendGlass(IntPtr hwnd)
    {
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            target.BackgroundColor = Colors.Transparent;
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    /// <summary>Dock arka planı: bulanık cam / acrylic / düz.</summary>
    public static void ApplyDockBackdrop(IntPtr hwnd, BackdropKind kind, Color tint)
    {
        switch (kind)
        {
            case BackdropKind.Acrylic:
                // AABBGGRR
                uint abgr = ((uint)Math.Max(tint.A, (byte)0x30) << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R;
                SetAccent(hwnd, ACCENT_ENABLE_ACRYLICBLURBEHIND, abgr);
                break;
            case BackdropKind.Blur:
                SetAccent(hwnd, ACCENT_ENABLE_BLURBEHIND, 0);
                break;
            default:
                SetAccent(hwnd, ACCENT_DISABLED, 0);
                break;
        }
    }

    /// <summary>Etkin pencereler için Windows 11 Mica (ayarlar penceresi).</summary>
    public static bool TryApplyMica(IntPtr hwnd)
    {
        if (!SupportsSystemBackdrop) return false;
        int type = 2; // DWMSBT_MAINWINDOW
        return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) == 0;
    }

    private static void SetAccent(IntPtr hwnd, int accentState, uint gradientColor)
    {
        var accent = new AccentPolicy { AccentState = accentState, AccentFlags = 0, GradientColor = gradientColor };
        int size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = 19 /* WCA_ACCENT_POLICY */, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>Pencereyi Alt+Tab ve görev listesinden gizler; isteğe bağlı olarak tıklamayla etkinleşmez.</summary>
    public static void MakeToolWindow(IntPtr hwnd, bool noActivate = false, bool clickThrough = false)
    {
        long ex = GetExStyle(hwnd);
        ex = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        if (noActivate) ex |= WS_EX_NOACTIVATE;
        if (clickThrough) ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        SetExStyle(hwnd, ex);
    }

    public static void SetNoActivate(IntPtr hwnd, bool noActivate)
    {
        long ex = GetExStyle(hwnd);
        ex = noActivate ? ex | WS_EX_NOACTIVATE : ex & ~WS_EX_NOACTIVATE;
        SetExStyle(hwnd, ex);
    }
}
