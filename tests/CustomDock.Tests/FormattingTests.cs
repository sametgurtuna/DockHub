using System.Globalization;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Tests;

public class FormattingTests
{
    [Theory]
    [InlineData(0, "0.0", "KB/s")]
    [InlineData(5 * 1024, "5.0", "KB/s")]
    [InlineData(512 * 1024, "512", "KB/s")]
    [InlineData(3.5 * 1024 * 1024, "3.5", "MB/s")]
    [InlineData(120.0 * 1024 * 1024, "120", "MB/s")]
    public void Network_speeds_pick_a_unit(double bytesPerSecond, string value, string unit)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal((value, unit), NetworkMonitorService.Format(bytesPerSecond));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(1234567.0, "1,234,567")]
    [InlineData(0.1 + 0.2, "0.3")]
    [InlineData(2.5, "2.5")]
    [InlineData(1e20, "1E+20")]
    public void Calculator_results_are_formatted(double value, string text)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(text, LauncherMath.Format(value));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("Ayarlar", "ayar", 100)]
    [InlineData("Café Racer", "cafe", 100)]  // accents are ignored
    [InlineData("Microsoft Edge", "edge", 80)]
    [InlineData("Notepad", "", 1)]
    public void Launcher_matching_ignores_case_and_accents(string title, string query, int score)
    {
        Assert.Equal(score, LauncherMatch.Score(title, null, query));
    }

    [Fact]
    public void Every_weather_code_has_a_kind_and_description()
    {
        int[] codes = { 0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 71, 73, 75, 77, 80, 81, 82, 85, 86, 95, 96, 99 };
        foreach (int code in codes)
            Assert.NotEqual("—", WeatherService.DescribeCode(code));
        Assert.Equal(WeatherKind.Thunder, WeatherService.KindFromCode(99));
        Assert.Equal(WeatherKind.Snow, WeatherService.KindFromCode(85));
        Assert.Equal(WeatherKind.Cloudy, WeatherService.KindFromCode(1234));
    }
}
