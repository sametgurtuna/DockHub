using CustomDock.Services;

namespace CustomDock.Tests;

public class CurrencyServiceTests
{
    private static Dictionary<string, Dictionary<string, double>> Rates() => new()
    {
        ["2026-09-25"] = new() { ["TRY"] = 41.0, ["EUR"] = 0.90 },
        ["2026-09-23"] = new() { ["TRY"] = 40.0, ["EUR"] = 0.92 },
        ["2026-09-24"] = new() { ["TRY"] = 40.5 },
    };

    [Fact]
    public void Quotes_use_the_latest_day_and_the_one_before()
    {
        var quotes = CurrencyService.BuildQuotes("USD", new[] { "TRY", "EUR" }, Rates());
        Assert.Equal(2, quotes.Count);

        var lira = quotes[0];
        Assert.Equal("TRY", lira.Target);
        Assert.Equal(41.0, lira.Rate);
        Assert.Equal(40.5, lira.Previous);
        Assert.Equal(new[] { 40.0, 40.5, 41.0 }, lira.History);
        Assert.Equal(new DateTime(2026, 9, 25), lira.Date);
        Assert.Equal((41.0 - 40.5) / 40.5 * 100, lira.ChangePercent!.Value, 6);

        var euro = quotes[1];
        Assert.Equal(0.92, euro.Previous); // the missing day is skipped
    }

    [Fact]
    public void Currencies_without_rates_are_left_out()
    {
        Assert.Empty(CurrencyService.BuildQuotes("USD", new[] { "GBP" }, Rates()));
        var single = CurrencyService.BuildQuotes("USD", new[] { "TRY" }, new() { ["2026-09-25"] = new() { ["TRY"] = 41.0 } });
        Assert.Null(Assert.Single(single).Previous);
        Assert.Null(single[0].ChangePercent);
    }
}
