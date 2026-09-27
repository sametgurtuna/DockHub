using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>
/// A pair's latest rate and its recent daily history (oldest first). For a cryptocurrency the pair is the coin in the
/// chosen currency ("1 BTC = 65,000 USD"), so <see cref="Base"/> is the coin.
/// </summary>
public sealed record CurrencyQuote(string Base, string Target, double Rate, double? Previous, IReadOnlyList<double> History, DateTime Date,
    bool IsCrypto = false)
{
    public double? ChangePercent => Previous is { } p && p != 0 ? (Rate - p) / p * 100 : null;

    /// <summary>What the user typed for this quote: the currency, or the coin.</summary>
    public string Symbol => IsCrypto ? Base : Target;
}

/// <summary>
/// Exchange rates from the European Central Bank via Frankfurter (api.frankfurter.dev, free, no API key), and
/// cryptocurrency prices from CoinGecko (api.coingecko.com, free, no API key). ECB rates are published once per
/// working day; results are cached for an hour and shared between widgets.
/// </summary>
public static class CurrencyService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);
    private static readonly Dictionary<string, (DateTime FetchedAt, List<CurrencyQuote> Quotes)> Cache = new();

    public static readonly string[] Supported =
    {
        "AUD", "BGN", "BRL", "CAD", "CHF", "CNY", "CZK", "DKK", "EUR", "GBP", "HKD", "HUF", "IDR", "ILS", "INR", "ISK",
        "JPY", "KRW", "MXN", "MYR", "NOK", "NZD", "PHP", "PLN", "RON", "SEK", "SGD", "THB", "TRY", "USD", "ZAR",
    };

    /// <summary>Cryptocurrencies by symbol, with their CoinGecko ids.</summary>
    public static readonly IReadOnlyDictionary<string, string> CryptoIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = "bitcoin", ["ETH"] = "ethereum", ["SOL"] = "solana", ["BNB"] = "binancecoin", ["XRP"] = "ripple",
        ["ADA"] = "cardano", ["DOGE"] = "dogecoin", ["TRX"] = "tron", ["TON"] = "the-open-network", ["DOT"] = "polkadot",
        ["AVAX"] = "avalanche-2", ["LTC"] = "litecoin", ["LINK"] = "chainlink", ["BCH"] = "bitcoin-cash", ["XLM"] = "stellar",
        ["ATOM"] = "cosmos", ["SHIB"] = "shiba-inu", ["USDT"] = "tether", ["USDC"] = "usd-coin",
    };

    /// <summary>
    /// Quotes for <paramref name="targets"/> against <paramref name="baseCurrency"/>, in the order they were asked for:
    /// currencies from the ECB, cryptocurrencies from CoinGecko.
    /// </summary>
    public static async Task<List<CurrencyQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> targets)
    {
        baseCurrency = baseCurrency.ToUpperInvariant();
        var symbols = targets.Select(t => t.ToUpperInvariant()).Where(t => t != baseCurrency).Distinct().ToList();

        // One source failing doesn't hide the other; only when nothing could be loaded is the error passed on.
        var quotes = new List<CurrencyQuote>();
        Exception? error = null;
        try
        {
            quotes.AddRange(await GetFiatAsync(baseCurrency, symbols.Where(t => Supported.Contains(t)).ToList()).ConfigureAwait(true));
        }
        catch (Exception ex)
        {
            error = ex;
        }
        foreach (var coin in symbols.Where(CryptoIds.ContainsKey))
        {
            try
            {
                if (await GetCryptoAsync(coin, baseCurrency).ConfigureAwait(true) is { } quote) quotes.Add(quote);
            }
            catch (Exception ex)
            {
                error ??= ex;
                Log.Warn($"Crypto price for {coin} failed: {ex.Message}");
            }
        }
        if (quotes.Count == 0 && error is not null) throw error;

        var bySymbol = quotes.ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase);
        return symbols.Where(bySymbol.ContainsKey).Select(s => bySymbol[s]).ToList();
    }

    private static async Task<List<CurrencyQuote>> GetFiatAsync(string baseCurrency, List<string> wanted)
    {
        if (wanted.Count == 0) return new();

        string key = baseCurrency + ":" + string.Join(",", wanted);
        if (Cache.TryGetValue(key, out var cached) && DateTime.Now - cached.FetchedAt < CacheFor) return cached.Quotes;

        var end = DateTime.Today;
        var start = end.AddDays(-14);
        string url = $"https://api.frankfurter.dev/v1/{start:yyyy-MM-dd}..{end:yyyy-MM-dd}?from={baseCurrency}&to={string.Join(",", wanted)}";
        var response = await Http.GetFromJsonAsync<RangeResponse>(url).ConfigureAwait(true)
                       ?? throw new InvalidOperationException("Empty response");

        var quotes = BuildQuotes(baseCurrency, wanted, response.Rates);
        Cache[key] = (DateTime.Now, quotes);
        return quotes;
    }

    /// <summary>Quotes from a Frankfurter range answer (date → currency → rate), oldest day first in each history.</summary>
    internal static List<CurrencyQuote> BuildQuotes(string baseCurrency, IEnumerable<string> wanted,
        Dictionary<string, Dictionary<string, double>> rates)
    {
        var days = rates.OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        var quotes = new List<CurrencyQuote>();
        foreach (var target in wanted)
        {
            var series = days.Where(d => d.Value.ContainsKey(target)).Select(d => (Date: d.Key, Rate: d.Value[target])).ToList();
            if (series.Count == 0) continue;
            var latest = series[^1];
            quotes.Add(new CurrencyQuote(baseCurrency, target, latest.Rate, series.Count > 1 ? series[^2].Rate : null,
                series.Select(s => s.Rate).ToList(),
                DateTime.TryParse(latest.Date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) ? date : DateTime.Today));
        }
        return quotes;
    }

    // ------------------------------------------------------------------ Cryptocurrencies (CoinGecko)

    private static readonly Dictionary<string, (DateTime FetchedAt, CurrencyQuote Quote)> CryptoCache = new();

    /// <summary>The price of a coin in <paramref name="currency"/>; a stale price is kept when CoinGecko limits requests.</summary>
    private static async Task<CurrencyQuote?> GetCryptoAsync(string coin, string currency)
    {
        string key = coin + ":" + currency;
        CryptoCache.TryGetValue(key, out var cached);
        if (cached.Quote is not null && DateTime.Now - cached.FetchedAt < CacheFor) return cached.Quote;

        string url = $"https://api.coingecko.com/api/v3/coins/{CryptoIds[coin]}/market_chart?vs_currency={currency.ToLowerInvariant()}&days=14";
        using var response = await Http.GetAsync(url).ConfigureAwait(true);
        if (response.StatusCode == HttpStatusCode.TooManyRequests && cached.Quote is not null)
        {
            Log.Info($"CoinGecko limit reached; keeping the {coin} price from {cached.FetchedAt:t}.");
            return cached.Quote;
        }
        response.EnsureSuccessStatusCode();

        var points = ParseMarketChart(await response.Content.ReadAsStringAsync().ConfigureAwait(true));
        var quote = BuildCryptoQuote(coin, currency, points);
        if (quote is not null) CryptoCache[key] = (DateTime.Now, quote);
        return quote;
    }

    /// <summary>The "prices" of a CoinGecko market chart: [unix milliseconds, price] pairs, oldest first.</summary>
    internal static List<(DateTime Time, double Price)> ParseMarketChart(string json)
    {
        var points = new List<(DateTime, double)>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("prices", out var prices) || prices.ValueKind != JsonValueKind.Array) return points;
        foreach (var pair in prices.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2) continue;
            if (pair[0].ValueKind != JsonValueKind.Number || pair[1].ValueKind != JsonValueKind.Number) continue;
            if (!pair[0].TryGetDouble(out double ms) || !pair[1].TryGetDouble(out double price)) continue;
            points.Add((DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime, price));
        }
        points.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return points;
    }

    /// <summary>
    /// A coin's quote from its price history: the latest price, the price 24 hours before it (for the change), and the
    /// last price of each day for the trend line.
    /// </summary>
    internal static CurrencyQuote? BuildCryptoQuote(string coin, string currency, IReadOnlyList<(DateTime Time, double Price)> points)
    {
        if (points.Count == 0) return null;
        var latest = points[^1];
        var dayBefore = latest.Time.AddHours(-24);
        double? previous = null;
        foreach (var point in points)
            if (point.Time <= dayBefore) previous = point.Price;
        var daily = points.GroupBy(p => p.Time.Date).Select(day => day.Last().Price).ToList();
        return new CurrencyQuote(coin.ToUpperInvariant(), currency.ToUpperInvariant(), latest.Price, previous, daily,
            latest.Time.ToLocalTime(), IsCrypto: true);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DockHub (+https://github.com/sametgurtuna/DockHub)");
        return client;
    }

    private sealed class RangeResponse
    {
        [JsonPropertyName("rates")] public Dictionary<string, Dictionary<string, double>> Rates { get; set; } = new();
    }
}
