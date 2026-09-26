using System.Windows.Threading;
using CustomDock.Native;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

/// <summary>
/// Smart auto-hide ("Only hide when a window overlaps"): the dock stays up over the desktop and small windows and
/// slides away only while the active window covers its area. Driven by foreground and move/resize events of the
/// active window, not polling.
/// </summary>
public partial class DockWindow
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    private static readonly HashSet<string> DesktopClasses = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

    private WinEventProc? _smartHideProc;
    private readonly List<IntPtr> _smartHideHooks = new();
    private DispatcherTimer? _smartHideDebounce;

    private bool SmartHideActive => _config.AutoHide && _config.SmartAutoHide;

    /// <summary>Starts or stops listening for window changes when the setting changes (called from ApplySettings).</summary>
    private void UpdateSmartHide()
    {
        if (SmartHideActive && _smartHideHooks.Count == 0 && !_closing)
        {
            _smartHideProc ??= OnSmartHideEvent;
            _smartHideDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, (_, _) =>
            {
                _smartHideDebounce!.Stop();
                EvaluateSmartHide();
            }, Dispatcher);
            _smartHideDebounce.Stop();
            foreach (var (min, max) in new[] { (EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND), (EVENT_SYSTEM_MINIMIZEEND, EVENT_SYSTEM_MINIMIZEEND), (EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE) })
            {
                var hook = SetWinEventHook(min, max, IntPtr.Zero, _smartHideProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                if (hook != IntPtr.Zero) _smartHideHooks.Add(hook);
            }
            EvaluateSmartHide();
        }
        else if (!SmartHideActive && _smartHideHooks.Count > 0)
        {
            StopSmartHide();
        }
    }

    private void StopSmartHide()
    {
        foreach (var hook in _smartHideHooks) UnhookWinEvent(hook);
        _smartHideHooks.Clear();
        _smartHideDebounce?.Stop();
    }

    private void OnSmartHideEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || idChild != 0) return;
        // Location changes of other windows (and the cursor) are frequent; only the active window matters.
        if (eventType == EVENT_OBJECT_LOCATIONCHANGE && hwnd != GetForegroundWindow()) return;
        _smartHideDebounce?.Stop();
        _smartHideDebounce?.Start();
    }

    private void EvaluateSmartHide()
    {
        if (!SmartHideActive || _closing || _interactionCount > 0) return;
        if (ActiveWindowOverlapsDock())
        {
            if (_revealed && !IsCursorOverDock()) TryAutoHide();
        }
        else if (!_revealed)
        {
            Reveal();
        }
    }

    /// <summary>True when the foreground window (not the desktop or a shell surface) intersects the dock's area.</summary>
    private bool ActiveWindowOverlapsDock()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || hwnd == _hwnd || !IsWindowVisible(hwnd) || IsIconic(hwnd) || IsCloaked(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _processId) return false;
        if (DesktopClasses.Contains(GetClassName(hwnd))) return false;

        RECT bounds;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out bounds, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) != 0 &&
            !GetWindowRect(hwnd, out bounds))
            return false;

        var dock = _shownRect;
        return bounds.Left < dock.Right && bounds.Right > dock.Left && bounds.Top < dock.Bottom && bounds.Bottom > dock.Top;
    }
}
