using CustomDock.Dock;

namespace CustomDock.Tests;

public class ScrollSnapTests
{
    // Ten 50-wide items (a strip 500 long) in a 180 window: the last stop is 320.
    private static readonly double[] Starts = Enumerable.Range(0, 10).Select(i => i * 50.0).ToArray();
    private const double Viewport = 180, Extent = 500;

    [Theory]
    [InlineData(0, 70, 50)]     // a wheel step lands on the item start nearest to it
    [InlineData(0, 80, 100)]
    [InlineData(100, 20, 0)]    // back to the beginning
    [InlineData(250, 400, 320)] // past the end: the end, where the last item ends at the edge
    [InlineData(300, 310, 320)] // halfway to the end counts
    public void Scrolling_stops_at_item_starts(double current, double target, double expected)
        => Assert.Equal(expected, ScrollSnap.Snap(Starts, Viewport, Extent, current, target));

    [Theory]
    [InlineData(50, 60, 50)]  // not halfway to the next item: stays (the dock adds up small touchpad steps)
    [InlineData(50, 75, 100)]
    [InlineData(50, 40, 50)]
    [InlineData(50, 25, 0)]
    [InlineData(0, -30, 0)]   // nothing before the start
    [InlineData(320, 330, 320)]
    public void Small_steps_move_once_they_are_halfway(double current, double target, double expected)
        => Assert.Equal(expected, ScrollSnap.Snap(Starts, Viewport, Extent, current, target));

    [Fact]
    public void A_scroll_never_goes_the_other_way()
    {
        // Left between stops (keyboard focus brought an item into view): forward stays forward.
        double[] starts = { 0, 256, 300 };
        double result = ScrollSnap.Snap(starts, 100, 400, 20, 128);
        Assert.True(result > 20, $"moved back to {result}");
        Assert.True(ScrollSnap.Snap(starts, 100, 400, 170, 120) < 170);
    }

    [Fact]
    public void An_item_longer_than_the_view_can_be_seen_whole()
    {
        // A 278-long note card in a 240 window gets a stop inside it, so its end comes into view.
        double[] starts = { 0, 278, 320 };
        Assert.Equal(new[] { 0d, 192, 278, 320 }, ScrollSnap.Stops(starts, 240, 320));
        Assert.Equal(192, ScrollSnap.Snap(starts, 240, 560, 0, 108));
    }

    [Fact]
    public void A_strip_that_fits_does_not_scroll()
    {
        Assert.Equal(0, ScrollSnap.Snap(Starts, 600, Extent, 0, 100));
        Assert.Equal(0, ScrollSnap.Snap(Array.Empty<double>(), 180, 180, 0, 50));
    }

    [Fact]
    public void Items_of_different_lengths_work_the_same_on_a_vertical_dock()
    {
        // Tiles and cards stacked on a side dock: 0, 46, 92 (a 120-long card), 212, 258.
        double[] starts = { 258, 0, 92, 46, 212 };
        Assert.Equal(92, ScrollSnap.Snap(starts, 200, 304, 46, 80));
        Assert.Equal(104, ScrollSnap.Snap(starts, 200, 304, 92, 200)); // the end (304 - 200)
        Assert.Equal(46, ScrollSnap.Snap(starts, 200, 304, 92, 60));
    }

    [Theory]
    [InlineData(10, 100, -1)]  // start edge, scrolled
    [InlineData(10, 0, 0)]     // start edge, nothing before
    [InlineData(175, 100, 1)]  // end edge
    [InlineData(175, 320, 0)]  // end edge, at the end
    [InlineData(90, 100, 0)]   // middle
    public void The_faded_edges_scroll_only_where_there_is_more(double position, double offset, int expected)
        => Assert.Equal(expected, ScrollSnap.EdgeDirection(position, Viewport, 28, offset, 320));

    [Fact]
    public void The_edges_are_never_larger_than_the_fade()
    {
        // An 80-long strip: the fade (and the edge) is 20, so the middle doesn't scroll.
        Assert.Equal(0, ScrollSnap.EdgeDirection(25, 80, 28, 40, 100));
        Assert.Equal(-1, ScrollSnap.EdgeDirection(15, 80, 28, 40, 100));
    }

    [Fact]
    public void Nothing_to_scroll_means_no_edge()
        => Assert.Equal(0, ScrollSnap.EdgeDirection(5, Viewport, 28, 0, 0));
}
