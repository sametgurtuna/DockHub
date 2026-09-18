using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using CustomDock.Native;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

/// <summary>
/// Masaüstüne veya başka bir uygulamaya tıklandığında açık olan tüm ContextMenu
/// ve Popup'ların (Dock WS_EX_NOACTIVATE kipinde olduğu için normalde kapanmayan pencereler)
/// anında ve pürüzsüzce kapanmasını sağlayan düşük seviyeli kanca.
/// Yalnızca en az bir menü veya popup açıkken etkindir, diğer zamanlarda sıfır kaynak tüketir.
/// </summary>
public static class GlobalPopupDismissHook
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCRBUTTONDOWN = 0x00A4;
    private const int WM_NCMBUTTONDOWN = 0x00A7;

    private static IntPtr s_hook = IntPtr.Zero;
    private static HookProc? s_proc;
    private static readonly HashSet<Popup> s_activePopups = new();
    private static readonly HashSet<ContextMenu> s_activeMenus = new();

    public static bool HasActivePopupsOrMenus => s_activePopups.Count > 0 || s_activeMenus.Count > 0;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct HOOKPOINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public HOOKPOINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public static void RegisterPopup(Popup popup)
    {
        if (popup is null) return;
        if (s_activePopups.Add(popup))
        {
            popup.Closed += OnPopupClosed;
            EnsureHook();
        }
    }

    public static void UnregisterPopup(Popup popup)
    {
        if (popup is null) return;
        popup.Closed -= OnPopupClosed;
        if (s_activePopups.Remove(popup))
        {
            CheckUnhook();
        }
    }

    public static void RegisterMenu(ContextMenu menu)
    {
        if (menu is null) return;
        if (s_activeMenus.Add(menu))
        {
            menu.Closed += OnMenuClosed;
            EnsureHook();
        }
    }

    public static void UnregisterMenu(ContextMenu menu)
    {
        if (menu is null) return;
        menu.Closed -= OnMenuClosed;
        if (s_activeMenus.Remove(menu))
        {
            CheckUnhook();
        }
    }

    private static void OnPopupClosed(object? sender, EventArgs e)
    {
        if (sender is Popup p)
        {
            p.Closed -= OnPopupClosed;
            s_activePopups.Remove(p);
            CheckUnhook();
        }
    }

    private static void OnMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu m)
        {
            m.Closed -= OnMenuClosed;
            s_activeMenus.Remove(m);
            CheckUnhook();
        }
    }

    private static void EnsureHook()
    {
        if (s_hook != IntPtr.Zero) return;
        s_proc = HookCallback;
        IntPtr hMod = GetModuleHandle(null);
        s_hook = SetWindowsHookEx(WH_MOUSE_LL, s_proc, hMod, 0);
        if (s_hook == IntPtr.Zero)
        {
            s_hook = SetWindowsHookEx(WH_MOUSE_LL, s_proc, IntPtr.Zero, 0);
        }
    }

    private static void CheckUnhook()
    {
        if (s_activePopups.Count == 0 && s_activeMenus.Count == 0 && s_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(s_hook);
            s_hook = IntPtr.Zero;
            s_proc = null;
        }
    }


    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN
                    or WM_NCLBUTTONDOWN or WM_NCRBUTTONDOWN or WM_NCMBUTTONDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int x = hookStruct.pt.x;
                int y = hookStruct.pt.y;

                bool clickedInsidePopup = false;
                foreach (var p in s_activePopups.ToList())
                {
                    if (p.IsOpen && p.Child is FrameworkElement fe)
                    {
                        var source = PresentationSource.FromVisual(fe) as HwndSource;
                        if (source is not null && !source.IsDisposed)
                        {
                            if (GetWindowRect(source.Handle, out RECT r))
                            {
                                if (x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom)
                                {
                                    clickedInsidePopup = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                bool clickedInsideMenu = false;
                foreach (var m in s_activeMenus.ToList())
                {
                    if (m.IsOpen)
                    {
                        var source = PresentationSource.FromVisual(m) as HwndSource;
                        if (source is not null && !source.IsDisposed)
                        {
                            if (GetWindowRect(source.Handle, out RECT r))
                            {
                                if (x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom)
                                {
                                    clickedInsideMenu = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                // Dışarıya (örneğin masaüstüne veya başka bir pencereye) tıklandıysa kapat
                if (!clickedInsideMenu && s_activeMenus.Count > 0)
                {
                    var menus = s_activeMenus.ToList();
                    Application.Current?.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Input,
                        () =>
                        {
                            foreach (var m in menus)
                                if (m.IsOpen) m.IsOpen = false;
                        });
                }

                if (!clickedInsidePopup && s_activePopups.Count > 0)
                {
                    var popups = s_activePopups.ToList();
                    Application.Current?.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Input,
                        () =>
                        {
                            foreach (var p in popups)
                                if (p.IsOpen) p.IsOpen = false;
                        });
                }
            }
        }

        return CallNextHookEx(s_hook, nCode, wParam, lParam);
    }
}
