using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CustomDock.Core;

namespace CustomDock.Dock;

public partial class DockWindow
{
    // ------------------------------------------------------------------ Buttons

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        UpdateTrayHost();
        _shell.ShowStartMenu(_hwnd);
    }

    private void OnStartRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        UpdateTrayHost();
        _shell.ShowStartContextMenu();
    }

    private void OnSearchClick(object sender, RoutedEventArgs e)
    {
        if (_config.SearchButtonAction == SearchButtonAction.Launcher)
        {
            LauncherWindow.Toggle();
            return;
        }
        UpdateTrayHost();
        _shell.ShowSearch();
    }

    private void OnTaskViewClick(object sender, RoutedEventArgs e) => _shell.ShowTaskView();

    /// <summary>
    /// Opens Windows' quick settings next to this dock. Windows places (and on some builds only shows) the panel
    /// against the tray host rectangle, so it is refreshed first, as for the Start menu and the clock.
    /// </summary>
    internal void OpenQuickSettings()
    {
        UpdateTrayHost();
        _shell.ShowQuickSettings();
    }

    private void OnClockClick(object sender, RoutedEventArgs e)
    {
        UpdateTrayHost();
        _shell.ShowNotificationCenter();
    }

    private void OnShowDesktopClick(object sender, RoutedEventArgs e) => _shell.ToggleDesktop();

    private void OnClockMenuOpening(object sender, ContextMenuEventArgs e)
    {
        UpdateTrayHost();
        var menu = ClockButton.ContextMenu;
        menu.Items.Clear();
        menu.Items.Add(DockMenu.Item("Notification center", "\uE91C", _shell.ShowNotificationCenter));
        menu.Items.Add(DockMenu.Item("Quick settings", "\uE9E9", OpenQuickSettings));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("Adjust date and time", "\uE787",
            () => Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true })));
        menu.Items.Add(DockMenu.Check("Show seconds", _config.ClockShowSeconds, () => _config.ClockShowSeconds = !_config.ClockShowSeconds));
        menu.Items.Add(DockMenu.Check("Show date", _config.ClockShowDate, () => _config.ClockShowDate = !_config.ClockShowDate));
    }

    private void OnTrayOverflowChecked(object sender, RoutedEventArgs e)
    {
        UpdateTrayHost();
        (TrayOverflowPopup.Placement, TrayOverflowPopup.HorizontalOffset, TrayOverflowPopup.VerticalOffset) = _surface.Edge switch
        {
            DockEdge.Top => (PlacementMode.Bottom, -60.0, 10.0),
            DockEdge.Left => (PlacementMode.Right, 10.0, 0.0),
            DockEdge.Right => (PlacementMode.Left, -10.0, 0.0),
            _ => (PlacementMode.Top, -60.0, -10.0),
        };
        BeginInteraction();
        GlobalPopupDismissHook.RegisterPopup(TrayOverflowPopup);
        PopupAnimationHelper.AnimateOpen(TrayOverflowPopup, _surface.Edge, TrayOverflowButton);
    }

    private void OnTrayOverflowUnchecked(object sender, RoutedEventArgs e)
    {
        if (TrayOverflowPopup.IsOpen && !PopupAnimationHelper.IsClosing(TrayOverflowPopup))
        {
            PopupAnimationHelper.ClosePopup(TrayOverflowPopup, _surface.Edge, TrayOverflowButton);
        }
    }

    private void OnTrayOverflowClosed(object? sender, EventArgs e)
    {
        TrayOverflowButton.IsChecked = false;
        EndInteraction();
    }

    // ------------------------------------------------------------------ Dock menu

    private void OnDockMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Root has its own ContextMenu; when right-clicking a child element without a ContextMenu (like a tray icon),
        // default behavior raises this event on the nearest owner (Root). We must check if original source is a tray icon.
        if (FindAncestor<TrayIconView>(e.OriginalSource as DependencyObject) is not null)
        {
            e.Handled = true;
            return;
        }
        var menu = Root.ContextMenu;
        menu.Items.Clear();
        if (_editing)
        {
            menu.Items.Add(DockMenu.Item("Done", "\uE73E", () => DockEditMode.Exit()));
            menu.Items.Add(DockMenu.Item(IsBar ? "Add to the top bar…" : "Add to the dock…", "\uE710", ToggleAddPicker));
            return;
        }
        if (AppServices.ConfigService.History.CanUndo && AppServices.ConfigService.History.Latest is { } last)
        {
            menu.Items.Add(DockMenu.Item(L.T("Undo: {0}", last.Description), "\uE7A7", () => AppServices.ConfigService.Undo()));
            menu.Items.Add(DockMenu.Separator());
        }
        if (IsBar)
        {
            AddBarMenuItems(menu);
            return;
        }
        menu.Items.Add(DockMenu.Item("Edit dock", "\uE70F", () => EnterEditMode()));
        menu.Items.Add(DockMenu.Item("Add widget…", "\uE710", () => App.Instance.ShowSettings("gallery")));
        menu.Items.Add(DockMenu.Item("Pin application…", "\uE718", () => App.Instance.ShowAppPicker()));
        menu.Items.Add(DockMenu.Item("Add separator", "\uE76F", () => AppServices.ConfigService.AddItem(DockItem.Separator())));
        menu.Items.Add(DockMenu.Item("Create group", "\uE8B7", () => AppServices.ConfigService.AddItem(DockItem.Group(L.T("New group")))));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("Task Manager", "\uE9D9", () => Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true })));
        menu.Items.Add(DockMenu.Item("Windows Settings", "\uE770", () => Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true })));
        menu.Items.Add(DockMenu.Item("Quick settings", "\uE9E9", OpenQuickSettings));
        menu.Items.Add(DockMenu.Check("Auto-hide", _surface.AutoHide, () => _surface.AutoHide = !_surface.AutoHide));
        menu.Items.Add(DockMenu.Check("Hide Windows taskbar", _config.TaskbarMode == TaskbarMode.Replace,
            () => _config.TaskbarMode = _config.TaskbarMode == TaskbarMode.Replace ? TaskbarMode.ShowBoth : TaskbarMode.Replace));
        if (AppServices.Profiles.Profiles.Count > 1)
            menu.Items.Add(DockMenu.Submenu(L.T("Profile"), "\uE77B", AppServices.Profiles.Profiles.Select(p =>
                DockMenu.Check(p.Name, p.Id == _config.ActiveProfileId, () => AppServices.Profiles.SwitchTo(p.Id))).ToList()));
        menu.Items.Add(DockMenu.Submenu("Position", "\uE8A0", new[]
        {
            DockMenu.Check("Bottom", _surface.Edge == DockEdge.Bottom, () => _surface.Edge = DockEdge.Bottom),
            DockMenu.Check("Top", _surface.Edge == DockEdge.Top, () => _surface.Edge = DockEdge.Top),
            DockMenu.Check("Left", _surface.Edge == DockEdge.Left, () => _surface.Edge = DockEdge.Left),
            DockMenu.Check("Right", _surface.Edge == DockEdge.Right, () => _surface.Edge = DockEdge.Right),
        }));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("DockHub settings…", "\uE713", () => App.Instance.ShowSettings()));
        menu.Items.Add(DockMenu.Item("Exit", "\uE7E8", () => App.Instance.ExitApplication()));
    }

    /// <summary>The top bar's menu: its widgets, its own hiding, clock and edge, and turning it off.</summary>
    private void AddBarMenuItems(ContextMenu menu)
    {
        var bar = _config.TopBar;
        menu.Items.Add(DockMenu.Item("Edit top bar", "\uE70F", () => EnterEditMode()));
        // The edit mode's picker, which adds to the bar (the gallery adds to the dock).
        menu.Items.Add(DockMenu.Item("Add widget…", "\uE710", () =>
        {
            EnterEditMode();
            if (_editing) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, ToggleAddPicker);
        }));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Check("Auto-hide", _surface.AutoHide, () => _surface.AutoHide = !_surface.AutoHide));
        menu.Items.Add(DockMenu.Check("Show clock", bar.ShowClock, () => bar.ShowClock = !bar.ShowClock));
        // The dock's edge isn't offered: the bar can't share it.
        var edges = new[] { (DockEdge.Top, "Top"), (DockEdge.Bottom, "Bottom"), (DockEdge.Left, "Left"), (DockEdge.Right, "Right") }
            .Where(e => e.Item1 != _config.Edge)
            .Select(e => DockMenu.Check(e.Item2, _surface.Edge == e.Item1, () => _surface.Edge = e.Item1))
            .ToArray();
        menu.Items.Add(DockMenu.Submenu("Position", "\uE8A0", edges));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("Top bar settings…", "\uE713", () => App.Instance.ShowSettings("appearance")));
        menu.Items.Add(DockMenu.Item("Turn off the top bar", "\uE711", () => bar.Enabled = false));
    }
}
