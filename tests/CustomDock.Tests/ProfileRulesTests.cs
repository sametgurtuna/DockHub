using CustomDock.Core;

namespace CustomDock.Tests;

public class ProfileRulesTests
{
    private static readonly IReadOnlySet<string> NoApps = new HashSet<string>();

    [Theory]
    [InlineData("Steam.exe", "steam")]
    [InlineData(@"C:\Games\cs2.exe", "cs2")]
    [InlineData("  Discord ", "discord")]
    public void App_names_are_normalized(string input, string expected)
        => Assert.Equal(expected, ProfileRules.NormalizeApp(input));

    [Fact]
    public void Time_window_within_a_day()
    {
        var work = new DockProfile { AutoTimeFrom = "08:30", AutoTimeTo = "17:30" };
        Assert.True(ProfileRules.InTimeWindow(work, new DateTime(2026, 9, 28, 9, 0, 0)));
        Assert.False(ProfileRules.InTimeWindow(work, new DateTime(2026, 9, 28, 17, 30, 0)));
        Assert.False(ProfileRules.InTimeWindow(work, new DateTime(2026, 9, 28, 8, 0, 0)));
    }

    [Fact]
    public void Time_window_can_pass_midnight()
    {
        var night = new DockProfile { AutoTimeFrom = "22:00", AutoTimeTo = "02:00" };
        Assert.True(ProfileRules.InTimeWindow(night, new DateTime(2026, 9, 28, 23, 0, 0)));
        Assert.True(ProfileRules.InTimeWindow(night, new DateTime(2026, 9, 29, 1, 0, 0)));
        Assert.False(ProfileRules.InTimeWindow(night, new DateTime(2026, 9, 29, 12, 0, 0)));
    }

    [Fact]
    public void Weekdays_only_skips_the_weekend()
    {
        var work = new DockProfile { AutoTimeFrom = "09:00", AutoTimeTo = "17:00", AutoWeekdaysOnly = true };
        Assert.True(ProfileRules.InTimeWindow(work, new DateTime(2026, 9, 25, 10, 0, 0)));  // Friday
        Assert.False(ProfileRules.InTimeWindow(work, new DateTime(2026, 9, 26, 10, 0, 0))); // Saturday
    }

    [Fact]
    public void Invalid_times_never_match()
    {
        Assert.False(ProfileRules.InTimeWindow(new DockProfile { AutoTimeFrom = "25:00", AutoTimeTo = "10:00" }, DateTime.Now));
        Assert.False(ProfileRules.InTimeWindow(new DockProfile { AutoTimeFrom = "soon", AutoTimeTo = "later" }, DateTime.Now));
    }

    [Fact]
    public void An_app_rule_wins_over_a_time_rule()
    {
        var work = new DockProfile { Name = "Work", AutoTimeFrom = "00:00", AutoTimeTo = "23:59" };
        var gaming = new DockProfile { Name = "Gaming", AutoApp = "steam" };
        var profiles = new[] { work, gaming };

        Assert.Same(work, ProfileRules.Pick(profiles, NoApps, new DateTime(2026, 9, 28, 12, 0, 0)));
        Assert.Same(gaming, ProfileRules.Pick(profiles, new HashSet<string> { "steam" }, new DateTime(2026, 9, 28, 12, 0, 0)));
    }

    [Fact]
    public void No_rule_means_no_profile()
        => Assert.Null(ProfileRules.Pick(new[] { new DockProfile(), new DockProfile() }, NoApps, DateTime.Now));
}
