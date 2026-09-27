using CustomDock.Services;

namespace CustomDock.Tests;

public class LowBatteryAlertsTests
{
    private static BatteryDeviceInfo Mouse(int percent, bool charging = false, string id = "mouse")
        => new(id, "Mouse", percent, charging, true, BatteryDeviceKind.Mouse);

    [Fact]
    public void Warns_once_when_the_threshold_is_reached()
    {
        var alerts = new LowBatteryAlerts();
        Assert.Empty(alerts.Check(new[] { Mouse(40) }, 15));
        Assert.Single(alerts.Check(new[] { Mouse(15) }, 15));
        Assert.Empty(alerts.Check(new[] { Mouse(12) }, 15));
        Assert.Empty(alerts.Check(new[] { Mouse(5) }, 15));
    }

    [Fact]
    public void Charging_rearms_the_warning()
    {
        var alerts = new LowBatteryAlerts();
        Assert.Single(alerts.Check(new[] { Mouse(10) }, 15));
        Assert.Empty(alerts.Check(new[] { Mouse(11, charging: true) }, 15));
        Assert.Single(alerts.Check(new[] { Mouse(11) }, 15));
    }

    [Fact]
    public void Small_climbs_above_the_threshold_do_not_rearm()
    {
        var alerts = new LowBatteryAlerts();
        Assert.Single(alerts.Check(new[] { Mouse(15) }, 15));
        Assert.Empty(alerts.Check(new[] { Mouse(18) }, 15));   // readings jitter by a few percent
        Assert.Empty(alerts.Check(new[] { Mouse(14) }, 15));
        Assert.Empty(alerts.Check(new[] { Mouse(21) }, 15));   // above 15 + 5: rearmed
        Assert.Single(alerts.Check(new[] { Mouse(15) }, 15));
    }

    [Fact]
    public void A_device_that_disappears_and_returns_low_is_not_announced_again()
    {
        var alerts = new LowBatteryAlerts();
        Assert.Single(alerts.Check(new[] { Mouse(8) }, 15));
        Assert.Empty(alerts.Check(Array.Empty<BatteryDeviceInfo>(), 15));
        Assert.Empty(alerts.Check(new[] { Mouse(8) }, 15));
    }

    [Fact]
    public void Devices_are_tracked_separately()
    {
        var alerts = new LowBatteryAlerts();
        var first = alerts.Check(new[] { Mouse(10, id: "a"), Mouse(50, id: "b") }, 15);
        Assert.Equal("a", Assert.Single(first).Id);
        var second = alerts.Check(new[] { Mouse(10, id: "a"), Mouse(9, id: "b") }, 15);
        Assert.Equal("b", Assert.Single(second).Id);
    }

    [Fact]
    public void Threshold_zero_turns_warnings_off()
    {
        var alerts = new LowBatteryAlerts();
        Assert.Empty(alerts.Check(new[] { Mouse(1) }, 0));
        Assert.Single(alerts.Check(new[] { Mouse(1) }, 5));
    }
}
