using CustomDock.Core;

namespace CustomDock.Tests;

public class StartupPacingTests
{
    private static readonly DateTime Start = new(2026, 9, 27, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Work_waits_until_its_offset_after_startup()
    {
        Assert.Equal(TimeSpan.FromSeconds(4), StartupPacing.DelayFor(TimeSpan.FromSeconds(4), Start, Start));
        Assert.Equal(TimeSpan.FromSeconds(3), StartupPacing.DelayFor(TimeSpan.FromSeconds(4), Start, Start.AddSeconds(1)));
    }

    [Fact]
    public void Later_work_is_not_delayed()
    {
        Assert.Equal(TimeSpan.Zero, StartupPacing.DelayFor(TimeSpan.FromSeconds(4), Start, Start.AddSeconds(4)));
        Assert.Equal(TimeSpan.Zero, StartupPacing.DelayFor(TimeSpan.FromSeconds(4), Start, Start.AddMinutes(30)));
    }

    [Fact]
    public void First_requests_are_spread_out()
    {
        Assert.True(StartupPacing.DeviceBatteries < StartupPacing.Weather);
        Assert.True(StartupPacing.Weather < StartupPacing.AIUsage);
        Assert.True(StartupPacing.AIUsage <= TimeSpan.FromSeconds(10));
    }
}
