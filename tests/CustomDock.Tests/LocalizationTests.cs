using System.Text.Json;
using System.Text.RegularExpressions;
using CustomDock.Core;

namespace CustomDock.Tests;

public class LocalizationTests
{
    private static Dictionary<string, string> LoadTurkish()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("CustomDock.Resources.Strings_tr.json");
        Assert.NotNull(stream);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream!, new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        })!;
    }

    [Fact]
    public void Turkish_strings_load_and_keep_their_placeholders()
    {
        var strings = LoadTurkish();
        Assert.True(strings.Count > 500);
        var placeholder = new Regex(@"\{\d\}");
        foreach (var (english, turkish) in strings)
        {
            var expected = placeholder.Matches(english).Select(m => m.Value).OrderBy(v => v);
            var actual = placeholder.Matches(turkish).Select(m => m.Value).OrderBy(v => v);
            Assert.True(expected.SequenceEqual(actual), $"Placeholders differ: \"{english}\" -> \"{turkish}\"");
            Assert.False(string.IsNullOrWhiteSpace(turkish), $"Empty translation for \"{english}\"");
            Assert.DoesNotContain("—", turkish);
        }
    }

    [Fact]
    public void English_is_returned_when_no_translation_is_loaded()
    {
        Assert.Equal("Some text", L.T("Some text"));
        Assert.Equal("Removed 3", L.T("Removed {0}", 3));
    }
}
