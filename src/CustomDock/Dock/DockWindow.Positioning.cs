using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using ManagedShell.AppBar;
using Microsoft.Win32;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

public partial class DockWindow
{
    /// <summary>
    /// Scaled content thickness in DIP. Whole numbers keep the scaled layout on a clean grid;
    /// the bar is always sized from this so the scaled content is never squeezed or clipped.
    /// </summary>
    /// <summary>The main dock uses the general size; docks on other displays may have their own.</summary>
    private DockSize EffectiveSize => IsMain ? _surface.Size : _config.DisplaySizeOf(_monitor.DeviceName) ?? _surface.Size;

    private double ContentDip => EffectiveSize switch
    {
        DockSize.Small => 40,
        DockSize.Large => 54,
        _ => BaseContent,
    };

    private double Scale => ContentDip / BaseContent;

    /// <summary>Bar thickness: scaled content plus the unscaled zone margins.</summary>
    private double ThicknessDip => ContentDip + ZonesMargin * 2;

    private double MarginDip => _surface.Layout == DockLayout.Floating ? _surface.EdgeMargin : 0;

    private void ApplyOrientation()
    {
        bool vertical = IsVertical;
        var orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        Zones.Orientation = orientation;
        StartZone.Orientation = orientation;
        EndZone.Orientation = orientation;
        ItemsPanel.Orientation = orientation;
        Scroller.HorizontalScrollBarVisibility = vertical ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        Scroller.VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
        Scroller.PanningMode = vertical ? PanningMode.VerticalOnly : PanningMode.HorizontalOnly;
        StartSeparator.SetOrientation(vertical);
        EndSeparator.SetOrientation(vertical);
        _runningSeparator.SetOrientation(vertical);
        NewAppHint.HorizontalAlignment = HorizontalAlignment.Right;
        NewAppHint.VerticalAlignment = vertical ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        NewAppHint.Margin = vertical ? new Thickness(0, 0, 4, 2) : new Thickness(0, 4, 2, 0);

        bool center = _surface.Alignment == DockAlignment.Center;
        ItemsPanel.HorizontalAlignment = vertical ? HorizontalAlignment.Center : center ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        ItemsPanel.VerticalAlignment = !vertical ? VerticalAlignment.Center : center ? VerticalAlignment.Center : VerticalAlignment.Top;

        ClockDate.Visibility = _config.ClockShowDate && !vertical ? Visibility.Visible : Visibility.Collapsed;
        ClockButton.Width = vertical ? 44 : double.NaN;
        ClockButton.Padding = vertical ? new Thickness(0) : new Thickness(9, 0, 9, 0);
        ClockTime.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Right;

        var showDesktop = ShowDesktopButton;
        showDesktop.Width = vertical ? 46 : 12;
        showDesktop.Height = vertical ? 12 : 46;
        showDesktop.LayoutTransform = vertical ? new RotateTransform(90) : Transform.Identity;

        EdgeHighlight.BorderThickness = _surface.Edge switch
        {
            DockEdge.Top => new Thickness(0, 0, 0, 1),
            DockEdge.Left => new Thickness(0, 0, 1, 0),
            DockEdge.Right => new Thickness(1, 0, 0, 0),
            _ => new Thickness(0, 1, 0, 0),
        };
    }

    private void ApplyZoneVisibility()
    {
        StartButton.Visibility = _config.ShowStartButton ? Visibility.Visible : Visibility.Collapsed;
        SearchButton.Visibility = _config.ShowSearchButton ? Visibility.Visible : Visibility.Collapsed;
        TaskViewButton.Visibility = _config.ShowTaskViewButton ? Visibility.Visible : Visibility.Collapsed;
        StartSeparator.Visibility = _config.ShowStartButton || _config.ShowSearchButton || _config.ShowTaskViewButton
            ? Visibility.Visible : Visibility.Collapsed;

        bool tray = HasTray;
        PinnedTray.Visibility = tray ? Visibility.Visible : Visibility.Collapsed;
        NetworkStatusIcon.Visibility = tray && _config.ShowNetworkIcon ? Visibility.Visible : Visibility.Collapsed;
        VolumeStatusIcon.Visibility = tray && _config.ShowVolumeIcon ? Visibility.Visible : Visibility.Collapsed;
        BatteryStatusIcon.Visibility = tray && _config.ShowBatteryIcon && BatteryStatusIconView.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        ClockButton.Visibility = _config.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        ShowDesktopButton.Visibility = _config.ShowDesktopButton ? Visibility.Visible : Visibility.Collapsed;
        UpdateTrayVisibility();
    }

    /// <summary>Tray icons live on the main dock only (like the Windows taskbar on secondary displays).</summary>
    private bool HasTray => IsMain && _config.ShowTray && _shell.Tray is not null;

