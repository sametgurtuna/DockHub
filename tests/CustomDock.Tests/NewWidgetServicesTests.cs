using CustomDock.Services;

namespace CustomDock.Tests;

public class StockServiceTests
{
    private const string Csv = "Date,Open,High,Low,Close,Volume\r\n" +
                               "2026-09-24,225.1,228.0,224.3,226.40,51234567\r\n" +
                               "2026-09-25,226.5,227.1,223.9,224.81,48123456\r\n" +
                               "2026-09-28,225.0,228.3,224.7,227.52,50123456\r\n";

    [Fact]
    public void Reads_the_last_close_the_one_before_and_the_trend()
    {
        var quote = StockService.Parse("AAPL", Csv)!;
        Assert.Equal("AAPL", quote.Symbol);
        Assert.Equal(227.52, quote.Close);
        Assert.Equal(224.81, quote.PreviousClose);
        Assert.Equal(new[] { 226.40, 224.81, 227.52 }, quote.History);
        Assert.Equal(new DateTime(2026, 9, 28), quote.Date);
        Assert.Equal(1.20546, quote.ChangePercent!.Value, 4);
    }

    [Fact]
    public void Days_out_of_order_and_bad_lines_are_handled()
    {
        const string csv = "Date,Open,High,Low,Close,Volume\n2026-09-28,1,1,1,12.5,1\nnot,a,line\n2026-09-25,1,1,1,10,1\n2026-09-26,1,1,1,,1\n";
        var quote = StockService.Parse("X", csv)!;
        Assert.Equal(12.5, quote.Close);
        Assert.Equal(10, quote.PreviousClose);
        Assert.Equal(25, quote.ChangePercent!.Value, 6);
    }

    [Theory]
    [InlineData("No data")]
    [InlineData("")]
    [InlineData("Date,Open,High,Low,Close,Volume\n")]
    [InlineData("<html>Exceeded the daily hits limit</html>")]
    public void No_price_is_no_quote(string csv) => Assert.Null(StockService.Parse("X", csv));

    [Fact]
    public void A_single_day_has_no_change()
    {
        var quote = StockService.Parse("X", "Date,Close\n2026-09-28,5\n")!;
        Assert.Null(quote.PreviousClose);
        Assert.Null(quote.ChangePercent);
    }

    [Theory]
    [InlineData("AAPL", "aapl.us")]
    [InlineData("sap.de", "sap.de")]
    [InlineData("^SPX", "^spx")]
    [InlineData("brk-b", "brk-b.us")]
    [InlineData("a b", null)]
    [InlineData("aapl&x=1", null)]
    [InlineData("", null)]
    public void Symbols_become_stooq_names(string symbol, string? expected) => Assert.Equal(expected, StockService.StooqSymbol(symbol));

    [Fact]
    public void Symbols_are_split_and_cleaned()
        => Assert.Equal(new[] { "AAPL", "MSFT", "^SPX" }, StockService.SplitSymbols(" aapl, msft;^spx  AAPL, a&b"));
}

public class PingStatsTests
{
    [Fact]
    public void Averages_range_jitter_and_loss()
    {
        var stats = new PingStats();
        foreach (var ms in new double?[] { 20, 30, null, 10 }) stats.Add(ms);
        Assert.Equal(10, stats.Latest);
        Assert.False(stats.LastLost);
        Assert.Equal(20, stats.Average);
        Assert.Equal(10, stats.Min);
        Assert.Equal(30, stats.Max);
        Assert.Equal(25, stats.Loss);
        Assert.Equal(15, stats.Jitter); // |30-20| and |10-30|, lost replies skipped
        Assert.Equal(new double[] { 20, 30, 30, 10 }, stats.History);
    }

    [Fact]
    public void Only_the_last_replies_count()
    {
        var stats = new PingStats();
        for (int i = 0; i < PingStats.Window; i++) stats.Add(null);
        for (int i = 0; i < 10; i++) stats.Add(50);
        Assert.Equal(PingStats.Window, stats.Count);
        Assert.Equal((PingStats.Window - 10) * 100.0 / PingStats.Window, stats.Loss, 6);
    }

    [Fact]
    public void Nothing_yet_is_nothing()
    {
        var stats = new PingStats();
        Assert.Null(stats.Latest);
        Assert.Null(stats.Average);
        Assert.Null(stats.Jitter);
        Assert.Equal(0, stats.Loss);
        Assert.Empty(stats.History);
        stats.Add(null);
        Assert.True(stats.LastLost);
        Assert.Equal(100, stats.Loss);
    }

    [Theory]
    [InlineData(12.0, "AccentGreenBrush")]
    [InlineData(80.0, "AccentOrangeBrush")]
    [InlineData(400.0, "AccentRedBrush")]
    [InlineData(null, "AccentRedBrush")]
    public void Times_are_colored_by_quality(double? ms, string brush) => Assert.Equal(brush, PingStats.BrushFor(ms));
}

public class PowerModeOverlaysTests
{
    [Theory]
    [InlineData(PowerMode.BestEfficiency, "961cc777-2547-4f9d-8174-7d86181b8a7a")]
    [InlineData(PowerMode.Balanced, "00000000-0000-0000-0000-000000000000")]
    [InlineData(PowerMode.BestPerformance, "ded574b5-45a0-4f42-8737-46345c09c238")]
    public void Modes_map_to_the_overlays_Windows_uses(PowerMode mode, string guid)
    {
        Assert.Equal(new Guid(guid), PowerModeOverlays.OverlayOf(mode));
        Assert.Equal(mode, PowerModeOverlays.FromOverlay(new Guid(guid)));
    }

    [Fact]
    public void An_overlay_set_by_another_tool_is_unknown()
        => Assert.Null(PowerModeOverlays.FromOverlay(new Guid("3af9b8d9-7c97-431d-ad78-34a8bfea439f")));
}
