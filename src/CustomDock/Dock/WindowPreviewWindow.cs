using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Shell;
using ManagedShell.WindowsTasks;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

/// <summary>
/// Live DWM thumbnail preview window for open application windows (Taskbar Live Thumbnail Preview).
/// </summary>
public sealed partial class WindowPreviewWindow : Window
{
    private const double CardWidth = 208;
    private const double CardHeight = 158;
    private const double ThumbWidth = 200;
    private const double ThumbHeight = 120;

    private readonly StackPanel _cardsPanel;
    private readonly Border _container;
    private readonly List<IntPtr> _thumbnails = new();
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _monitorTimer;
    private int _outsideTicks;
    private readonly List<(Border Host, ApplicationWindow Window)> _previewItems = new();

    private static readonly TimeSpan ClosingGrace = TimeSpan.FromSeconds(3);
    private readonly Dictionary<IntPtr, DateTime> _closingWindows = new();

    private const double MediaRowHeight = 30;

    private readonly DispatcherTimer _peekTimer;
    private ApplicationWindow? _peekTarget;
    private ApplicationWindow? _peeking;
    private int _cardsVersion;

    private IntPtr _hwnd;
    private AppButton? _currentButton;
    private AppGroup? _currentGroup;
    private DockEdge _currentEdge = DockEdge.Bottom;
    private bool _isClosing;

