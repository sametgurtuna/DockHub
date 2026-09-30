using System.Globalization;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Tests;

public class LauncherUnitsTests
{
    [Theory]
    [InlineData("10 km to mi", 6.21371)]
    [InlineData("10 km in mi", 6.21371)]
    [InlineData("10 km kaç mil", 6.21371)]
    [InlineData("10 km kaç mil eder?", 6.21371)]
    [InlineData("10 km in Meilen", 6.21371)]
    [InlineData("10 km a millas", 6.21371)]
    [InlineData("10km to mi", 6.21371)]
    [InlineData("5 in in cm", 12.7)]
    [InlineData("6 ft to m", 1.8288)]
    [InlineData("1 nautical mile in km", 1.852)]
    [InlineData("2,5 kg to lb", 5.51156)]
    [InlineData("100 g kaç ons", 3.52740)]
    [InlineData("1 gal to l", 3.78541)]
    [InlineData("100 km/h to mph", 62.1371)]
    [InlineData("1 GB to MB", 1000)]
    [InlineData("1 GiB in MiB", 1024)]
    [InlineData("100 mbps to MB", 12.5)]
    public void Converts_in_every_language(string text, double expected)
    {
        var result = LauncherUnits.TryConvert(text);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Value.Value, 3);
    }

    [Theory]
    [InlineData("100 c to f", 212)]
    [InlineData("32 °F in °C", 0)]
    [InlineData("0 celsius in kelvin", 273.15)]
    [InlineData("-40 f to c", -40)]
    public void Converts_temperatures(string text, double expected)
        => Assert.Equal(expected, LauncherUnits.TryConvert(text)!.Value.Value, 6);

    [Theory]
    [InlineData("10 km to kg")]       // different kinds
    [InlineData("10 km to")]
    [InlineData("km to mi")]
    [InlineData("10 bananas to mi")]
    [InlineData("hello world")]
    [InlineData("")]
    [InlineData("200*15%")]
    public void Anything_else_is_not_a_conversion(string text) => Assert.Null(LauncherUnits.TryConvert(text));

    [Fact]
    public void Results_have_six_significant_digits()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("6.21371", LauncherUnits.Format(6.2137119224));
            Assert.Equal("1,609.34", LauncherUnits.Format(1609.344));
            Assert.Equal("1,000", LauncherUnits.Format(1000));
            Assert.Equal("0.000125", LauncherUnits.Format(0.000125));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static readonly HashSet<string> Codes = new(StringComparer.OrdinalIgnoreCase) { "USD", "EUR", "TRY", "GBP", "BTC" };

    [Theory]
    [InlineData("100 usd to try", 100, "USD", "TRY")]
    [InlineData("100 $ in €", 100, "USD", "EUR")]
    [InlineData("50 euro kaç tl", 50, "EUR", "TRY")]
    [InlineData("0,5 btc a usd", 0.5, "BTC", "USD")]
    public void Reads_currency_conversions(string text, double amount, string from, string to)
        => Assert.Equal((amount, from, to), LauncherUnits.TryParseCurrency(text, Codes));

    [Theory]
    [InlineData("100 usd to usd")]
    [InlineData("100 usd to xyz")]
    [InlineData("10 km to mi")]
    public void Other_text_is_not_a_currency_conversion(string text) => Assert.Null(LauncherUnits.TryParseCurrency(text, Codes));
}

public class WindowsSearchQueryTests
{
    private const string Profile = @"C:\Users\ayşe";
    private static readonly string[] Excluded = { @"C:\Users\ayşe\AppData" };

    [Fact]
    public void Searches_names_and_text_in_the_user_folder_without_AppData()
    {
        string sql = WindowsSearchQuery.Build("tax report", Profile, Excluded)!;
        Assert.StartsWith("SELECT TOP 20 ", sql);
        Assert.Contains(@"SCOPE='file:C:\Users\ayşe'", sql);
        Assert.Contains(@"AND NOT System.ItemPathDisplay LIKE 'C:\Users\ayşe\AppData\%'", sql);
        Assert.Contains("System.ItemNameDisplay LIKE 'tax report%'", sql);
        Assert.Contains("CONTAINS(*, '\"tax*\" AND \"report*\"')", sql);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("  a  ")]
    [InlineData("\"*\"")]
    public void Too_little_to_search_gives_no_query(string text) => Assert.Null(WindowsSearchQuery.Build(text, Profile, Excluded));

    [Fact]
    public void Quotes_cannot_end_a_string()
    {
        string sql = WindowsSearchQuery.Build("x' OR 1=1 --", Profile, Excluded)!;
        Assert.Contains("LIKE 'x'' OR 1=1 --%'", sql);
        // Every quote typed is doubled, so the literals still pair up.
        Assert.Equal(0, sql.Replace("''", "").Count(c => c == '\'') % 2);
        Assert.DoesNotContain("x' OR", sql.Replace("''", "\u0001"));
    }

    [Fact]
    public void Wildcards_are_matched_as_typed()
    {
        Assert.Equal("100[%] [_]done[[]1]", WindowsSearchQuery.LikeText("100% _done[1]"));
        string sql = WindowsSearchQuery.Build("50%_off", Profile, Excluded)!;
        Assert.Contains("LIKE '50[%][_]off%'", sql);
    }

