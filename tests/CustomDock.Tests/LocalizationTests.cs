using System.Text.Json;
using System.Text.RegularExpressions;
using CustomDock.Core;

namespace CustomDock.Tests;

public class LocalizationTests
{
    private static readonly Regex Placeholder = new(@"\{\d+[^}]*\}");

    private static Dictionary<string, string> Load(string code)
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream($"CustomDock.Resources.Strings_{code}.json");
        Assert.True(stream is not null, $"Strings_{code}.json is not embedded");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream!, new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        })!;
    }

    public static TheoryData<string> TranslatedCodes()
    {
        var data = new TheoryData<string>();
        foreach (var language in L.Languages.Where(l => l.Code != "en")) data.Add(language.Code);
        return data;
    }

    [Theory]
    [MemberData(nameof(TranslatedCodes))]
    public void Strings_load_and_keep_their_placeholders(string code)
    {
        var strings = Load(code);
        Assert.True(strings.Count > 500);
        foreach (var (english, translated) in strings)
        {
            var expected = Placeholder.Matches(english).Select(m => m.Value).OrderBy(v => v);
            var actual = Placeholder.Matches(translated).Select(m => m.Value).OrderBy(v => v);
            Assert.True(expected.SequenceEqual(actual), $"{code}: placeholders differ: \"{english}\" -> \"{translated}\"");
            Assert.False(string.IsNullOrWhiteSpace(translated), $"{code}: empty translation for \"{english}\"");
            Assert.DoesNotContain("—", translated);
            Assert.Equal(english.Count(c => c == '\n'), translated.Count(c => c == '\n'));
        }
    }

    [Theory]
    [MemberData(nameof(TranslatedCodes))]
    public void Every_language_translates_the_same_texts_as_Turkish(string code)
    {
        var turkish = Load("tr").Keys.ToHashSet();
        var keys = Load(code).Keys.ToHashSet();
        Assert.Empty(turkish.Except(keys).Order());
        Assert.Empty(keys.Except(turkish).Order());
    }

    [Theory]
    [InlineData(UiLanguage.English, "tr", "en")]
    [InlineData(UiLanguage.Turkish, "en", "tr")]
    [InlineData(UiLanguage.German, "tr", "de")]
    [InlineData(UiLanguage.Spanish, "en", "es")]
    [InlineData(UiLanguage.System, "tr", "tr")]
    [InlineData(UiLanguage.System, "de", "de")]
    [InlineData(UiLanguage.System, "ES", "es")]
    [InlineData(UiLanguage.System, "fr", "en")]
    [InlineData(UiLanguage.System, "", "en")]
    public void The_language_code_follows_the_setting_or_Windows(UiLanguage language, string windows, string expected)
    {
        Assert.Equal(expected, L.CodeFor(language, windows));
    }

    [Fact]
    public void Every_language_has_its_own_name_and_code()
    {
        Assert.Equal(L.Languages.Count, L.Languages.Select(l => l.Code).Distinct().Count());
        Assert.Equal(Enum.GetValues<UiLanguage>().Length - 1, L.Languages.Count); // all but System
        Assert.Contains(L.Languages, l => l.Language == UiLanguage.German && l.NativeName == "Deutsch");
        Assert.Contains(L.Languages, l => l.Language == UiLanguage.Spanish && l.NativeName == "Español");
    }

    [Fact]
    public void English_is_returned_when_no_translation_is_loaded()
    {
        Assert.Equal("Some text", L.T("Some text"));
        Assert.Equal("Removed 3", L.T("Removed {0}", 3));
    }
}
