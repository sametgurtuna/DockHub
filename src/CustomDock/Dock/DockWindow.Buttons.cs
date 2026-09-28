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
        (TrayOverflowPopup.Placement, TrayOverflowPopup.HorizontalOffset, TrayOverflowPopup.VerticalOffset) = _config.Edge switch
        {
            DockEdge.Top => (PlacementMode.Bottom, -60.0, 10.0),
            DockEdge.Left => (PlacementMode.Right, 10.0, 0.0),
            DockEdge.Right => (PlacementMode.Left, -10.0, 0.0),
            _ => (PlacementMode.Top, -60.0, -10.0),
        };
        BeginInteraction();
        GlobalPopupDismissHook.RegisterPopup(TrayOverflowPopup);
        PopupAnimationHelper.AnimateOpen(TrayOverflowPopup, _config.Edge, TrayOverflowButton);
    }

    private void OnTrayOverflowUnchecked(object sender, RoutedEventArgs e)
    {
        if (TrayOverflowPopup.IsOpen && !PopupAnimationHelper.IsClosing(TrayOverflowPopup))
        {
            PopupAnimationHelper.ClosePopup(TrayOverflowPopup, _config.Edge, TrayOverflowButton);
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
        if (AppServices.ConfigService.History.Latest is { } last)
        {
            menu.Items.Add(DockMenu.Item(L.T("Undo: {0}", last.Description), "\uE7A7", () => AppServices.ConfigService.Undo()));
            menu.Items.Add(DockMenu.Separator());
        }
        menu.Items.Add(DockMenu.Item("Add widget…", "\uE710", () => App.Instance.ShowSettings("gallery")));
        menu.Items.Add(DockMenu.Item("Pin application…", "\uE718", () => App.Instance.ShowAppPicker()));
        menu.Items.Add(DockMenu.Item("Add separator", "\uE76F", () => AppServices.ConfigService.AddItem(DockItem.Separator())));
        menu.Items.Add(DockMenu.Item("Create group", "\uE8B7", () => AppServices.ConfigService.AddItem(DockItem.Group(L.T("New group")))));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("Task Manager", "\uE9D9", () => Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true })));
        menu.Items.Add(DockMenu.Item("Windows Settings", "\uE770", () => Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true })));
        menu.Items.Add(DockMenu.Item("Quick settings", "\uE9E9", OpenQuickSettings));
        menu.Items.Add(DockMenu.Check("Auto-hide", _config.AutoHide, () => _config.AutoHide = !_config.AutoHide));
        menu.Items.Add(DockMenu.Check("Hide Windows taskbar", _config.TaskbarMode == TaskbarMode.Replace,
            () => _config.TaskbarMode = _config.TaskbarMode == TaskbarMode.Replace ? TaskbarMode.ShowBoth : TaskbarMode.Replace));
        if (AppServices.Profiles.Profiles.Count > 1)
            menu.Items.Add(DockMenu.Submenu(L.T("Profile"), "\uE77B", AppServices.Profiles.Profiles.Select(p =>
                DockMenu.Check(p.Name, p.Id == _config.ActiveProfileId, () => AppServices.Profiles.SwitchTo(p.Id))).ToList()));
        menu.Items.Add(DockMenu.Submenu("Position", "\uE8A0", new[]
        {
            DockMenu.Check("Bottom", _config.Edge == DockEdge.Bottom, () => _config.Edge = DockEdge.Bottom),
            DockMenu.Check("Top", _config.Edge == DockEdge.Top, () => _config.Edge = DockEdge.Top),
            DockMenu.Check("Left", _config.Edge == DockEdge.Left, () => _config.Edge = DockEdge.Left),
            DockMenu.Check("Right", _config.Edge == DockEdge.Right, () => _config.Edge = DockEdge.Right),
        }));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("DockHub settings…", "\uE713", () => App.Instance.ShowSettings()));
        menu.Items.Add(DockMenu.Item("Exit", "\uE7E8", () => App.Instance.ExitApplication()));
    }
}
