using System.Runtime.InteropServices;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Shell;

/// <summary>
/// Hides and restores the Explorer taskbar.
/// <list type="bullet">
/// <item>Taskbar is put into "auto-hide" state (work area is freed) and its windows are hidden.</item>
/// <item>If Explorer unhides taskbar on its own (Explorer restart, setting change), it is hidden again;
///       however, when Start / Search / Quick settings are open it waits, otherwise those menus cannot open.</item>
/// <item>Original state is saved to session.json; restored on next launch even if application crashes.</item>
/// </list>
/// </summary>
public sealed class TaskbarController : IDisposable
{
    private static readonly HashSet<string> ShellFlyoutClasses = new(StringComparer.Ordinal)
    {
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow", "ControlCenterWindow",
        "Shell_InputSwitchTopLevelWindow",
    };

    private readonly Func<IntPtr> _ownTrayProvider;
    private readonly Func<bool> _launcherVisible;
    private readonly DispatcherTimer _monitor;
    private readonly EventHandler _onTick;
    private int _originalState;
    private int _visibleTicks;

    // Explorer re-showing its taskbar is caught by a show event; the timer only confirms it (fast for a few
    // seconds after such an event, otherwise a slow safety check).
    private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FastPeriod = TimeSpan.FromSeconds(5);
    private DateTime _fastUntil;
    private WinEventProc? _showProc;
    private IntPtr _showHook;

    public TaskbarController(Func<IntPtr> ownTrayProvider, Func<bool> launcherVisible)
    {
        _ownTrayProvider = ownTrayProvider;
        _launcherVisible = launcherVisible;
        _onTick = (_, _) => Enforce();
        _monitor = new DispatcherTimer(DispatcherPriority.Background) { Interval = SlowInterval };
        _monitor.Tick += _onTick;
    }

    public bool IsHidden { get; private set; }

    private IntPtr ExplorerTray => ManagedShell.Common.Helpers.WindowHelper.FindWindowsTray(_ownTrayProvider());

    public void Hide()
    {
        if (IsHidden) return;
        var tray = ExplorerTray;
        if (tray == IntPtr.Zero)
        {
            Log.Info("Explorer taskbar not found.");
            return;
        }

        _originalState = GetState(tray);
        SessionState.Save(new SessionState { TaskbarHidden = true, OriginalTaskbarState = _originalState });

        SetState(tray, ABS_AUTOHIDE | (_originalState & ABS_ALWAYSONTOP));
        SetVisible(tray, false);
        IsHidden = true;
        _monitor.Start();
        StartShowHook();
        Log.Info($"Windows taskbar hidden (original state {_originalState}).");
    }

    public void Restore()
    {
        if (IsHidden)
        {
            IsHidden = false;
            var tray = ExplorerTray;
            if (tray != IntPtr.Zero)
                SetState(tray, _originalState);
            SetVisible(tray, true);
            SessionState.Clear();
            Log.Info("Windows taskbar restored.");
        }

        try
        {
            // Hooks and timers belong to the UI thread (Restore can also run from process exit).
            if (_monitor.Dispatcher.CheckAccess()) { StopShowHook(); _monitor.Stop(); }
            else _monitor.Dispatcher.BeginInvoke(() => { StopShowHook(); _monitor.Stop(); });
        }
        catch
        {
            // shutdown
        }
    }

