using CustomDock.Widgets;

namespace CustomDock.Tests;

public class WidgetFlyoutLayoutTests
{
    [Fact]
    public void The_three_panel_widths()
    {
        Assert.Equal(280, WidgetFlyoutLayout.WidthOf(FlyoutSize.Narrow));
        Assert.Equal(340, WidgetFlyoutLayout.WidthOf(FlyoutSize.Standard));
        Assert.Equal(420, WidgetFlyoutLayout.WidthOf(FlyoutSize.Wide));
    }

    [Theory]
    [InlineData(260, FlyoutSize.Narrow)]   // clock, world clock
    [InlineData(300, FlyoutSize.Narrow)]   // brightness
    [InlineData(310, FlyoutSize.Narrow)]   // halfway rounds down
    [InlineData(320, FlyoutSize.Standard)] // to do
    [InlineData(380, FlyoutSize.Standard)]
    [InlineData(392, FlyoutSize.Wide)]     // folder stack
    [InlineData(600, FlyoutSize.Wide)]
    public void Old_panel_widths_map_to_the_closest_size(double width, FlyoutSize expected)
        => Assert.Equal(expected, WidgetFlyoutLayout.SizeFor(width));

    [Fact]
    public void Every_size_maps_back_to_itself()
    {
        foreach (var size in Enum.GetValues<FlyoutSize>())
            Assert.Equal(size, WidgetFlyoutLayout.SizeFor(WidgetFlyoutLayout.WidthOf(size)));
    }
}
