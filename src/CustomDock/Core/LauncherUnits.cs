using System.Globalization;
using System.Text.RegularExpressions;

namespace CustomDock.Core;

public enum UnitKind { Length, Weight, Temperature, Volume, Speed, Data }

/// <summary>A unit: its symbol and how it converts to its kind's base unit (value × factor + offset).</summary>
public sealed record UnitInfo(string Symbol, UnitKind Kind, double Factor, double Offset = 0);

/// <summary>An amount to convert, as typed: "10 km to mi" (amount 10, from "km", to "mi").</summary>
public sealed record ConversionQuery(double Amount, string From, string To);

/// <summary>
/// Unit conversion for the launcher: length, weight, temperature, volume, speed and data size, typed in English,
/// Turkish, German or Spanish ("10 km to mi", "10 km in mi", "10 km kaç mil", "10 km in Meilen", "10 km a millas").
/// Currencies use the same form ("100 usd to try"); their rates come from elsewhere.
/// </summary>
public static class LauncherUnits
{
    /// <summary>Words between the two units.</summary>
    private static readonly HashSet<string> Separators = new(StringComparer.OrdinalIgnoreCase)
    {
        "to", "in", "into", "as", "=", "->", "→", "kaç", "kac", "nach", "en", "a",
    };

    /// <summary>Words that may end a question ("10 km kaç mil eder?").</summary>
    private static readonly HashSet<string> Trailing = new(StringComparer.OrdinalIgnoreCase) { "eder", "yapar", "?" };

    private static readonly Regex Amount = new(@"^\s*(?<n>[-+]?\d+(?:[.,]\d+)?)\s*(?<rest>\S.*)$", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, UnitInfo> Units = BuildUnits();

    /// <summary>Splits "10 km to mi" into its amount and the two unit texts; null when the text isn't in that form.</summary>
    public static ConversionQuery? Parse(string text) => Readings(text).FirstOrDefault();

    /// <summary>
    /// Every way to split the text at a separator word, first to last: "5 in in cm" reads as ("in", "in cm") and as
    /// ("in", "cm"); the caller takes the first whose units it knows.
    /// </summary>
    private static IEnumerable<ConversionQuery> Readings(string text)
    {
        var match = Amount.Match(text);
        if (!match.Success) yield break;
        string number = match.Groups["n"].Value.Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double amount))
            yield break;

