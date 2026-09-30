using System.Globalization;
using System.Text.Json;

namespace CustomDock.Core;

/// <summary>An emoji and its names by language code (the first name is its short name, the rest keywords).</summary>
public sealed record EmojiEntry(string Emoji, IReadOnlyDictionary<string, string[]> Names)
{
    /// <summary>The short name in <paramref name="language"/>, or the English one.</summary>
    public string NameIn(string language)
        => Names.TryGetValue(language, out var names) && names.Length > 0 ? names[0] : Names.TryGetValue("en", out var en) && en.Length > 0 ? en[0] : Emoji;
}

/// <summary>
/// Emoji for the launcher (":heart", ":kalp"): about 1,900 emoji with their short names and keywords in English,
/// Turkish, German and Spanish from Unicode CLDR (Resources/Emoji.json, Unicode License v3). Loaded on first use.
/// </summary>
public static class EmojiIndex
{
    private static IReadOnlyList<EmojiEntry>? s_entries;

    public static IReadOnlyList<EmojiEntry> Entries => s_entries ??= Load();

    private static IReadOnlyList<EmojiEntry> Load()
    {
        try
        {
            using var stream = typeof(EmojiIndex).Assembly.GetManifestResourceStream("CustomDock.Resources.Emoji.json");
            if (stream is null) return Array.Empty<EmojiEntry>();
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load the emoji list");
            return Array.Empty<EmojiEntry>();
        }
    }

    /// <summary>The file: <c>[{"e":"😀","en":"grinning face|face|grin",...}]</c>, names separated by "|".</summary>
    public static IReadOnlyList<EmojiEntry> Parse(string json)
    {
        var entries = new List<EmojiEntry>();
        using var document = JsonDocument.Parse(json);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            string? emoji = null;
            var names = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) continue;
                string value = property.Value.GetString() ?? "";
                if (property.Name == "e") emoji = value;
                else names[property.Name] = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            if (!string.IsNullOrEmpty(emoji) && names.Count > 0) entries.Add(new EmojiEntry(emoji, names));
        }
        return entries;
    }

    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    /// <summary>
    /// The best matches for <paramref name="term"/> in <paramref name="language"/> and English: a short name that is the
    /// term, has it as a word or starts with it first, then keywords; several words ("flag turkey") each have to start a
    /// word. Equal matches: shorter names first, then the emoji order.
    /// </summary>
    public static IReadOnlyList<EmojiEntry> Search(IReadOnlyList<EmojiEntry> entries, string term, string language, int max)
    {
        var termWords = Words(term);
        if (termWords.Length == 0) return entries.Take(max).ToList();
        return entries
            .Select((entry, index) => (Entry: entry, Index: index, Score: Math.Max(Score(entry, language, termWords), Score(entry, "en", termWords))))
            .Where(m => m.Score > 0)
            .OrderByDescending(m => m.Score)
            .ThenBy(m => Words(m.Entry.NameIn(language)).Length)
            .ThenBy(m => m.Index)
            .Take(max)
            .Select(m => m.Entry)
            .ToList();
    }

    private static readonly char[] WordBreaks = { ' ', ':', ',', '(', ')', '-', '\t' };

    private static string[] Words(string text) => text.Split(WordBreaks, StringSplitOptions.RemoveEmptyEntries);

    private static bool Same(string a, string b) => Compare.Compare(a, b, Options) == 0;

    private static bool StartsWith(string text, string start) => Compare.IsPrefix(text, start, Options);

    private static int Score(EmojiEntry entry, string language, string[] termWords)
    {
        if (!entry.Names.TryGetValue(language, out var names) || names.Length == 0) return 0;
        string name = names[0];
        var words = Words(name);
        string term = string.Join(' ', termWords);
        if (Same(name, term)) return 100;
        if (termWords.Length > 1)
        {
            if (termWords.All(t => words.Any(w => StartsWith(w, t)))) return 75;
            var all = words.Concat(names.Skip(1).SelectMany(Words)).ToList();
            return termWords.All(t => all.Any(w => StartsWith(w, t))) ? 55 : 0;
        }
        if (words.Any(w => Same(w, term))) return 90;
        if (StartsWith(name, term)) return 85;
        if (words.Any(w => StartsWith(w, term))) return 80;
        int best = 0;
        foreach (var keyword in names.Skip(1))
        {
            if (Same(keyword, term)) return 70;
            if (StartsWith(keyword, term)) best = 60;
        }
        if (best == 0 && Compare.IndexOf(name, term, Options) >= 0) best = 40;
        return best;
    }
}