    private void StartShowHook()
    {
        if (_showHook != IntPtr.Zero) return;
        _showProc ??= OnWindowShown;
        _showHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _showProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    private void StopShowHook()
    {
        if (_showHook == IntPtr.Zero) return;
        UnhookWinEvent(_showHook);
        _showHook = IntPtr.Zero;
    }

    private void OnWindowShown(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || idChild != 0) return;
        string cls = GetClassName(hwnd);
        if (cls is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")) return;
        _fastUntil = DateTime.UtcNow + FastPeriod;
        _monitor.Interval = FastInterval;
    }

    private void Enforce()
    {
        if (!IsHidden) return;
        if (_monitor.Interval != SlowInterval && DateTime.UtcNow > _fastUntil) _monitor.Interval = SlowInterval;

        var tray = ExplorerTray;
        bool anyVisible = (tray != IntPtr.Zero && IsWindowVisible(tray)) || AnySecondaryTrayVisible();

        if (!anyVisible)
        {
            _visibleTicks = 0;
            return;
        }

        // Explorer shows taskbar when Start / Search / quick settings open; do not intervene.
        if (_launcherVisible() || ShellFlyoutClasses.Contains(GetClassName(GetForegroundWindow())))
        {
            _visibleTicks = 0;
            return;
        }

        // Short tolerance: wait during initial moments of menu opening as well.
        if (++_visibleTicks < 2) return;

        _visibleTicks = 0;
        if ((GetState(tray) & ABS_AUTOHIDE) == 0)
            SetState(tray, ABS_AUTOHIDE | (_originalState & ABS_ALWAYSONTOP));
        SetVisible(tray, false);
    }

    private static bool AnySecondaryTrayVisible()
    {
        IntPtr secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (IsWindowVisible(secondary)) return true;
        }
        return false;
    }

    private static void SetVisible(IntPtr tray, bool visible)
    {
        uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW);
        if (tray != IntPtr.Zero)
            SetWindowPos(tray, visible ? IntPtr.Zero : HWND_BOTTOM, 0, 0, 0, 0, visible ? flags | SWP_NOZORDER : flags);

        IntPtr secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            SetWindowPos(secondary, visible ? IntPtr.Zero : HWND_BOTTOM, 0, 0, 0, 0, visible ? flags | SWP_NOZORDER : flags);
        }
    }

    // ------------------------------------------------------------------ Crash recovery (runs before ManagedShell starts)

    /// <summary>Restores taskbar if previous session closed while hidden.</summary>
    public static void RecoverFromPreviousSession()
    {
        var session = SessionState.Load();
        if (!session.TaskbarHidden) return;
        Log.Info("Restoring hidden taskbar from previous session.");
        ForceRestore(session.OriginalTaskbarState);
    }

    /// <summary>Emergency: makes taskbar visible under all conditions.</summary>
    public static void ForceShow()
    {
        var session = SessionState.Load();
        ForceRestore(session.TaskbarHidden ? session.OriginalTaskbarState : null);
    }

    private static void ForceRestore(int? state)
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero && state is int s)
            SetState(tray, s);
        SetVisible(tray, true);
        SessionState.Clear();

        // Tell applications to re-register their tray icons with Explorer (ManagedShell does this on clean exit;
        // prevent icons from being lost after a crash).
        SendNotifyMessage(HWND_BROADCAST, RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
    }

    private static int GetState(IntPtr tray)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = tray };
        return (int)SHAppBarMessage(ABM_GETSTATE, ref data);
    }

    private static void SetState(IntPtr tray, int state)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = tray, lParam = new IntPtr(state) };
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    /// <summary>Sends "show desktop" command to Explorer taskbar.</summary>
    public void ToggleDesktop()
    {
        var tray = ExplorerTray;
        if (tray != IntPtr.Zero)
            SendMessage(tray, WM_COMMAND, new IntPtr(407), IntPtr.Zero);
    }

    public void Dispose()
    {
        Restore();
        _monitor.Tick -= _onTick;
    }
}

/// <summary>Session state for crash safety (session.json).</summary>
public sealed class SessionState
{
    public bool TaskbarHidden { get; set; }

    public int OriginalTaskbarState { get; set; }

    public static SessionState Load() => JsonStore.Load<SessionState>(AppPaths.SessionFile);

    public static void Save(SessionState state) => JsonStore.Save(AppPaths.SessionFile, state);

    public static void Clear()
    {
        try { File.Delete(AppPaths.SessionFile); } catch { /* ignore */ }
    }
}