        var words = match.Groups["rest"].Value.TrimEnd('?', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 0 && Trailing.Contains(words[^1])) words.RemoveAt(words.Count - 1);
        for (int i = 1; i < words.Count - 1; i++)
        {
            if (Separators.Contains(words[i]))
                yield return new ConversionQuery(amount, string.Join(' ', words.Take(i)), string.Join(' ', words.Skip(i + 1)));
        }
    }

    public static UnitInfo? Find(string name)
        => Units.TryGetValue(Normalize(name), out var unit) ? unit : null;

    /// <summary>Converts a typed conversion between two units of the same kind; null when the text is not one.</summary>
    public static (double Value, UnitInfo From, UnitInfo To)? TryConvert(string text)
    {
        foreach (var candidate in Readings(text))
        {
            if (Find(candidate.From) is not { } from || Find(candidate.To) is not { } to || from.Kind != to.Kind) continue;
            return (Convert(candidate.Amount, from, to), from, to);
        }
        return null;
    }

    public static double Convert(double amount, UnitInfo from, UnitInfo to)
        => (amount * from.Factor + from.Offset - to.Offset) / to.Factor;

    /// <summary>
    /// A currency conversion ("100 usd to try", "100 $ in €", "50 euro kaç tl"): the amount and the two currency codes
    /// from <paramref name="codes"/>; null when the text is not one.
    /// </summary>
    public static (double Amount, string From, string To)? TryParseCurrency(string text, IReadOnlySet<string> codes)
    {
        foreach (var candidate in Readings(text))
        {
            if (CurrencyCode(candidate.From, codes) is not { } from || CurrencyCode(candidate.To, codes) is not { } to || from == to) continue;
            return (candidate.Amount, from, to);
        }
        return null;
    }

    private static readonly Dictionary<string, string> CurrencyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["$"] = "USD", ["dollar"] = "USD", ["dollars"] = "USD", ["dolar"] = "USD", ["dólar"] = "USD", ["dólares"] = "USD",
        ["€"] = "EUR", ["euro"] = "EUR", ["euros"] = "EUR", ["avro"] = "EUR",
        ["£"] = "GBP", ["sterlin"] = "GBP",
        ["¥"] = "JPY", ["yen"] = "JPY",
        ["₺"] = "TRY", ["tl"] = "TRY", ["lira"] = "TRY",
        ["₿"] = "BTC", ["bitcoin"] = "BTC",
    };

    private static string? CurrencyCode(string name, IReadOnlySet<string> codes)
    {
        string trimmed = name.Trim();
        if (CurrencyNames.TryGetValue(trimmed, out var code) && codes.Contains(code)) return code;
        string upper = trimmed.ToUpperInvariant();
        return codes.Contains(upper) ? upper : null;
    }

    /// <summary>A result for the launcher: six significant digits, in the current culture.</summary>
    public static string Format(double value)
    {
        if (value == 0) return "0";
        double magnitude = Math.Floor(Math.Log10(Math.Abs(value)));
        if (magnitude >= 15 || magnitude < -6) return value.ToString("G6", CultureInfo.CurrentCulture);
        int decimals = (int)Math.Clamp(5 - magnitude, 0, 10);
        return Math.Round(value, decimals).ToString("#,0." + new string('#', Math.Max(decimals, 1)), CultureInfo.CurrentCulture);
    }

    private static string Normalize(string name) => string.Join(' ', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static Dictionary<string, UnitInfo> BuildUnits()
    {
        var units = new Dictionary<string, UnitInfo>(StringComparer.Ordinal);
        void Add(UnitInfo unit, params string[] names)
        {
            units[Normalize(unit.Symbol)] = unit;
            foreach (var name in names) units[Normalize(name)] = unit;
        }

        // Length (metre)
        Add(new("mm", UnitKind.Length, 0.001), "millimeter", "millimeters", "millimetre", "millimetres", "milimetre", "milímetro", "milímetros", "milimetro", "milimetros");
        Add(new("cm", UnitKind.Length, 0.01), "centimeter", "centimeters", "centimetre", "centimetres", "santimetre", "santim", "zentimeter", "centímetro", "centímetros", "centimetro", "centimetros");
        Add(new("m", UnitKind.Length, 1), "meter", "meters", "metre", "metres", "metro", "metros");
        Add(new("km", UnitKind.Length, 1000), "kilometer", "kilometers", "kilometre", "kilometres", "kilómetro", "kilómetros", "kilometro", "kilometros");
        Add(new("in", UnitKind.Length, 0.0254), "inch", "inches", "\"", "inç", "zoll", "pulgada", "pulgadas");
        Add(new("ft", UnitKind.Length, 0.3048), "foot", "feet", "'", "fit", "ayak", "fuß", "fuss", "pie", "pies");
        Add(new("yd", UnitKind.Length, 0.9144), "yard", "yards", "yarda", "yardas");
        Add(new("mi", UnitKind.Length, 1609.344), "mile", "miles", "mil", "meile", "meilen", "milla", "millas");
        Add(new("nmi", UnitKind.Length, 1852), "nautical mile", "nautical miles", "deniz mili", "seemeile", "seemeilen", "milla náutica", "millas náuticas");

        // Weight (kilogram)
        Add(new("mg", UnitKind.Weight, 1e-6), "milligram", "milligrams", "miligram", "milligramm", "miligramo", "miligramos");
        Add(new("g", UnitKind.Weight, 0.001), "gram", "grams", "gramme", "grammes", "gramm", "gramo", "gramos");
        Add(new("kg", UnitKind.Weight, 1), "kilogram", "kilograms", "kilo", "kilos", "kilogramm", "kilogramo", "kilogramos");
        Add(new("t", UnitKind.Weight, 1000), "ton", "tons", "tonne", "tonnes", "tonnen", "tonelada", "toneladas");
        Add(new("oz", UnitKind.Weight, 0.028349523125), "ounce", "ounces", "ons", "unze", "unzen", "onza", "onzas");
        Add(new("lb", UnitKind.Weight, 0.45359237), "lbs", "pound", "pounds", "libre", "pfund", "libra", "libras");
        Add(new("st", UnitKind.Weight, 6.35029318), "stone", "stones");

        // Temperature (degree Celsius)
        Add(new("°C", UnitKind.Temperature, 1), "c", "celsius", "santigrat", "santigrad", "grad celsius", "grados celsius");
        Add(new("°F", UnitKind.Temperature, 5.0 / 9, -160.0 / 9), "f", "fahrenheit");
        Add(new("K", UnitKind.Temperature, 1, -273.15), "kelvin");

        // Volume (litre)
        Add(new("ml", UnitKind.Volume, 0.001), "milliliter", "milliliters", "millilitre", "millilitres", "mililitre", "mililitro", "mililitros");
        Add(new("cl", UnitKind.Volume, 0.01), "centiliter", "centiliters", "centilitre", "santilitre", "zentiliter", "centilitro", "centilitros");
        Add(new("dl", UnitKind.Volume, 0.1), "deciliter", "decilitre", "desilitre", "deziliter", "decilitro", "decilitros");
        Add(new("l", UnitKind.Volume, 1), "liter", "liters", "litre", "litres", "litro", "litros");
        Add(new("m³", UnitKind.Volume, 1000), "m3", "cubic meter", "cubic meters", "cubic metre", "metreküp", "kubikmeter", "metro cúbico", "metros cúbicos");
        Add(new("gal", UnitKind.Volume, 3.785411784), "gallon", "gallons", "galon", "gallone", "gallonen", "galón", "galones");
        Add(new("qt", UnitKind.Volume, 0.946352946), "quart", "quarts");
        Add(new("pt", UnitKind.Volume, 0.473176473), "pint", "pints", "pinta", "pintas");
        Add(new("cup", UnitKind.Volume, 0.2365882365), "cups", "taza", "tazas");
        Add(new("fl oz", UnitKind.Volume, 0.0295735295625), "floz", "fluid ounce", "fluid ounces");

        // Speed (metre per second)
        Add(new("m/s", UnitKind.Speed, 1), "mps", "meters per second", "metres per second");
        Add(new("km/h", UnitKind.Speed, 1 / 3.6), "kmh", "kph", "km/sa", "kilometers per hour", "kilometres per hour");
        Add(new("mph", UnitKind.Speed, 0.44704), "mi/h", "miles per hour", "mil/sa");
        Add(new("kn", UnitKind.Speed, 1852 / 3600.0), "kt", "knot", "knots", "knoten", "nudo", "nudos", "knot/sa");
        Add(new("ft/s", UnitKind.Speed, 0.3048), "fps");

        // Data (byte; kB, MB... in thousands, KiB, MiB... in 1024s)
        Add(new("bit", UnitKind.Data, 0.125), "bits");
        Add(new("B", UnitKind.Data, 1), "byte", "bytes", "bayt");
        Add(new("kB", UnitKind.Data, 1e3), "kilobyte", "kilobytes", "kilobayt");
        Add(new("MB", UnitKind.Data, 1e6), "megabyte", "megabytes", "megabayt");
        Add(new("GB", UnitKind.Data, 1e9), "gigabyte", "gigabytes", "gigabayt");
        Add(new("TB", UnitKind.Data, 1e12), "terabyte", "terabytes", "terabayt");
        Add(new("PB", UnitKind.Data, 1e15), "petabyte", "petabytes", "petabayt");
        Add(new("KiB", UnitKind.Data, 1024), "kibibyte", "kibibytes");
        Add(new("MiB", UnitKind.Data, 1024.0 * 1024), "mebibyte", "mebibytes");
        Add(new("GiB", UnitKind.Data, 1024.0 * 1024 * 1024), "gibibyte", "gibibytes");
        Add(new("TiB", UnitKind.Data, 1024.0 * 1024 * 1024 * 1024), "tebibyte", "tebibytes");
        Add(new("kbit", UnitKind.Data, 1e3 / 8), "kilobit", "kilobits", "kb/s", "kbps");
        Add(new("Mbit", UnitKind.Data, 1e6 / 8), "megabit", "megabits", "mbps");
        Add(new("Gbit", UnitKind.Data, 1e9 / 8), "gigabit", "gigabits", "gbps");
        return units;
    }
}
