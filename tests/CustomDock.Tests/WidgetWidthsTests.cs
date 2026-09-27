using CustomDock.Widgets;

namespace CustomDock.Tests;

public class WidgetWidthsTests
{
    [Theory]
    [InlineData(WidgetWidth.Auto, 0)]
    [InlineData(WidgetWidth.Compact, 46)]
    [InlineData(WidgetWidth.Standard, 115)]
    [InlineData(WidgetWidth.Wide, 184)]
    public void Classes_are_multiples_of_the_card_height(WidgetWidth width, double minWidth)
    {
        Assert.Equal(minWidth, WidgetWidths.MinCardWidth(width));
    }

    [Theory]
    [InlineData(WidgetWidth.Compact)]
    [InlineData(WidgetWidth.Standard)]
    [InlineData(WidgetWidth.Wide)]
    public void Classes_sit_on_the_half_height_snap_grid(WidgetWidth width)
    {
        double step = WidgetWidths.CardHeight / 2;
        double value = WidgetWidths.MinCardWidth(width);
        Assert.Equal(0, value % step);
    }
}
