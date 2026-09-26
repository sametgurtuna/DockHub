using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>A currency pair's latest rate and its recent daily history (oldest first).</summary>
public sealed record CurrencyQuote(string Base, string Target, double Rate, double? Previous, IReadOnlyList<double> History, DateTime Date)
{
    public double? ChangePercent => Previous is { } p && p != 0 ? (Rate - p) / p * 100 : null;
}

/// <summary>
/// Exchange rates from the European Central Bank via Frankfurter (api.frankfurter.dev, free, no API key).
/// Rates are published once per working day; results are cached for an hour and shared between widgets.
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

    public static async Task<List<CurrencyQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> targets)
    {
        baseCurrency = baseCurrency.ToUpperInvariant();
        var wanted = targets.Select(t => t.ToUpperInvariant()).Where(t => t != baseCurrency && Supported.Contains(t)).Distinct().ToList();
        if (wanted.Count == 0) return new();

        string key = baseCurrency + ":" + string.Join(",", wanted);
        if (Cache.TryGetValue(key, out var cached) && DateTime.Now - cached.FetchedAt < CacheFor) return cached.Quotes;

        var end = DateTime.Today;
        var start = end.AddDays(-14);
        string url = $"https://api.frankfurter.dev/v1/{start:yyyy-MM-dd}..{end:yyyy-MM-dd}?from={baseCurrency}&to={string.Join(",", wanted)}";
        var response = await Http.GetFromJsonAsync<RangeResponse>(url).ConfigureAwait(true)
                       ?? throw new InvalidOperationException("Empty response");

        var days = response.Rates.OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        var quotes = new List<CurrencyQuote>();
        foreach (var target in wanted)
        {
            var series = days.Where(d => d.Value.ContainsKey(target)).Select(d => (Date: d.Key, Rate: d.Value[target])).ToList();
            if (series.Count == 0) continue;
            var latest = series[^1];
            quotes.Add(new CurrencyQuote(baseCurrency, target, latest.Rate, series.Count > 1 ? series[^2].Rate : null,
                series.Select(s => s.Rate).ToList(), DateTime.TryParse(latest.Date, out var date) ? date : DateTime.Today));
        }
        Cache[key] = (DateTime.Now, quotes);
        return quotes;
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
