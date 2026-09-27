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

    // ------------------------------------------------------------------ Crypto (CoinGecko)

    private static long Ms(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds();

    [Fact]
    public void Market_chart_prices_are_read_in_time_order()
    {
        var day = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
        string json = $$"""
            { "prices": [[{{Ms(day.AddHours(1))}}, 101.5], [{{Ms(day)}}, 100.0], ["bad", 1], [{{Ms(day.AddHours(2))}}]],
              "market_caps": [[{{Ms(day)}}, 1e12]], "total_volumes": [] }
            """;
        var points = CurrencyService.ParseMarketChart(json);
        Assert.Equal(2, points.Count);
        Assert.Equal((day, 100.0), points[0]);
        Assert.Equal((day.AddHours(1), 101.5), points[1]);
        Assert.Empty(CurrencyService.ParseMarketChart("""{ "error": "coin not found" }"""));
    }

    [Fact]
    public void Crypto_quote_uses_the_price_a_day_earlier_and_one_point_per_day()
    {
        var start = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
        var points = Enumerable.Range(0, 49).Select(h => (start.AddHours(h), 1000.0 + h)).ToList(); // hourly, 2 days

        var quote = CurrencyService.BuildCryptoQuote("btc", "usd", points)!;
        Assert.True(quote.IsCrypto);
        Assert.Equal("BTC", quote.Base);
        Assert.Equal("USD", quote.Target);
        Assert.Equal("BTC", quote.Symbol);
        Assert.Equal(1048.0, quote.Rate);
        Assert.Equal(1024.0, quote.Previous); // exactly 24 hours before the latest price
        Assert.Equal(new[] { 1023.0, 1047.0, 1048.0 }, quote.History); // last price of each day
        Assert.Null(CurrencyService.BuildCryptoQuote("btc", "usd", Array.Empty<(DateTime, double)>()));
    }

    [Fact]
    public void A_short_history_has_no_daily_change()
    {
        var now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        var quote = CurrencyService.BuildCryptoQuote("eth", "eur", new[] { (now.AddHours(-3), 2000.0), (now, 2100.0) })!;
        Assert.Null(quote.Previous);
        Assert.Null(quote.ChangePercent);
    }

    [Fact]
    public void Fiat_quotes_are_listed_by_their_currency()
    {
        var quote = new CurrencyQuote("USD", "TRY", 41, null, new[] { 41.0 }, DateTime.Today);
        Assert.False(quote.IsCrypto);
        Assert.Equal("TRY", quote.Symbol);
    }

    [Fact]
    public void Crypto_symbols_do_not_clash_with_currencies()
    {
        Assert.Empty(CurrencyService.CryptoIds.Keys.Intersect(CurrencyService.Supported, StringComparer.OrdinalIgnoreCase));
        Assert.Equal("bitcoin", CurrencyService.CryptoIds["btc"]);
    }
}
