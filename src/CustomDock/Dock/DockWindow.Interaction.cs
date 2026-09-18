using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    }

    private void OnLauncherVisibilityChanged(bool visible)
    {
        if (!_config.AutoHide) return;
        if (visible) Reveal();
        else ScheduleAutoHide();
    }

    private bool IsFullscreenBlocked => _config.HideOnFullscreen && _fullscreen;

    private void UpdateVisibility(bool animate)
    {
        bool shouldShow = !IsFullscreenBlocked && (!_config.AutoHide || _revealed);
        if (shouldShow == _shown && (_animating || IsVisible == shouldShow)) return;
        _shown = shouldShow;
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
                // Açılış: uzun yavaşlama (ease-out quint); kapanış: hızlanarak çıkış
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

    /// <summary>Bir sonraki ekran karesini bekler (animasyonu ekran yenilemesiyle eşzamanlar).</summary>
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
        if (!_config.AutoHide || _interactionCount > 0 || _closing) return;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void TryAutoHide()
    {
        if (!_config.AutoHide || _interactionCount > 0 || !_shown) return;
        if (_shell.IsLauncherVisible || IsCursorOverDock()) return;
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
        bool active = _config.AutoHide && !IsFullscreenBlocked && !_shown && !_closing;
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
        RECT rect = _config.Edge switch
        {
            DockEdge.Top => new RECT(dock.Left, b.Top, dock.Right, b.Top + TriggerThickness),
            DockEdge.Left => new RECT(b.Left, dock.Top, b.Left + TriggerThickness, dock.Bottom),
            DockEdge.Right => new RECT(b.Right - TriggerThickness, dock.Top, b.Right, dock.Bottom),
            _ => new RECT(dock.Left, b.Bottom - TriggerThickness, dock.Right, b.Bottom),
        };

        _trigger.SetActive(true);
        _trigger.Place(rect);
    }

    // ------------------------------------------------------------------ Kaydırma

    private double ScrollOffset => IsVertical ? Scroller.VerticalOffset : Scroller.HorizontalOffset;

    private double ScrollableLength => IsVertical ? Scroller.ScrollableHeight : Scroller.ScrollableWidth;

    private double ViewportLength => IsVertical ? Scroller.ViewportHeight : Scroller.ViewportWidth;

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Menü veya açılır pencere (mikser, takvim vb.) açıkken dock'u yatay kaydırma
        if (GlobalPopupDismissHook.HasActivePopupsOrMenus) return;

        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is TextBox { IsKeyboardFocusWithin: true } box && box.ExtentHeight > box.ViewportHeight) return;
            // Ses widget'ı fare tekerleğini kendisi kullanır (ses seviyesi ayarı)
            if (d is Widgets.AudioWidget) return;
            if (ReferenceEquals(d, Scroller)) break;
        }

        if (ScrollableLength <= 0) return;
        e.Handled = true;
        SmoothScrollBy(-e.Delta * 0.9);
    }

    private void SmoothScrollBy(double delta)
    {
        double from = double.IsNaN(_scrollTarget) ? ScrollOffset : _scrollTarget;
        _scrollTarget = Math.Clamp(from + delta, 0, ScrollableLength);
        if (_scrollAnimating) return;
        _scrollAnimating = true;
        _lastScrollFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnScrollFrame;
    }

    private void OnScrollFrame(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        double dt = _lastScrollFrame == TimeSpan.Zero ? 1 / 60.0 : (time - _lastScrollFrame).TotalSeconds;
        if (dt <= 0) return; // aynı kare için ikinci çağrı
        _lastScrollFrame = time;
        dt = Math.Min(dt, 0.05);

        double current = ScrollOffset;
        double diff = _scrollTarget - current;
        // Kritik sönümlü yaklaşım: ~120 ms'de hedefe yerleşir
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

    private void OnScrollBackClick(object sender, RoutedEventArgs e) => SmoothScrollBy(-ViewportLength * 0.6);

    private void OnScrollForwardClick(object sender, RoutedEventArgs e) => SmoothScrollBy(ViewportLength * 0.6);

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        bool scrollable = ScrollableLength > 0.5;
        var visibility = scrollable ? Visibility.Visible : Visibility.Collapsed;
        if (ScrollBackButton.Visibility != visibility)
        {
            ScrollBackButton.Visibility = ScrollForwardButton.Visibility = visibility;
            UpdateTrayVisibility();
        }
        ScrollBackButton.IsEnabled = ScrollOffset > 0.5;
        ScrollForwardButton.IsEnabled = ScrollOffset < ScrollableLength - 0.5;
        UpdateFadeMask();
    }

    /// <summary>Kaydırılabilir içerikte kenarları yumuşakça soldurur.</summary>
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

    // ------------------------------------------------------------------ Bağlam menüleri & Tıklama

    private void OnAnyContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindAncestor<TrayIconView>(e.OriginalSource as DependencyObject) is not null)
        {
            e.Handled = true;
            return;
        }
        if (e.Handled) return;
        if (PopupPlacement.FindMenuOwner(e.OriginalSource as DependencyObject) is { ContextMenu: { } menu } owner)
            PopupPlacement.PlaceMenu(menu, owner, _config.Edge);
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
