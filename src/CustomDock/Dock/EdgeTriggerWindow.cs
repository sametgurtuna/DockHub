using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CustomDock.Native;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

/// <summary>
/// Otomatik gizleme modunda ekran kenarında duran, neredeyse tamamen saydam ince pencere.
/// Fare üzerine geldiğinde dock'u gösterir. Sürekli fare yoklaması yerine olay tabanlıdır.
/// </summary>
public sealed class EdgeTriggerWindow : Window
{
    private readonly IntPtr _hwnd;

    public EdgeTriggerWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // Alfa = 1: görünmez ama fare olaylarını alır.
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
        Title = "CustomDock Edge";

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
