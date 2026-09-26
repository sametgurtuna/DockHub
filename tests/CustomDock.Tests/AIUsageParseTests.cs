using CustomDock.Services;

namespace CustomDock.Tests;

public class AIUsageParseTests
{
    [Fact]
    public void Parses_session_and_week_with_reset_times()
    {
        var data = AIUsageService.Parse(TestEnvironment.Fixture(@"ai-usage\both.txt"));
        Assert.Equal(38, data.SessionPercent);
        Assert.Equal("5pm", data.SessionResets);
        Assert.Equal(48, data.WeekPercent);
        Assert.Equal("Oct 2, 10am", data.WeekResets);
    }

    [Fact]
    public void Parses_a_session_only_output()
    {
        var data = AIUsageService.Parse(TestEnvironment.Fixture(@"ai-usage\session-only.txt"));
        Assert.Equal(7, data.SessionPercent);
        Assert.Equal("in 3 hr 12 min", data.SessionResets);
        Assert.Null(data.WeekPercent);
    }

    [Theory]
    [InlineData(@"ai-usage\model-answer.txt")]
    [InlineData(@"ai-usage\login.txt")]
    public void Unrecognized_output_has_no_values(string fixture)
    {
        var data = AIUsageService.Parse(TestEnvironment.Fixture(fixture));
        Assert.Null(data.SessionPercent);
        Assert.Null(data.WeekPercent);
    }

    [Fact]
    public void Parsing_is_culture_independent()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            Assert.Equal(38, AIUsageService.Parse("Current session: 38% used · resets 5pm").SessionPercent);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
