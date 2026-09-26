using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Shell;
using ManagedShell.WindowsTasks;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

/// <summary>
/// Live DWM thumbnail preview window for open application windows (Taskbar Live Thumbnail Preview).
/// </summary>
public sealed class WindowPreviewWindow : Window
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
    }

    public static WindowPreviewWindow Instance { get; } = new();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowEffects.MakeToolWindow(_hwnd, noActivate: true);
        WindowEffects.ExtendGlass(_hwnd);
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

    private void RebuildCards(List<ApplicationWindow> windows)
    {
        UnregisterAllThumbnails();
        _previewItems.Clear();
        _cardsPanel.Children.Clear();

        // Show at most 8 windows side by side
        foreach (var window in windows.Take(8))
        {
            var w = window;

            var card = new Border
            {
                Width = CardWidth,
                Height = CardHeight,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(4),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };

            var cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 1. Header row (Icon + Title + Close button)
            var headerGrid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });

            var iconImg = new Image
            {
                Width = 16,
                Height = 16,
                Source = w.Icon ?? ShellIcons.GetWindowIcon(w.Handle),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            Grid.SetColumn(iconImg, 0);

            var titleText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(w.Title) ? "Window" : w.Title,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0),
            };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            Grid.SetColumn(titleText, 1);

            // Close button (✕)
            var closeBtn = new Button
            {
                Content = "\uE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 9,
                Width = 20,
                Height = 20,
                Style = Application.Current.TryFindResource("DockButton") as Style,
                ToolTip = "Close",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0),
            };
            closeBtn.Click += (s, e) =>
            {
                e.Handled = true;
                CloseWindowFromPreview(w);
            };
            Grid.SetColumn(closeBtn, 2);

            headerGrid.Children.Add(iconImg);
            headerGrid.Children.Add(titleText);
            headerGrid.Children.Add(closeBtn);
            Grid.SetRow(headerGrid, 0);

            // 2. Live Preview Host (DWM Thumbnail Host)
            var thumbHost = new Border
            {
                Width = ThumbWidth,
                Height = ThumbHeight,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(4, 0, 4, 4),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(thumbHost, 1);

            cardGrid.Children.Add(headerGrid);
            cardGrid.Children.Add(thumbHost);
            card.Child = cardGrid;

            // Hover effect
            card.MouseEnter += (_, _) =>
            {
                card.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");
            };
            card.MouseLeave += (_, _) =>
            {
                card.Background = Brushes.Transparent;
            };

            // Bring window to front on click
            card.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (w.IsMinimized) w.Restore();
                w.BringToFront();
                HidePreview();
            };

            // Middle click closes the window, like the Windows taskbar. Releasing outside the card cancels.
            card.MouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                e.Handled = true;
                card.CaptureMouse();
            };
            card.MouseUp += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                e.Handled = true;
                bool captured = card.IsMouseCaptured;
                card.ReleaseMouseCapture();
                var position = e.GetPosition(card);
                bool inside = position.X >= 0 && position.Y >= 0 && position.X <= card.ActualWidth && position.Y <= card.ActualHeight;
                if (captured && inside) CloseWindowFromPreview(w);
            };
            card.ToolTip = "Click to switch · Middle-click to close";

            _previewItems.Add((thumbHost, w));
            _cardsPanel.Children.Add(card);
        }
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

    private void RegisterThumbnails()
    {
        if (_hwnd == IntPtr.Zero)
            _hwnd = new WindowInteropHelper(this).EnsureHandle();

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return;

        double scale = source.CompositionTarget.TransformToDevice.M11;

        foreach (var (host, window) in _previewItems)
        {
            if (window.Handle == IntPtr.Zero) continue;

            try
            {
                var hostScreen = host.PointToScreen(new Point(0, 0));
                var pt = new POINT { X = (int)hostScreen.X, Y = (int)hostScreen.Y };
                ScreenToClient(_hwnd, ref pt);

                int hostW = (int)Math.Round(host.ActualWidth * scale);
                int hostH = (int)Math.Round(host.ActualHeight * scale);

                if (hostW <= 0 || hostH <= 0) continue;

                int hr = DwmRegisterThumbnail(_hwnd, window.Handle, out IntPtr hThumb);
                if (hr == 0 && hThumb != IntPtr.Zero)
                {
                    _thumbnails.Add(hThumb);

                    // Oran koruma (Aspect ratio fitting)
                    int destW = hostW;
                    int destH = hostH;

                    if (DwmQueryThumbnailSourceSize(hThumb, out PSIZE srcSize) == 0 && srcSize.x > 0 && srcSize.y > 0)
                    {
                        double aspect = (double)srcSize.x / srcSize.y;
                        if (aspect > (double)hostW / hostH)
                        {
                            destW = hostW;
                            destH = (int)Math.Round(hostW / aspect);
                        }
                        else
                        {
                            destH = hostH;
                            destW = (int)Math.Round(hostH * aspect);
                        }
                    }

                    int offsetX = pt.X + (hostW - destW) / 2;
                    int offsetY = pt.Y + (hostH - destH) / 2;

                    var props = new DWM_THUMBNAIL_PROPERTIES
                    {
                        dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY,
                        rcDestination = new RECT(offsetX, offsetY, offsetX + destW, offsetY + destH),
                        fVisible = true,
                        opacity = 255,
                    };

                    DwmUpdateThumbnailProperties(hThumb, ref props);
                }
            }
            catch
            {
                // Continue if single window thumbnail cannot be acquired
            }
        }
    }

    private void UnregisterAllThumbnails()
    {
        foreach (var thumb in _thumbnails)
        {
            try
            {
                DwmUnregisterThumbnail(thumb);
            }
            catch
            {
                // Yoksay
            }
        }
        _thumbnails.Clear();
    }

    private void EnsureHandle()
    {
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            WindowEffects.MakeToolWindow(_hwnd, noActivate: true);
            WindowEffects.ExtendGlass(_hwnd);
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
