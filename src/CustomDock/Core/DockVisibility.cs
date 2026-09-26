namespace CustomDock.Core;

/// <summary>
/// Whether any dock is on screen. Services that only feed the dock's visuals (CPU, network speed) skip their
/// samples while every dock is hidden (auto-hide, full-screen apps), which saves wake-ups during games and videos.
/// </summary>
public static class DockVisibility
{
    private static readonly HashSet<object> Visible = new();

    /// <summary>True while at least one dock is shown (or before any dock reported).</summary>
    public static bool IsAnyDockVisible => Visible.Count > 0 || !_reported;

    private static bool _reported;

    public static void Report(object dock, bool shown)
    {
        _reported = true;
        if (shown) Visible.Add(dock);
        else Visible.Remove(dock);
    }
}
