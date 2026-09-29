using CustomDock.Shell;

namespace CustomDock.Tests;

public class IconSizingTests
{
    [Theory]
    [InlineData(1.0, 96)]
    [InlineData(1.5, 96)]
    [InlineData(2.0, 96)]
    [InlineData(2.25, 96)]
    [InlineData(2.5, 128)]
    [InlineData(3.0, 128)]
    [InlineData(3.5, 192)]
    [InlineData(5.0, 256)]
    public void Icons_are_requested_large_enough_for_the_display(double dpiScale, int expected)
    {
        Assert.Equal(expected, IconSizing.For(dpiScale));
    }

    [Fact]
    public void Sizes_never_shrink_below_the_old_fixed_request_and_stop_at_256()
    {
        Assert.Equal(96, IconSizing.For(0.5));
        Assert.Equal(256, IconSizing.For(12));
    }
}