    private void UpdateTrayVisibility()
    {
        bool tray = HasTray;
        bool hasHidden = tray && _shell.Tray!.UnpinnedIcons is { IsEmpty: false };
        TrayOverflowButton.Visibility = hasHidden ? Visibility.Visible : Visibility.Collapsed;
        EndSeparator.Visibility = tray || _config.ShowClock ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyBackdrop()
    {
        if (_hwnd == IntPtr.Zero) return;
        bool floating = _surface.Layout == DockLayout.Floating;
        var tint = (TryFindResource("DockTintBrush") as SolidColorBrush)?.Color ?? Colors.Black;

        // Contrast themes need an opaque background behind the system text colors.
        if (_surface.Backdrop == BackdropKind.Solid || ThemeManager.IsHighContrast)
        {
            TintLayer.SetResourceReference(Border.BackgroundProperty, "DockSolidBrush");
            TintLayer.Opacity = 1;
        }
        else
        {
            TintLayer.SetResourceReference(Border.BackgroundProperty, "DockTintBrush");
            TintLayer.Opacity = _surface.TintOpacity;
        }

        WindowEffects.ApplyDockBackdrop(_hwnd, _surface.Backdrop, Color.FromArgb((byte)(_surface.TintOpacity * 255), tint.R, tint.G, tint.B));
        WindowEffects.SetDarkMode(_hwnd, ThemeManager.IsDark);
        WindowEffects.SetCornerPreference(_hwnd, floating ? 2 : 1);
        WindowEffects.SetBorderColor(_hwnd, floating ? (TryFindResource("DockBorderColor") as Color?) : null);
        EdgeHighlight.Visibility = ThemeManager.IsDark ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnThemeChanged()
    {
        ApplyBackdrop();
        foreach (var view in _itemViews.Values.OfType<WidgetItemView>())
            view.HoverEnabled = _config.HoverEffect;
    }

    /// <summary>Band the reserver keeps free: the bar plus its floating margins, in DIP.</summary>
    private double ReservedThicknessDip => ThicknessDip + 2 * MarginDip;

    private void UpdateReserver()
    {
        bool needed = !_surface.AutoHide && !_closing;
        // A new display or edge needs a new AppBar; size and DPI changes only move the existing one.
        var key = (_monitor.DeviceName, _surface.Edge, _monitor.Bounds);

        if (needed && _reserver is not null && _reserverKey == key)
        {
            _reserver.Update(ReservedThicknessDip, _monitor.DpiScale);
            return;
        }

        if (_reserver is not null)
        {
            _reserver.RectChanged -= QueueReposition;
            _reserver.CloseReserver();
            _reserver = null;
            _reserverKey = null;
        }

        if (!needed) return;

        var edge = _surface.Edge switch
        {
            DockEdge.Top => AppBarEdge.Top,
            DockEdge.Left => AppBarEdge.Left,
            DockEdge.Right => AppBarEdge.Right,
            _ => AppBarEdge.Bottom,
        };

        try
        {
            _reserver = new SpaceReserver(_shell.Manager, ToAppBarScreen(_monitor), edge, ReservedThicknessDip, _monitor.DpiScale);
            _reserver.RectChanged += QueueReposition;
            _reserver.Show();
            _reserverKey = key;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to reserve screen area");
            _reserver = null;
        }
    }

    /// <summary>Built from the dock's own monitor data so the AppBar and the dock always agree on the display.</summary>
    private static AppBarScreen ToAppBarScreen(MonitorInfo monitor) => new()
    {
        Bounds = new System.Drawing.Rectangle(monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Width, monitor.Bounds.Height),
        WorkingArea = new System.Drawing.Rectangle(monitor.WorkArea.Left, monitor.WorkArea.Top, monitor.WorkArea.Width, monitor.WorkArea.Height),
        DeviceName = monitor.DeviceName,
        Primary = monitor.IsPrimary,
        HMonitor = monitor.Handle,
    };

    /// <summary>Checks once the layout has settled that Windows really keeps maximized windows out of the dock's band.</summary>
    private void ScheduleReservationCheck()
    {
        if (_reserver is null || _closing) return;
        if (_reservationCheckTimer is null)
        {
            _reservationCheckTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.5) };
            _reservationCheckTimer.Tick += (_, _) =>
            {
                _reservationCheckTimer?.Stop();
                CheckReservation();
            };
        }
        _reservationCheckTimer.Stop();
        _reservationCheckTimer.Start();
    }

    private void CheckReservation()
    {
        // A full-screen app legitimately covers the whole display.
        if (_closing || _fullscreen || _reserver is not { Handle: not 0 } reserver) return;
        if (MonitorHelper.TryGet(_monitor.Handle) is { } monitor && reserver.Verify(monitor))
            ScheduleReservationCheck();
    }

