using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using ManagedShell;
using ManagedShell.AppBar;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

/// <summary>
/// Invisible and click-through AppBar window. Reserves space for dock at the screen edge
/// so maximized windows do not go under the dock. The dock window positions itself within this area.
/// (Because dock's blur background ignores window regions, floating margins are solved with a separate window.)
/// </summary>
public sealed class SpaceReserver : AppBarWindow
{
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int MaxFailedRepairs = 4;
    private const int MaxRepairsPerMinute = 6;
    private static readonly int WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

    private double _dpiScale;
    private bool _notifyQueued;
    private DispatcherTimer? _reregisterTimer;
    private (IntPtr Monitor, int Original, int Forced)? _forcedWorkArea;
    private int _failedRepairs;
    private int _repairsInWindow;
    private DateTime _repairWindowStart;
    private bool _explorerIgnoresBar;

    /// <param name="dpiScale">Scale of the dock's display, so the reserved band is exactly as thick as the dock.</param>
    public SpaceReserver(ShellManager shell, AppBarScreen screen, AppBarEdge edge, double thicknessDip, double dpiScale)
        : base(shell.AppBarManager, shell.ExplorerHelper, shell.FullScreenHelper, screen, edge, AppBarMode.Normal, thicknessDip)
    {
        _dpiScale = dpiScale;
        // The dock decides which display it is on; ManagedShell would move a primary display's bar to the new primary.
        ProcessScreenChanges = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Title = "DockHub Space";
        Width = 1;
        Height = 1;
        Left = screen.Bounds.Left;
        Top = screen.Bounds.Top;
    }

    /// <summary>Fired when the reserved rectangle (physical pixels) changes.</summary>
    public event Action? RectChanged;

    public RECT Rect => new(WindowRect.Left, WindowRect.Top, WindowRect.Right, WindowRect.Bottom);

    protected override void OnSourceInitialized(object sender, EventArgs e)
    {
        base.OnSourceInitialized(sender, e);
        WindowEffects.MakeToolWindow(Handle, noActivate: true, clickThrough: true);

        // ManagedShell sizes the bar with its own DPI guess (the primary display's), and a tool window never receives
        // WM_DPICHANGED on Windows 11 to correct it. The dock uses its display's real DPI, so a mismatch let
        // maximized windows slide under the dock; negotiate the position again with the dock's DPI.
        if (DpiScale != _dpiScale)
        {
            DpiScale = _dpiScale;
            UpdatePosition();
        }
        QueueNotify();
    }

    /// <summary>Follows the dock's thickness (DIP) and display scale without registering the AppBar again.</summary>
    public void Update(double thicknessDip, double dpiScale)
    {
        bool vertical = Orientation == Orientation.Vertical;
        if ((vertical ? DesiredWidth : DesiredHeight) == thicknessDip && _dpiScale == dpiScale && DpiScale == dpiScale) return;

        if (vertical) DesiredWidth = thicknessDip;
        else DesiredHeight = thicknessDip;
        _dpiScale = dpiScale;
        DpiScale = dpiScale;
        if (Handle != IntPtr.Zero && !AllowClose) UpdatePosition();
    }

    /// <summary>True when the display's work area leaves the reserved band out (maximized windows stop at the dock).</summary>
    private bool IsExcludedFrom(RECT workArea)
    {
        const int tolerance = 1;
        var r = Rect;
        return AppBarEdge switch
        {
            AppBarEdge.Top => workArea.Top >= r.Bottom - tolerance,
            AppBarEdge.Left => workArea.Left >= r.Right - tolerance,
            AppBarEdge.Right => workArea.Right <= r.Left + tolerance,
            _ => workArea.Bottom <= r.Top + tolerance,
        };
    }

