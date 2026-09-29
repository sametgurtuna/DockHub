using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Native;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

public partial class DockWindow
{
    private void OnFullScreenAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        bool fullscreen = _shell.Manager.FullScreenHelper.FullScreenApps.Any(app =>
            app.screen.IsVirtualScreen || string.Equals(app.screen.DeviceName, _monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (fullscreen == _fullscreen) return;
        _fullscreen = fullscreen;
        UpdateVisibility(animate: false);
        UpdateTrigger();
        if (!fullscreen) ScheduleReservationCheck();
    }

    private void OnLauncherVisibilityChanged(bool visible)
    {
        // Start / Search open on the display of the dock that launched them.
        bool owner = s_trayHostOwner is null ? IsMain : s_trayHostOwner == this;
        // Hand the flyouts back to the main display once they close (e.g. for the Windows key).
        if (!visible && owner && !IsMain)
            Dispatcher.BeginInvoke(() => s_docks.FirstOrDefault(d => d.IsMain)?.UpdateTrayHost());

        if (!_surface.AutoHide) return;
        if (!visible) ScheduleAutoHide();
        else if (owner) Reveal();
    }

    private bool IsFullscreenBlocked => _config.HideOnFullscreen && _fullscreen;

    private void UpdateVisibility(bool animate)
    {
        bool shouldShow = !IsFullscreenBlocked && (!_surface.AutoHide || _revealed);
        if (shouldShow == _shown && (_animating || IsVisible == shouldShow)) return;
        _shown = shouldShow;
        DockVisibility.Report(this, shouldShow);
        _ = AnimateAsync(shouldShow, animate);
        UpdateTrigger();
    }

    private async Task AnimateAsync(bool show, bool animate)
    {
        int version = ++_animationVersion;
        RECT from, to;

        if (show)
        {
            if (!IsVisible)
            {
                if (_shownRect.Width <= 0) Reposition();
                MoveTo(animate ? HiddenRect() : _shownRect);
                Show();
            }
            if (!animate)
            {
                MoveTo(_shownRect);
                return;
            }
            GetWindowRect(_hwnd, out from);
            to = _shownRect;
        }
        else
        {
            if (!IsVisible) return;
            if (!animate)
            {
                Hide();
                return;
            }
            GetWindowRect(_hwnd, out from);
            to = HiddenRect();
        }

        _animating = true;
        try
        {
            var duration = show ? ShowDuration : HideDuration;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < duration)
            {
                double t = sw.Elapsed.TotalMilliseconds / duration.TotalMilliseconds;
                // Open: slow deceleration (ease-out quint); close: accelerating exit
                double eased = show ? 1 - Math.Pow(1 - t, 5) : t * t * t;
                int x = (int)Math.Round(from.Left + (to.Left - from.Left) * eased);
                int y = (int)Math.Round(from.Top + (to.Top - from.Top) * eased);
                SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                await NextFrame();
                if (version != _animationVersion || _closing) return;
            }

            MoveTo(to);
            if (!show) Hide();
        }
        finally
        {
            if (version == _animationVersion) _animating = false;
        }
    }

