namespace CustomDock.Dock;

/// <summary>
/// Where the dock's item strip stops when it scrolls: at the start of an item (or at the very end), so the item at the
/// leading edge is never cut in half. An item longer than the visible part gets stops inside it too, so all of it can be
/// seen. The math is the same along either axis.
/// </summary>
public static class ScrollSnap
{
    private const double Epsilon = 0.5;

    /// <summary>
    /// The scroll offset for a scroll from <paramref name="current"/> toward <paramref name="target"/>: the stop in that
    /// direction nearest to the target, never one behind <paramref name="current"/>. Until the target is halfway to the
    /// next stop the strip stays put, so the caller adds small steps (a touchpad) up until they move it.
    /// </summary>
    /// <param name="starts">Where each item begins along the strip (any order).</param>
    /// <param name="viewport">Visible length.</param>
    /// <param name="extent">Length of the whole strip.</param>
    public static double Snap(IReadOnlyList<double> starts, double viewport, double extent, double current, double target)
    {
        double max = Math.Max(0, extent - viewport);
        if (max <= Epsilon) return 0;
        current = Math.Clamp(current, 0, max);
        target = Math.Clamp(target, 0, max);
        int direction = Math.Sign(target - current);
        if (direction == 0) return current;

        var stops = Stops(starts, viewport, max);
        var ahead = direction > 0
            ? stops.Where(s => s > current + Epsilon).ToList()
            : stops.Where(s => s < current - Epsilon).OrderByDescending(s => s).ToList();
        if (ahead.Count == 0) return current;
        if (Math.Abs(target - current) < Math.Abs(ahead[0] - current) / 2) return current;
        // Nearest to the target; on a tie the one closer to where the strip is (the list runs outward).
        return ahead.OrderBy(s => Math.Abs(s - target)).First();
    }

    /// <summary>Item starts that can reach the leading edge, both ends, and steps inside items longer than the viewport.</summary>
    internal static List<double> Stops(IReadOnlyList<double> starts, double viewport, double max)
    {
        var stops = starts.Where(s => s > Epsilon && s < max - Epsilon).Append(0).Append(max).Distinct().OrderBy(s => s).ToList();
        double step = viewport * 0.8;
        if (step <= Epsilon) return stops;
        for (int i = 0; i < stops.Count - 1; i++)
        {
            if (stops[i + 1] - stops[i] <= viewport) continue;
            stops.Insert(i + 1, stops[i] + step);
        }
        return stops;
    }

    /// <summary>Whether a drag at <paramref name="position"/> (along the strip's visible part) rests in a faded edge that can scroll.</summary>
    /// <returns>-1 toward the start, +1 toward the end, 0 outside the edges or when there is nothing more that way.</returns>
    public static int EdgeDirection(double position, double viewport, double edge, double offset, double scrollable)
    {
        if (scrollable <= Epsilon || viewport <= 0) return 0;
        // The edges never cover more than the fade the dock draws (a quarter of the strip each).
        edge = Math.Min(edge, viewport * 0.25);
        if (position <= edge && offset > Epsilon) return -1;
        if (position >= viewport - edge && offset < scrollable - Epsilon) return 1;
        return 0;
    }
}
