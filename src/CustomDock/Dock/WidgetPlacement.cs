namespace CustomDock.Dock;

/// <summary>
/// Which dock shows a widget. Widgets keep their own state (timers, notes, alarms), so each one lives on exactly one
/// dock: the dock of the display it is assigned to while that display has a dock, otherwise the main dock.
/// </summary>
public static class WidgetPlacement
{
    /// <param name="itemDisplay">The widget's display (<see cref="Core.DockItem.Display"/>).</param>
    /// <param name="dockDevice">The dock's display; null for the main dock.</param>
    /// <param name="secondaryDocks">Displays that have (or are getting) a dock of their own.</param>
    public static bool ShowsOn(string? itemDisplay, string? dockDevice, IEnumerable<string> secondaryDocks)
    {
        bool onSecondary = itemDisplay is not null && secondaryDocks.Contains(itemDisplay, StringComparer.OrdinalIgnoreCase);
        return dockDevice is null
            ? !onSecondary
            : onSecondary && string.Equals(itemDisplay, dockDevice, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a dock shows an item. The top bar shows only the widgets placed on it; while the bar is off they show on
    /// the dock (on the main display). Other items and widgets follow the display rule above; items other than widgets
    /// show on every dock but the bar.
    /// </summary>
    /// <param name="barOn">Whether the top bar is on.</param>
    public static bool ShowsOn(Core.DockItemKind kind, string? itemSurface, string? itemDisplay, DockRole role, string? dockDevice,
        IEnumerable<string> secondaryDocks, bool barOn)
    {
        bool onBar = barOn && kind == Core.DockItemKind.Widget
            && string.Equals(itemSurface, Core.DockItem.BarSurface, StringComparison.OrdinalIgnoreCase);
        if (role == DockRole.Bar) return onBar;
        if (onBar) return false;
        if (kind != Core.DockItemKind.Widget) return true;
        // A bar widget while the bar is off: on the main dock, whatever display it had.
        if (string.Equals(itemSurface, Core.DockItem.BarSurface, StringComparison.OrdinalIgnoreCase)) return dockDevice is null;
        return ShowsOn(itemDisplay, dockDevice, secondaryDocks);
    }
}
