using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Dock;

/// <summary>Where a dock that reserves no screen space itself (auto-hide) lays out while DockHub replaces the taskbar.</summary>
public static class DockArea
{
    /// <summary>
    /// The display's <paramref name="bounds"/> without the bands the other DockHub windows on it keep free (the dock's
    /// for the top bar, the bar's for the dock), so the two never cover each other.
    /// </summary>
    public static RECT Beside(RECT bounds, IEnumerable<(DockEdge Edge, RECT Band)> reserved)
    {
        var area = bounds;
        foreach (var (edge, band) in reserved)
        {
            switch (edge)
            {
                case DockEdge.Top: area.Top = Math.Max(area.Top, band.Bottom); break;
                case DockEdge.Left: area.Left = Math.Max(area.Left, band.Right); break;
                case DockEdge.Right: area.Right = Math.Min(area.Right, band.Left); break;
                default: area.Bottom = Math.Min(area.Bottom, band.Top); break;
            }
        }
        return area;
    }
}