    private void QueueReposition()
    {
        if (_repositionQueued || _closing) return;
        _repositionQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _repositionQueued = false;
            Reposition();
        });
    }

    private double DesiredLengthPx(double dpi)
    {
        var zonesMargin = Zones.Margin;
        // The constraint is in the (scaled) window space, so it must be the scaled content thickness;
        // measuring with the unscaled 46 DIP squeezes Large content and yields a wrong length.
        Zones.Measure(IsVertical
            ? new Size(ContentDip, double.PositiveInfinity)
            : new Size(double.PositiveInfinity, ContentDip));
        double length = IsVertical
            ? Zones.DesiredSize.Height + zonesMargin.Top + zonesMargin.Bottom
            : Zones.DesiredSize.Width + zonesMargin.Left + zonesMargin.Right;
        return Math.Ceiling(length * dpi);
    }

    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero || _closing) return;

        _monitor = ResolveMonitor();
        double dpi = _monitor.DpiScale;
        int barPx = (int)Math.Round(ThicknessDip * dpi);
        int marginPx = (int)Math.Round(MarginDip * dpi);
        // Keep the reserved band in step with the dock (DPI changes do not always reach the AppBar).
        _reserver?.Update(ReservedThicknessDip, dpi);

        RECT area;
        if (_reserver is { Handle: not 0 } reserver && reserver.Rect.Width > 0 && reserver.Rect.Height > 0)
            area = reserver.Rect;
        else
            area = _shell.IsReplacingTaskbar ? _monitor.Bounds : _monitor.WorkArea;

        bool vertical = IsVertical;
        int maxLength = (vertical ? area.Height : area.Width) - 2 * marginPx;
        int length = _surface.WidthMode == DockWidthMode.Full
            ? maxLength
            : (int)Math.Min(maxLength, DesiredLengthPx(dpi));

        _shownRect = _surface.Edge switch
        {
            DockEdge.Top => Rect(area.Left + (area.Width - length) / 2, area.Top + marginPx, length, barPx),
            DockEdge.Left => Rect(area.Left + marginPx, area.Top + (area.Height - length) / 2, barPx, length),
            DockEdge.Right => Rect(area.Right - marginPx - barPx, area.Top + (area.Height - length) / 2, barPx, length),
            _ => Rect(area.Left + (area.Width - length) / 2, area.Bottom - marginPx - barPx, length, barPx),
        };

        if (!_animating && _shown)
            MoveTo(_shownRect);

        UpdateTrigger();
        // Only the dock the flyouts belong to may move them (the main dock unless one was just used).
        if (s_trayHostOwner is null ? IsMain : s_trayHostOwner == this)
            UpdateTrayHost();
        UpdateFadeMask();
        ScheduleReservationCheck();
    }

    private static RECT Rect(int x, int y, int w, int h) => new(x, y, x + w, y + h);

    private void MoveTo(RECT r)
        => SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);

    private RECT HiddenRect()
    {
        var b = _monitor.Bounds;
        var r = _shownRect;
        return _surface.Edge switch
        {
            DockEdge.Top => new RECT(r.Left, b.Top - r.Height - 2, r.Right, b.Top - 2),
            DockEdge.Left => new RECT(b.Left - r.Width - 2, r.Top, b.Left - 2, r.Bottom),
            DockEdge.Right => new RECT(b.Right + 2, r.Top, b.Right + 2 + r.Width, r.Bottom),
            _ => new RECT(r.Left, b.Bottom + 2, r.Right, b.Bottom + 2 + r.Height),
        };
    }

    /// <summary>Start menu, quick settings, and notifications position relative to this rectangle.</summary>
    private void UpdateTrayHost()
    {
        if (_hwnd == IntPtr.Zero || _shownRect.Width <= 0) return;
        s_trayHostOwner = this;
        _shell.SetTrayHost(_shownRect, _surface.Edge);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            _monitor = ResolveMonitor();
            // The reserver no longer handles display changes itself; keep ManagedShell's screen cache fresh.
            _shell.Manager.FullScreenHelper.NotifyScreensChanged();
            ApplyBackdrop();
            UpdateReserver();
            QueueReposition();
            RefreshMissingIcons();
        });

    private static readonly HashSet<string> MenuWindowClasses = new(StringComparer.Ordinal)
    {
        "#32768", "tooltips_class32", "SysShadow", "Xaml_WindowedPopupClass", "DropDown", "Chrome_WidgetWin_2",
    };

    private void ReassertTopmost()
    {
        if (_closing || _fullscreen) return;
        // Bringing dock to front while a menu/panel is open causes them to end up underneath.
        if (_openMenus.Count > 0 || _interactionCount > 0) return;
        const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER;
        if (_shown && IsVisible && IsCoveredByForeignWindow())
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, flags);
        if (_trigger is { IsVisible: true })
            SetWindowPos(_trigger.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
    }

    /// <summary>
    /// Scans windows above the dock: do nothing if it belongs to us or is a menu/tooltip window;
    /// returns true only if another application's window covers the dock.
    /// </summary>
    private bool IsCoveredByForeignWindow()
    {
        if (!GetWindowRect(_hwnd, out var dock)) return false;
        int guard = 0;
        for (IntPtr h = NativeMethods.GetWindow(_hwnd, GW_HWNDPREV); h != IntPtr.Zero && guard++ < 256; h = NativeMethods.GetWindow(h, GW_HWNDPREV))
        {
            if (!IsWindowVisible(h) || IsCloaked(h)) continue;
            if (!GetWindowRect(h, out var r)) continue;
            if (r.Right <= dock.Left || r.Left >= dock.Right || r.Bottom <= dock.Top || r.Top >= dock.Bottom) continue;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == _processId) return false;
            if (MenuWindowClasses.Contains(GetClassName(h))) return false;
            return true;
        }
        return false;
    }
}
