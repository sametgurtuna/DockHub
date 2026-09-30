using System.Globalization;
using System.Net.Http;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>A stock's latest close, the one before it (for the change) and its recent daily closes (oldest first).</summary>
public sealed record StockQuote(string Symbol, double Close, double? PreviousClose, IReadOnlyList<double> History, DateTime Date)
{
    public double? ChangePercent => PreviousClose is { } previous && previous != 0 ? (Close - previous) / previous * 100 : null;
}

/// <summary>
/// Stock prices from Stooq (stooq.com): free daily prices without an API key, delayed, for personal use. A symbol
/// without a market ("AAPL") is looked up on the US market ("aapl.us"); "^spx" is an index. Results are cached for
/// fifteen minutes and shared between widgets.
/// </summary>
public static class StockService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);
    private static readonly Dictionary<string, (DateTime FetchedAt, StockQuote Quote)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The symbols typed in the settings ("AAPL, MSFT; ^spx"), in order, without repeats.</summary>
    public static IReadOnlyList<string> SplitSymbols(string text)
        => text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => StooqSymbol(s) is not null)
            .Distinct()
            .ToList();

    /// <summary>Stooq's name for a symbol ("AAPL" → "aapl.us"); null for text that isn't a symbol.</summary>
    public static string? StooqSymbol(string symbol)
    {
        string s = symbol.Trim().ToLowerInvariant();
        if (s.Length is 0 or > 20 || !s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '^' or '-' or '_')) return null;
        return s.StartsWith('^') || s.Contains('.') ? s : s + ".us";
    }

    /// <summary>Quotes in the order asked; one symbol failing doesn't hide the others.</summary>
    public static async Task<List<StockQuote>> GetAsync(IReadOnlyList<string> symbols)
    {
        var quotes = new List<StockQuote>();
        Exception? error = null;
        foreach (var symbol in symbols)
        {
            try
            {
                if (await GetOneAsync(symbol).ConfigureAwait(true) is { } quote) quotes.Add(quote);
            }
            catch (Exception ex)
            {
                error ??= ex;
                Log.Warn($"Stock price for {symbol} failed: {ex.Message}");
            }
        }
        if (quotes.Count == 0 && error is not null) throw error;
        return quotes;
    }

    private static async Task<StockQuote?> GetOneAsync(string symbol)
    {
        if (StooqSymbol(symbol) is not { } stooq) return null;
        if (Cache.TryGetValue(stooq, out var cached) && DateTime.Now - cached.FetchedAt < CacheFor) return cached.Quote;

        var end = DateTime.Today;
        var start = end.AddDays(-30);
        string url = $"https://stooq.com/q/d/l/?s={Uri.EscapeDataString(stooq)}&i=d&d1={start:yyyyMMdd}&d2={end:yyyyMMdd}";
        string csv = await Http.GetStringAsync(url).ConfigureAwait(true);
        var quote = Parse(symbol.ToUpperInvariant(), csv);
        if (quote is not null) Cache[stooq] = (DateTime.Now, quote);
        return quote;
    }

    /// <summary>
    /// A quote from Stooq's daily CSV (<c>Date,Open,High,Low,Close,Volume</c>, oldest day first); null for "No data"
    /// or anything else without a price.
    /// </summary>
    internal static StockQuote? Parse(string symbol, string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return null;
        var header = lines[0].Split(',');
        int dateColumn = Array.FindIndex(header, h => h.Equals("Date", StringComparison.OrdinalIgnoreCase));
        int closeColumn = Array.FindIndex(header, h => h.Equals("Close", StringComparison.OrdinalIgnoreCase));
        if (dateColumn < 0 || closeColumn < 0) return null;

        var days = new List<(DateTime Date, double Close)>();
        foreach (var line in lines.Skip(1))
        {
            var fields = line.Split(',');
            if (fields.Length <= Math.Max(dateColumn, closeColumn)) continue;
            if (!DateTime.TryParseExact(fields[dateColumn], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            if (!double.TryParse(fields[closeColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out double close) || close <= 0) continue;
            days.Add((date, close));
        }
        if (days.Count == 0) return null;
        days.Sort((a, b) => a.Date.CompareTo(b.Date));
        var latest = days[^1];
        return new StockQuote(symbol, latest.Close, days.Count > 1 ? days[^2].Close : null, days.Select(d => d.Close).ToList(), latest.Date);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DockHub (+https://github.com/sametgurtuna/DockHub)");
        return client;
    }
}