    /// <summary>Waits for the next screen frame (synchronizes animation with display refresh).</summary>
    private static Task NextFrame()
    {
        var tcs = new TaskCompletionSource();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            CompositionTarget.Rendering -= handler;
            tcs.TrySetResult();
        };
        CompositionTarget.Rendering += handler;
        return tcs.Task;
    }

    public void Reveal()
    {
        _revealed = true;
        _hideTimer.Stop();
        UpdateVisibility(animate: true);
        ScheduleAutoHide();
    }

    private void ScheduleAutoHide()
    {
        if (!_surface.AutoHide || _interactionCount > 0 || _closing) return;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void TryAutoHide()
    {
        if (!_surface.AutoHide || _interactionCount > 0 || !_shown) return;
        if (_shell.IsLauncherVisible || IsCursorOverDock()) return;
        if (SmartHideActive && !ActiveWindowOverlapsDock()) return;
        if (_inputMode && IsActive) return;

        _revealed = false;
        UpdateVisibility(animate: true);
    }

    private bool IsCursorOverDock()
    {
        if (!GetCursorPos(out var p) || !GetWindowRect(_hwnd, out var r)) return false;
        const int slack = 2;
        return p.X >= r.Left - slack && p.X <= r.Right + slack && p.Y >= r.Top - slack && p.Y <= r.Bottom + slack;
    }

    private void UpdateTrigger()
    {
        bool active = _surface.AutoHide && !IsFullscreenBlocked && !_shown && !_closing;
        if (!active)
        {
            _trigger?.SetActive(false);
            return;
        }

        if (_trigger is null)
        {
            _trigger = new EdgeTriggerWindow();
            _trigger.Entered += () => _revealTimer.Start();
            _trigger.Exited += () => _revealTimer.Stop();
            _trigger.DragEntered += Reveal;
        }

        var b = _monitor.Bounds;
        var dock = _shownRect;
        RECT rect = _surface.Edge switch
        {
            DockEdge.Top => new RECT(dock.Left, b.Top, dock.Right, b.Top + TriggerThickness),
            DockEdge.Left => new RECT(b.Left, dock.Top, b.Left + TriggerThickness, dock.Bottom),
            DockEdge.Right => new RECT(b.Right - TriggerThickness, dock.Top, b.Right, dock.Bottom),
            _ => new RECT(dock.Left, b.Bottom - TriggerThickness, dock.Right, b.Bottom),
        };

        _trigger.SetActive(true);
        _trigger.Place(rect);
    }

    // ------------------------------------------------------------------ Scrolling

    private double ScrollOffset => IsVertical ? Scroller.VerticalOffset : Scroller.HorizontalOffset;

    private double ScrollableLength => IsVertical ? Scroller.ScrollableHeight : Scroller.ScrollableWidth;

    private double ViewportLength => IsVertical ? Scroller.ViewportHeight : Scroller.ViewportWidth;

    private double ExtentLength => IsVertical ? Scroller.ExtentHeight : Scroller.ExtentWidth;

    /// <summary>
    /// Where each item begins along the strip (its margin included), for <see cref="ScrollSnap"/>. Layout slots, not
    /// drawn positions: hover magnification, wiggle and move animations don't shift the stops.
    /// </summary>
    private List<double> ItemStarts()
    {
        bool vertical = IsVertical;
        var starts = new List<double>();
        foreach (var child in ItemsPanel.Children.OfType<FrameworkElement>())
        {
            if (child.Visibility != Visibility.Visible) continue;
            var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(child);
            starts.Add(vertical ? slot.Y : slot.X);
        }
        return starts;
    }

    /// <summary>Wheel movement not yet enough to reach the next item (touchpads send many small steps).</summary>
    private double _pendingScroll;

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Do not scroll dock horizontally when menu or popup (mixer, calendar, etc.) is open
        if (GlobalPopupDismissHook.HasActivePopupsOrMenus) return;

        // Over an app whose preview is open, the wheel steps through that app's windows.
        if (WindowPreviewWindow.Instance.IsVisible && FindAncestor<AppButton>(e.OriginalSource as DependencyObject) is not null
            && WindowPreviewWindow.Instance.CycleWindows(e.Delta))
        {
            e.Handled = true;
            return;
        }

        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is TextBox { IsKeyboardFocusWithin: true } box && box.ExtentHeight > box.ViewportHeight) return;
            // Audio widget uses mouse wheel for itself (volume adjustment)
            if (d is Widgets.AudioWidget) return;
            if (ReferenceEquals(d, Scroller)) break;
        }

        if (ScrollableLength <= 0) return;
        e.Handled = true;
        SmoothScrollBy(-e.Delta * 0.9);
    }

    /// <summary>Scrolls by about <paramref name="delta"/>, stopping where an item begins (no item is cut at the leading edge).</summary>
    private void SmoothScrollBy(double delta)
    {
        double from = double.IsNaN(_scrollTarget) ? ScrollOffset : _scrollTarget;
        // A change of direction starts counting again.
        if (Math.Sign(delta) != Math.Sign(_pendingScroll)) _pendingScroll = 0;
        _pendingScroll += delta;
        double snapped = ScrollSnap.Snap(ItemStarts(), ViewportLength, ExtentLength, from, from + _pendingScroll);
        if (Math.Abs(snapped - from) < 0.5) return; // not halfway to the next item yet
        _pendingScroll = 0;
        _scrollTarget = snapped;
        if (_scrollAnimating) return;
        _scrollAnimating = true;
        _lastScrollFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnScrollFrame;
    }

    private void OnScrollFrame(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        double dt = _lastScrollFrame == TimeSpan.Zero ? 1 / 60.0 : (time - _lastScrollFrame).TotalSeconds;
        if (dt <= 0) return; // second call for same frame
        _lastScrollFrame = time;
        dt = Math.Min(dt, 0.05);

        double current = ScrollOffset;
        double diff = _scrollTarget - current;
        // Critically damped approach: settles into target in ~120 ms
        double next = Math.Abs(diff) < 0.5 ? _scrollTarget : current + diff * (1 - Math.Exp(-dt * 18));

        if (IsVertical) Scroller.ScrollToVerticalOffset(next);
        else Scroller.ScrollToHorizontalOffset(next);

        if (next == _scrollTarget)
        {
            CompositionTarget.Rendering -= OnScrollFrame;
            _scrollAnimating = false;
            _scrollTarget = double.NaN;
        }
    }

    // ------------------------------------------------------------------ Scrolling from the faded edges

    /// <summary>Length of the faded edge where a dragged item scrolls the strip (at most the fade, see <see cref="ScrollSnap.EdgeDirection"/>).</summary>
    private const double EdgeZone = 28;

    private DispatcherTimer? _edgeScrollTimer;
    private int _edgeScrollDirection;
    private double _edgePointer;

    /// <summary>
    /// The strip has no scroll arrows: where it continues, its edge fades. The wheel scrolls it, and an item dragged
    /// to a faded edge (to be dropped out of view) scrolls it after a moment, one step at a time. The mouse alone
    /// doesn't: the edges lie over items, which must not move under a pointer that rests on them.
    /// </summary>
    private void UpdateEdgeScroll(Point pointer)
    {
        _edgePointer = IsVertical ? pointer.Y : pointer.X;
        int direction = ScrollSnap.EdgeDirection(_edgePointer, ViewportLength, EdgeZone, ScrollOffset, ScrollableLength);
        if (direction == _edgeScrollDirection) return;
        _edgeScrollDirection = direction;
        _edgeScrollTimer?.Stop();
        if (direction == 0) return;
        _edgeScrollTimer ??= CreateEdgeScrollTimer();
        _edgeScrollTimer.Interval = TimeSpan.FromMilliseconds(450);
        _edgeScrollTimer.Start();
    }

    private void StopEdgeScroll()
    {
        _edgeScrollDirection = 0;
        _edgeScrollTimer?.Stop();
    }

    private DispatcherTimer CreateEdgeScrollTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += (_, _) =>
        {
            double offset = double.IsNaN(_scrollTarget) ? ScrollOffset : _scrollTarget;
            if (_closing || _edgeScrollDirection == 0
                || ScrollSnap.EdgeDirection(_edgePointer, ViewportLength, EdgeZone, offset, ScrollableLength) != _edgeScrollDirection)
            {
                StopEdgeScroll(); // reached the end
                return;
            }
            // Like the wheel: a popup open on an item stays with it.
            if (GlobalPopupDismissHook.HasActivePopupsOrMenus) return;
            SmoothScrollBy(_edgeScrollDirection * ViewportLength * 0.5);
            timer.Interval = TimeSpan.FromMilliseconds(650);
        };
        return timer;
    }

    private DispatcherTimer? _newAppHintTimer;
    private readonly DateTime _startedAt = DateTime.UtcNow;

    /// <summary>Marks the forward scroll arrow when a newly opened app was added outside the visible area.</summary>
    private void HintIfOutOfView(AppButton button)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_closing || ScrollableLength <= 0.5 || !button.IsLoaded || !Scroller.IsAncestorOf(button)) return;
            var bounds = button.TransformToAncestor(Scroller).TransformBounds(new Rect(button.RenderSize));
            double end = IsVertical ? bounds.Bottom : bounds.Right;
            double viewport = IsVertical ? Scroller.ViewportHeight : Scroller.ViewportWidth;
            if (end <= viewport + 1) return;

            NewAppHint.SetResourceReference(Shape.FillProperty, button.Group?.IsFlashing == true ? "AccentOrangeBrush" : "AccentBlueBrush");
            NewAppHint.Visibility = Visibility.Visible;
            _newAppHintTimer ??= CreateNewAppHintTimer();
            _newAppHintTimer.Stop();
            _newAppHintTimer.Start();
        });
    }

    private DispatcherTimer CreateNewAppHintTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) => HideNewAppHint();
        return timer;
    }

    private void HideNewAppHint()
    {
        _newAppHintTimer?.Stop();
        NewAppHint.Visibility = Visibility.Collapsed;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Scrolling toward the end reveals the new app.
        if (NewAppHint.Visibility == Visibility.Visible && (IsVertical ? e.VerticalChange : e.HorizontalChange) > 0)
            HideNewAppHint();

        UpdateFadeMask();
    }

    /// <summary>Softly fades edges for scrollable content.</summary>
    private void UpdateFadeMask()
    {
        double length = IsVertical ? Scroller.ActualHeight : Scroller.ActualWidth;
        if (ScrollableLength <= 0.5 || length <= 0)
        {
            Scroller.OpacityMask = null;
            return;
        }

        double fade = Math.Min(0.25, 28 / length);
        bool startFade = ScrollOffset > 0.5;
        bool endFade = ScrollOffset < ScrollableLength - 0.5;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = IsVertical ? new Point(0, 1) : new Point(1, 0),
        };
        brush.GradientStops.Add(new GradientStop(startFade ? Colors.Transparent : Colors.Black, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Black, fade));
        brush.GradientStops.Add(new GradientStop(Colors.Black, 1 - fade));
        brush.GradientStops.Add(new GradientStop(endFade ? Colors.Transparent : Colors.Black, 1));
        brush.Freeze();
        Scroller.OpacityMask = brush;
    }

    // ------------------------------------------------------------------ Context Menus & Click

    private void OnAnyContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindAncestor<TrayIconView>(e.OriginalSource as DependencyObject) is not null)
        {
            e.Handled = true;
            return;
        }
        if (e.Handled) return;
        var menuOwner = PopupPlacement.FindMenuOwner(e.OriginalSource as DependencyObject);
        // In edit mode the items' own menus stay closed; the dock's menu has the edit commands.
        if (_editing && menuOwner is not null && !ReferenceEquals(menuOwner, Root))
        {
            e.Handled = true;
            return;
        }
        if (menuOwner is { ContextMenu: { } menu } owner)
            PopupPlacement.PlaceMenu(menu, owner, _surface.Edge);
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var d = source; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d)
                 : LogicalTreeHelper.GetParent(d))
        {
            if (d is T match) return match;
        }
        return null;
    }

    private void OnPreviewMouseLeftButtonDownAnywhere(object sender, MouseButtonEventArgs e)
    {
        if (_openMenus.Count > 0)
        {
            var menus = _openMenus.ToList();
            foreach (var menu in menus)
                menu.IsOpen = false;
        }
    }

    private void OnContextMenuStateChanged(ContextMenu menu, bool open)
    {
        if (open)
        {
            _openMenus.Add(menu);
            GlobalPopupDismissHook.RegisterMenu(menu);
            BeginInteraction();
        }
        else if (_openMenus.Remove(menu))
        {
            GlobalPopupDismissHook.UnregisterMenu(menu);
            EndInteraction();
        }
    }

    private void OnDraggingChanged(bool dragging)
    {
        if (dragging) BeginInteraction();
        else EndInteraction();
    }
}