    [Fact]
    public void Contains_terms_drop_their_operators()
    {
        Assert.Equal("\"ab*\" AND \"cd*\"", WindowsSearchQuery.ContainsTerms("a\"b* (cd)"));
        string sql = WindowsSearchQuery.Build("it's \"quoted\"", Profile, Excluded)!;
        Assert.Contains("CONTAINS(*, '\"it''s*\" AND \"quoted*\"')", sql);
    }

    [Fact]
    public void A_scope_with_a_quote_stays_one_literal()
        => Assert.Contains(@"SCOPE='file:C:\Users\o''brien'", WindowsSearchQuery.Build("report", @"C:\Users\o'brien", Array.Empty<string>())!);

    [Fact]
    public void Line_breaks_become_spaces() => Assert.Contains("LIKE 'tax report%'", WindowsSearchQuery.Build("tax\r\nreport", Profile, Excluded)!);
}

public class EmojiIndexTests
{
    private const string Json = """
        [{"e":"😀","en":"grinning face|face|happy","tr":"sırıtan yüz|mutlu"},
         {"e":"❤️","en":"red heart|love|heart","tr":"kırmızı kalp|aşk|kalp","de":"rotes Herz|Herz"},
         {"e":"💔","en":"broken heart|break|heart","tr":"kırık kalp|kalp"}]
        """;

    private static readonly IReadOnlyList<EmojiEntry> Entries = EmojiIndex.Parse(Json);

    [Fact]
    public void Finds_by_name_in_the_language_and_in_English()
    {
        Assert.Equal("❤️", EmojiIndex.Search(Entries, "heart", "tr", 5)[0].Emoji);
        Assert.Equal("❤️", EmojiIndex.Search(Entries, "kalp", "tr", 5)[0].Emoji);
        Assert.Equal("❤️", EmojiIndex.Search(Entries, "herz", "de", 5)[0].Emoji);
        Assert.Equal("😀", EmojiIndex.Search(Entries, "mutlu", "tr", 5).Single().Emoji);
        Assert.Empty(EmojiIndex.Search(Entries, "xyz", "tr", 5));
    }

    [Fact]
    public void Names_fall_back_to_English()
    {
        Assert.Equal("kırık kalp", Entries[2].NameIn("tr"));
        Assert.Equal("broken heart", Entries[2].NameIn("de"));
    }

    [Fact]
    public void The_bundled_list_loads_in_every_language()
    {
        var entries = EmojiIndex.Entries;
        Assert.True(entries.Count > 1500);
        foreach (var language in new[] { "en", "tr", "de", "es" })
            Assert.True(entries.Count(e => e.Names.ContainsKey(language)) > 1500, language);
        Assert.Contains(EmojiIndex.Search(entries, "kalp", "tr", 24), e => e.Emoji == "❤️");
        Assert.Contains(EmojiIndex.Search(entries, "heart", "en", 24), e => e.Emoji == "❤️");
        Assert.Equal("🇹🇷", EmojiIndex.Search(entries, "bayrak türkiye", "tr", 24)[0].Emoji);
        Assert.Equal("🔥", EmojiIndex.Search(entries, "fuego", "es", 24)[0].Emoji);
    }
}

public class CurrencyConversionTests
{
    private static CurrencyQuote Quote(string @base, string target, double rate, bool crypto = false)
        => new(@base, target, rate, null, new[] { rate }, DateTime.Today, crypto);

    [Fact]
    public void Currencies_and_coins_are_asked_the_right_way()
    {
        static string Show((string Base, string[] Targets) request) => request.Base + ":" + string.Join(",", request.Targets);
        Assert.Equal("USD:TRY", Show(CurrencyConversion.Request("USD", "TRY")));
        Assert.Equal("EUR:BTC", Show(CurrencyConversion.Request("BTC", "EUR")));
        Assert.Equal("EUR:BTC", Show(CurrencyConversion.Request("EUR", "BTC")));
        Assert.Equal("USD:BTC,ETH", Show(CurrencyConversion.Request("BTC", "ETH")));
    }

    [Fact]
    public void Converts_with_the_quotes()
    {
        Assert.Equal(3400, CurrencyConversion.Convert(100, "USD", "TRY", new[] { Quote("USD", "TRY", 34) }));
        Assert.Equal(30000, CurrencyConversion.Convert(0.5, "BTC", "EUR", new[] { Quote("BTC", "EUR", 60000, true) }));
        Assert.Equal(0.5, CurrencyConversion.Convert(30000, "EUR", "BTC", new[] { Quote("BTC", "EUR", 60000, true) }));
        Assert.Equal(20, CurrencyConversion.Convert(1, "BTC", "ETH", new[] { Quote("BTC", "USD", 60000, true), Quote("ETH", "USD", 3000, true) }));
        Assert.Null(CurrencyConversion.Convert(1, "USD", "TRY", Array.Empty<CurrencyQuote>()));
    }
}

public class LauncherRankingTests
{
    [Fact]
    public void The_selection_follows_its_result()
    {
        string a = "a", b = "b", c = "c", d = "d";
        Assert.Equal(2, LauncherRanking.SelectionAfter(new[] { a, b, c }, 1, new[] { d, a, b, c }));
        Assert.Equal(0, LauncherRanking.SelectionAfter(new[] { a, b }, 1, new[] { a, c }));
        Assert.Equal(0, LauncherRanking.SelectionAfter(new[] { a }, -1, new[] { b }));
        Assert.Equal(-1, LauncherRanking.SelectionAfter(new[] { a }, 0, Array.Empty<string>()));
    }
}
