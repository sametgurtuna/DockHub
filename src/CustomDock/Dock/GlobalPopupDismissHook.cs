using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Native;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

/// <summary>
/// Low-level hook ensuring all open ContextMenus and Popups
/// (which normally do not close because Dock is in WS_EX_NOACTIVATE mode)
/// close instantly and smoothly when the desktop or another application is clicked.
/// Active only while at least one menu or popup is open; consumes zero resources otherwise.
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
        popup.AllowsTransparency = true;
        popup.PopupAnimation = PopupAnimation.None;
        popup.StaysOpen = true;
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
                var pt = new POINT { X = x, Y = y };
                IntPtr clickedHwnd = WindowFromPoint(pt);

                bool clickedOurPopupRoot = false;
                if (clickedHwnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(clickedHwnd, out uint clickPid);
                    if (clickPid == (uint)Environment.ProcessId)
                    {
                        var hwndSource = HwndSource.FromHwnd(clickedHwnd);
                        if (hwndSource?.RootVisual is DependencyObject root)
                        {
                            if (root.GetType().Name == "PopupRoot")
                                clickedOurPopupRoot = true;
                        }
                    }
                }

                bool clickedInsidePopup = false;
                foreach (var p in s_activePopups.ToList())
                {
                    if (p.IsOpen && p.Child is FrameworkElement fe)
                    {
                        if (IsPointInVisual(fe, x, y))
                        {
                            clickedInsidePopup = true;
                            break;
                        }
                    }
                }

                bool clickedInsideMenu = false;
                foreach (var m in s_activeMenus.ToList())
                {
                    if (IsPointInMenuOrSubmenus(m, x, y))
                    {
                        clickedInsideMenu = true;
                        break;
                    }
                }

                if (!clickedInsideMenu && clickedOurPopupRoot && s_activeMenus.Count > 0 && clickedHwnd != IntPtr.Zero)
                {
                    var hwndSource = HwndSource.FromHwnd(clickedHwnd);
                    if (hwndSource?.RootVisual is DependencyObject root && FindVisualChildren<MenuItem>(root).Any())
                    {
                        clickedInsideMenu = true;
                    }
                }

                // Dismiss if clicked outside (e.g. desktop or another window)
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

                if (!clickedInsidePopup && s_activePopups.Count > 0 && !clickedOurPopupRoot)
                {
                    var popups = s_activePopups.ToList();
                    Application.Current?.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Input,
                        () =>
                        {
                            var edge = AppServices.Config.Edge;
                            foreach (var p in popups)
                            {
                                if (p.IsOpen && !PopupAnimationHelper.IsClosing(p))
                                    PopupAnimationHelper.ClosePopup(p, edge);
                            }
                        });
                }
            }
        }

        return CallNextHookEx(s_hook, nCode, wParam, lParam);
    }

    private static bool IsPointInVisual(Visual visual, int x, int y)
    {
        var source = PresentationSource.FromVisual(visual) as HwndSource;
        if (source is not null && !source.IsDisposed)
        {
            if (GetWindowRect(source.Handle, out RECT r))
            {
                return x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
            }
        }
        return false;
    }

    private static bool IsPointInMenuOrSubmenus(ContextMenu menu, int x, int y)
    {
        if (!menu.IsOpen) return false;
        if (IsPointInVisual(menu, x, y)) return true;

        return CheckSubmenus(menu, x, y);
    }

    private static bool CheckSubmenus(ItemsControl parent, int x, int y)
    {
        for (int i = 0; i < parent.Items.Count; i++)
        {
            var item = parent.Items[i];
            MenuItem? mi = item as MenuItem;
            if (mi is null && parent.ItemContainerGenerator is { } icg)
                mi = icg.ContainerFromIndex(i) as MenuItem ?? icg.ContainerFromItem(item) as MenuItem;

            if (mi is not null && mi.IsSubmenuOpen)
            {
                foreach (var popup in FindVisualChildren<Popup>(mi))
                {
                    if (popup.IsOpen && popup.Child is Visual child && IsPointInVisual(child, x, y))
                        return true;
                }

                if (CheckSubmenus(mi, x, y))
                    return true;
            }
        }
        return false;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
