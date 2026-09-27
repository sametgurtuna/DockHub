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
}