    /// <summary>
    /// Makes sure maximized windows really stop at the dock. Explorer can lose an AppBar (display changes, sign-in
    /// races), so the bar is registered again first; if Windows still ignores it, the work area is trimmed directly.
    /// Returns true when something was repaired and the result should be checked again.
    /// </summary>
    public bool Verify(MonitorInfo monitor)
    {
        var rect = Rect;
        if (Handle == IntPtr.Zero || AllowClose || rect.Width <= 0 || rect.Height <= 0) return false;
        if (IsExcludedFrom(monitor.WorkArea))
        {
            _failedRepairs = 0;
            return false;
        }

        // Never fight Explorer in a loop: stop after a few repairs that did not stick, and pace the rest.
        if (_failedRepairs > MaxFailedRepairs) return false;
        if (_failedRepairs == MaxFailedRepairs)
        {
            _failedRepairs++;
            Log.Warn($"Windows keeps the dock area {rect} inside the work area {monitor.WorkArea} on {monitor.DeviceName}; giving up.");
            return false;
        }
        var now = DateTime.UtcNow;
        if (now - _repairWindowStart > TimeSpan.FromMinutes(1))
        {
            _repairWindowStart = now;
            _repairsInWindow = 0;
        }
        if (++_repairsInWindow > MaxRepairsPerMinute) return false;

        _failedRepairs++;
        if (!_explorerIgnoresBar && _failedRepairs <= 2)
        {
            Log.Info($"Work area {monitor.WorkArea} overlaps the dock area {rect} on {monitor.DeviceName}; registering the AppBar again.");
            Reregister();
        }
        else
        {
            _explorerIgnoresBar = true;
            Log.Info($"Work area {monitor.WorkArea} still overlaps the dock area {rect} on {monitor.DeviceName}; setting the work area directly.");
            ForceWorkArea(monitor.Handle, monitor.WorkArea);
        }
        return true;
    }

    /// <summary>Registers the AppBar with Explorer again (Explorer forgets every AppBar when it restarts).</summary>
    public void Reregister()
    {
        if (Handle == IntPtr.Zero || AllowClose || IsClosing) return;
        UnregisterAppBar();
        RegisterAppBar();
    }

    /// <summary>
    /// Last resort when Explorer keeps ignoring the AppBar: trims the display's work area directly so maximized
    /// windows still stop at the dock. Undone when the reserver closes.
    /// </summary>
    private bool ForceWorkArea(IntPtr monitor, RECT workArea)
    {
        var r = Rect;
        int original = EdgeOf(workArea);
        int forced = AppBarEdge switch
        {
            AppBarEdge.Top => Math.Max(workArea.Top, r.Bottom),
            AppBarEdge.Left => Math.Max(workArea.Left, r.Right),
            AppBarEdge.Right => Math.Min(workArea.Right, r.Left),
            _ => Math.Min(workArea.Bottom, r.Top),
        };
        if (forced == original) return false;

        var trimmed = WithEdge(workArea, forced);
        if (!SystemParametersInfo(SPI_SETWORKAREA, 0, ref trimmed, SPIF_SENDCHANGE)) return false;
        _forcedWorkArea = (monitor, original, forced);
        return true;
    }

    private void RestoreWorkArea()
    {
        if (_forcedWorkArea is not { } forced) return;
        _forcedWorkArea = null;
        // Explorer may have recalculated the work area since; only undo our own trim.
        if (MonitorHelper.TryGet(forced.Monitor) is not { } monitor || EdgeOf(monitor.WorkArea) != forced.Forced) return;
        var restored = WithEdge(monitor.WorkArea, forced.Original);
        SystemParametersInfo(SPI_SETWORKAREA, 0, ref restored, SPIF_SENDCHANGE);
    }

    private int EdgeOf(RECT r) => AppBarEdge switch
    {
        AppBarEdge.Top => r.Top,
        AppBarEdge.Left => r.Left,
        AppBarEdge.Right => r.Right,
        _ => r.Bottom,
    };

    private RECT WithEdge(RECT r, int value) => AppBarEdge switch
    {
        AppBarEdge.Top => r with { Top = value },
        AppBarEdge.Left => r with { Left = value },
        AppBarEdge.Right => r with { Right = value },
        _ => r with { Bottom = value },
    };

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var result = base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        if (msg == WM_WINDOWPOSCHANGED) QueueNotify();
        else if (msg == WM_TASKBARCREATED && WM_TASKBARCREATED != 0) QueueReregister();
        return result;
    }

    /// <summary>After Explorer restarts, registers again once it has settled.</summary>
    private void QueueReregister()
    {
        if (_reregisterTimer is null)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Log.Info("Explorer restarted; reserving the dock's screen area again.");
                Reregister();
            };
            _reregisterTimer = timer;
        }
        _reregisterTimer.Stop();
        _reregisterTimer.Start();
    }

    private void QueueNotify()
    {
        if (_notifyQueued) return;
        _notifyQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _notifyQueued = false;
            RectChanged?.Invoke();
        });
    }

    public void CloseReserver()
    {
        _reregisterTimer?.Stop();
        AllowClose = true;
        Close();
        RestoreWorkArea();
    }
}
