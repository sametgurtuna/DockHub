using System.Windows;
using System.Windows.Media;
using CustomDock.Native;
using ManagedShell;
using ManagedShell.AppBar;

namespace CustomDock.Dock;

/// <summary>
/// Invisible and click-through AppBar window. Reserves space for dock at the screen edge
/// so maximized windows do not go under the dock. The dock window positions itself within this area.
/// (Because dock's blur background ignores window regions, floating margins are solved with a separate window.)
/// </summary>
public sealed class SpaceReserver : AppBarWindow
{
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private bool _notifyQueued;

    public SpaceReserver(ShellManager shell, AppBarScreen screen, AppBarEdge edge, double thicknessDip)
        : base(shell.AppBarManager, shell.ExplorerHelper, shell.FullScreenHelper, screen, edge, AppBarMode.Normal, thicknessDip)
    {
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
        QueueNotify();
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var result = base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        if (msg == WM_WINDOWPOSCHANGED) QueueNotify();
        return result;
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
        AllowClose = true;
        Close();
    }
}