    public WindowPreviewWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!IsMouseOverPreviewOrButton())
                HidePreview();
        };

        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _monitorTimer.Tick += (_, _) =>
        {
            if (!IsVisible)
            {
                _monitorTimer.Stop();
                return;
            }

            if (!IsMouseOverPreviewOrButton())
            {
                _outsideTicks++;
                if (_outsideTicks >= 2) // ~100ms outside
                {
                    HidePreview();
                }
            }
            else
            {
                _outsideTicks = 0;
            }
        };

        _cardsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        _container = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            Child = _cardsPanel,
        };
        _container.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        _container.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");

        // Shadow effect
        _container.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 3,
            Opacity = 0.35,
            Color = Colors.Black,
        };

        Content = _container;

        MouseEnter += (_, _) =>
        {
            _hideTimer.Stop();
            _outsideTicks = 0;
        };
        MouseLeave += (_, _) => ScheduleHide(100);
        SourceInitialized += OnSourceInitialized;

        // Aero Peek: resting on a card shows that window alone for a moment.
        _peekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _peekTimer.Tick += (_, _) =>
        {
            _peekTimer.Stop();
            if (IsVisible && _peekTarget is { } target && !target.IsMinimized) BeginPeek(target);
        };
        PreviewMouseWheel += (_, e) =>
        {
            if (CycleWindows(e.Delta)) e.Handled = true;
        };
    }

    public static WindowPreviewWindow Instance { get; } = new();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowEffects.MakeToolWindow(_hwnd, noActivate: true);
        WindowEffects.ExtendGlass(_hwnd);
        ManagedShell.Common.Helpers.WindowHelper.ExcludeWindowFromPeek(_hwnd);
    }

    public void ShowFor(AppButton button, AppGroup group, DockEdge edge)
    {
        if (_isClosing) return;
        _hideTimer.Stop();
        _outsideTicks = 0;

        // Windows closed from the preview keep their card hidden while they shut down (or show a save prompt).
        var now = DateTime.UtcNow;
        foreach (var handle in _closingWindows.Where(p => now - p.Value > ClosingGrace).Select(p => p.Key).ToList())
            _closingWindows.Remove(handle);

        var windows = group.Windows.Where(w => w.ShowInTaskbar && !_closingWindows.ContainsKey(w.Handle)).ToList();
        if (windows.Count == 0)
        {
            HidePreview();
            return;
        }

        _currentButton = button;
        _currentEdge = edge;
        if (!ReferenceEquals(_currentGroup, group))
        {
            if (_currentGroup is not null) _currentGroup.PropertyChanged -= OnGroupPropertyChanged;
            group.PropertyChanged += OnGroupPropertyChanged;
        }
        _currentGroup = group;

        RebuildCards(windows);

        if (!IsVisible)
        {
            Show();
            EnsureHandle();
        }

        UpdateLayout();
        PositionWindow(button, edge);
        RegisterThumbnails();
        _monitorTimer.Start();
    }

    public void ScheduleHide(int delayMs = 100)
    {
        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _hideTimer.Start();
    }

    public void HidePreview()
    {
        EndPeek();
        _monitorTimer.Stop();
        _hideTimer.Stop();
        _outsideTicks = 0;
        UnregisterAllThumbnails();
        if (_currentGroup is not null) _currentGroup.PropertyChanged -= OnGroupPropertyChanged;
        _currentButton = null;
        _currentGroup = null;
        _previewItems.Clear();
        _cardsPanel.Children.Clear();
        Hide();
    }

    /// <summary>A window of the previewed app opened or closed: rebuild the cards in place.</summary>
    private void OnGroupPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppGroup.WindowCount)) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (IsVisible && ReferenceEquals(sender, _currentGroup) && _currentButton is not null && _currentGroup is not null)
                ShowFor(_currentButton, _currentGroup, _currentEdge);
        });
    }

    /// <summary>Close button and middle click: asks the window to close and drops its card right away.</summary>
    private void CloseWindowFromPreview(ApplicationWindow window)
    {
        _closingWindows[window.Handle] = DateTime.UtcNow;
        window.Close();
        if (_currentButton is not null && _currentGroup is not null)
            ShowFor(_currentButton, _currentGroup, _currentEdge);
        else
            HidePreview();
    }

    private bool IsMouseOverPreviewOrButton()
    {
        if (!IsVisible) return false;

        if (IsMouseOver) return true;
        if (_currentButton is { IsMouseOver: true }) return true;

        if (_currentButton is null || !GetCursorPos(out var pt))
            return false;

        try
        {
            var p = new Point(pt.X, pt.Y);

            // Button bounds in physical screen pixels
            var bTopLeft = _currentButton.PointToScreen(new Point(0, 0));
            var bBottomRight = _currentButton.PointToScreen(new Point(_currentButton.ActualWidth, _currentButton.ActualHeight));
            var bRect = new Rect(bTopLeft, bBottomRight);
            var bHit = bRect;
            bHit.Inflate(4, 4);
            if (bHit.Contains(p))
                return true;

            // Preview window bounds in physical screen pixels
            var prevTopLeft = PointToScreen(new Point(0, 0));
            var prevBottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
            var prevRect = new Rect(prevTopLeft, prevBottomRight);
            var prevHit = prevRect;
            prevHit.Inflate(4, 4);
            if (prevHit.Contains(p))
                return true;

            // Transition corridor between button and preview window
            double minX = Math.Min(bRect.Left, prevRect.Left);
            double maxX = Math.Max(bRect.Right, prevRect.Right);
            double minY = Math.Min(bRect.Top, prevRect.Top);
            double maxY = Math.Max(bRect.Bottom, prevRect.Bottom);

            // In horizontal dock (Bottom or Top):
            // Check if cursor is in the vertical gap between button and preview window
            bool inVerticalGap = p.Y >= Math.Min(bRect.Bottom, prevRect.Bottom) - 4 &&
                                 p.Y <= Math.Max(bRect.Top, prevRect.Top) + 4;
            bool inHorizontalSpan = p.X >= minX - 6 && p.X <= maxX + 6;

            if (inVerticalGap && inHorizontalSpan)
                return true;

            // In vertical dock (Left or Right):
            bool inHorizontalGap = p.X >= Math.Min(bRect.Right, prevRect.Right) - 4 &&
                                   p.X <= Math.Max(bRect.Left, prevRect.Left) + 4;
            bool inVerticalSpan = p.Y >= minY - 6 && p.Y <= maxY + 6;

            if (inHorizontalGap && inVerticalSpan)
                return true;
        }
        catch
        {
            // Ignore if detached from visual tree
        }

        return false;
    }

    private void PositionWindow(AppButton button, DockEdge edge)
    {
        if (PresentationSource.FromVisual(button) is not { } source) return;

        double scale = source.CompositionTarget.TransformToDevice.M11;
        var btnScreen = button.PointToScreen(new Point(0, 0));
        double btnScreenX = btnScreen.X / scale;
        double btnScreenY = btnScreen.Y / scale;
        double btnW = button.ActualWidth;
        double btnH = button.ActualHeight;

        double previewW = ActualWidth;
        double previewH = ActualHeight;

        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)btnScreen.X, (int)btnScreen.Y));
        var work = screen.WorkingArea;
        double workLeft = work.Left / scale;
        double workTop = work.Top / scale;
        double workRight = work.Right / scale;
        double workBottom = work.Bottom / scale;

        double targetX, targetY;

        switch (edge)
        {
            case DockEdge.Top:
                targetX = btnScreenX + (btnW - previewW) / 2.0;
                targetY = btnScreenY + btnH + 8;
                break;
            case DockEdge.Left:
                targetX = btnScreenX + btnW + 8;
                targetY = btnScreenY + (btnH - previewH) / 2.0;
                break;
            case DockEdge.Right:
                targetX = btnScreenX - previewW - 8;
                targetY = btnScreenY + (btnH - previewH) / 2.0;
                break;
            default: // Bottom
                targetX = btnScreenX + (btnW - previewW) / 2.0;
                targetY = btnScreenY - previewH - 8;
                break;
        }

        // Clamp to screen bounds
        if (targetX < workLeft + 6) targetX = workLeft + 6;
        if (targetX + previewW > workRight - 6) targetX = workRight - previewW - 6;
        if (targetY < workTop + 6) targetY = workTop + 6;
        if (targetY + previewH > workBottom - 6) targetY = workBottom - previewH - 6;

        Left = targetX;
        Top = targetY;
    }

    private void EnsureHandle()
    {
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            WindowEffects.MakeToolWindow(_hwnd, noActivate: true);
            WindowEffects.ExtendGlass(_hwnd);
            ManagedShell.Common.Helpers.WindowHelper.ExcludeWindowFromPeek(_hwnd);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _hideTimer.Stop();
        UnregisterAllThumbnails();
        base.OnClosed(e);
    }
}
