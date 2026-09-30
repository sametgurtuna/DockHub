namespace CustomDock.Services;

/// <summary>
/// "100 usd to try" in the launcher: which quotes to ask <see cref="CurrencyService"/> for, and the amount they give.
/// Currencies come from the ECB and coins from CoinGecko, where a coin's quote is its price in a currency.
/// </summary>
public static class CurrencyConversion
{
    /// <summary>Currency codes and coin symbols the launcher knows.</summary>
    public static readonly IReadOnlySet<string> Codes =
        new HashSet<string>(CurrencyService.Supported.Concat(CurrencyService.CryptoIds.Keys), StringComparer.OrdinalIgnoreCase);

    public static bool IsCoin(string code) => CurrencyService.CryptoIds.ContainsKey(code);

    /// <summary>The base currency and the symbols to ask for.</summary>
    public static (string Base, string[] Targets) Request(string from, string to) => (IsCoin(from), IsCoin(to)) switch
    {
        (false, _) => (from, new[] { to }),     // 1 from = rate to; or a coin's price in from
        (true, false) => (to, new[] { from }),  // the coin's price in to
        (true, true) => ("USD", new[] { from, to }),
    };

    /// <summary>The converted amount, or null when a quote is missing.</summary>
    public static double? Convert(double amount, string from, string to, IReadOnlyList<CurrencyQuote> quotes)
    {
        CurrencyQuote? Quote(string symbol) => quotes.FirstOrDefault(q => string.Equals(q.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        switch (IsCoin(from), IsCoin(to))
        {
            case (false, false):
                return Quote(to) is { Rate: > 0 } rate ? amount * rate.Rate : null;
            case (true, false):
                return Quote(from) is { Rate: > 0 } price ? amount * price.Rate : null;
            case (false, true):
                return Quote(to) is { Rate: > 0 } coin ? amount / coin.Rate : null;
            default:
                return Quote(from) is { Rate: > 0 } a && Quote(to) is { Rate: > 0 } b ? amount * a.Rate / b.Rate : null;
        }
    }
}
