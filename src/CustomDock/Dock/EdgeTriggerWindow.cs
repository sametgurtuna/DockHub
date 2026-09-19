using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CustomDock.Native;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

/// <summary>
/// Nearly fully transparent thin window that sits at the screen edge in auto-hide mode.
/// Reveals the dock when mouse hovers over it. Event-driven instead of constant mouse polling.
/// </summary>
public sealed class EdgeTriggerWindow : Window
{
    private readonly IntPtr _hwnd;

    public EdgeTriggerWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // Alpha = 1: invisible but receives mouse events.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        AllowDrop = true;
        Width = 1;
        Height = 1;
        Left = -32000;
        Top = -32000;
        Title = "DockHub Edge";

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        WindowEffects.MakeToolWindow(_hwnd, noActivate: true);

        MouseEnter += (_, _) => Entered?.Invoke();
        MouseLeave += (_, _) => Exited?.Invoke();
        DragEnter += (_, _) => DragEntered?.Invoke();
    }

    public event Action? Entered;

    public event Action? Exited;

    public event Action? DragEntered;

    public IntPtr Handle => _hwnd;

    public void Place(RECT rect)
    {
        SetWindowPos(_hwnd, HWND_TOPMOST, rect.Left, rect.Top, Math.Max(1, rect.Width), Math.Max(1, rect.Height),
            SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public void SetActive(bool active)
    {
        if (active && !IsVisible) Show();
        else if (!active && IsVisible) Hide();
    }
}
