using System.Globalization;
using System.Windows;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Services;

namespace CustomDock.Dock;

/// <summary>Results the launcher gets right away while typing (apps, settings, commands, unit conversion...).</summary>
public interface ILauncherSource
{
    /// <summary>Whether it has results before anything is typed.</summary>
    bool WhenEmpty { get; }

    IEnumerable<LauncherResult> Results(string query);
}

/// <summary>
/// Results that take a while (files, exchange rates): asked once typing pauses, cancelled by the next keystroke. Each
/// result carries its own <see cref="LauncherResult.Score"/>.
/// </summary>
public interface IAsyncLauncherSource
{
    Task<IReadOnlyList<LauncherResult>> SearchAsync(string query, CancellationToken token);
}

/// <summary>A source made of a function (the launcher's lists of apps, pages and commands).</summary>
public sealed class LauncherSource(Func<IEnumerable<LauncherResult>> results, bool whenEmpty = false) : ILauncherSource
{
    public bool WhenEmpty => whenEmpty;

    public IEnumerable<LauncherResult> Results(string query) => results();
}

/// <summary>"10 km to mi", "20 c in f", "5 GB kaç MB": the converted value; Enter copies it.</summary>
public sealed class UnitSource : ILauncherSource
{
    public bool WhenEmpty => false;

    public IEnumerable<LauncherResult> Results(string query)
    {
        if (LauncherUnits.TryConvert(query) is not { } conversion) yield break;
        string value = LauncherUnits.Format(conversion.Value);
        string amount = LauncherUnits.Format(LauncherUnits.Parse(query)!.Amount);
        yield return new LauncherResult
        {
            Title = $"{amount} {conversion.From.Symbol} = {value} {conversion.To.Symbol}",
            Subtitle = L.T("Press Enter to copy the result"),
            Glyph = "\uE8EF",
            Score = 1000,
            Run = _ => Clipboard.SetText(value),
        };
    }
}

/// <summary>":heart", ":kalp": emoji by name in the display language or English; Enter copies the emoji.</summary>
public sealed class EmojiSource
{
    public const char Prefix = ':';

    /// <summary>More than the other results: the list scrolls, and many emoji share a word ("heart").</summary>
    public const int MaxResults = 24;

    public static bool IsEmojiQuery(string query) => query.Length > 0 && query[0] == Prefix;

    public static IEnumerable<LauncherResult> Results(string query, int max)
    {
        string term = query.TrimStart(Prefix);
        int score = 1000;
        foreach (var entry in EmojiIndex.Search(EmojiIndex.Entries, term, L.Code, max))
        {
            string emoji = entry.Emoji;
            yield return new LauncherResult
            {
                Title = entry.NameIn(L.Code),
                Subtitle = L.T("Press Enter to copy the emoji"),
                Emoji = emoji,
                Score = score--,
                Run = _ => Clipboard.SetText(emoji),
            };
        }
    }
}

/// <summary>Files and folders from the Windows Search index; Enter opens, Ctrl+Enter shows it in its folder.</summary>
public sealed class FileSource : IAsyncLauncherSource
{
    public async Task<IReadOnlyList<LauncherResult>> SearchAsync(string query, CancellationToken token)
    {
        if (!AppServices.Config.LauncherFileSearch || query.Length < WindowsSearchQuery.MinLength) return Array.Empty<LauncherResult>();
        // A calculation or a conversion is not a file name.
        if (LauncherMath.TryEvaluate(query, out _) || LauncherUnits.Parse(query) is not null) return Array.Empty<LauncherResult>();

        var hits = await FileSearchService.SearchAsync(query, token).ConfigureAwait(true);
        var results = new List<LauncherResult>();
        foreach (var hit in hits)
        {
            string path = hit.Path;
            string folder = Path.GetDirectoryName(path) ?? "";
            // A match in the text only (not in the name) still shows, after the others.
            int score = LauncherMatch.Score(hit.Name, null, query);
            results.Add(new LauncherResult
            {
                Title = hit.Name,
                Subtitle = hit.Modified is { } date ? $"{folder} · {date.ToString("g", CultureInfo.CurrentCulture)}" : folder,
                Glyph = hit.IsFolder ? "\uE8B7" : "\uE8A5",
                Icon = () => ShellIcons.GetIcon(path, 32),
                Score = Math.Max(score, 25) - 20,
                Run = reveal =>
                {
                    if (reveal) LauncherWindow.Open("explorer.exe", $"/select,\"{path}\"");
                    else LauncherWindow.Open(path);
                },
            });
        }
        return results;
    }
}

/// <summary>"100 usd to try", "0.5 btc in eur": the amount at the latest rate; Enter copies it.</summary>
public sealed class CurrencySource : IAsyncLauncherSource
{
    public async Task<IReadOnlyList<LauncherResult>> SearchAsync(string query, CancellationToken token)
    {
        if (LauncherUnits.TryParseCurrency(query, CurrencyConversion.Codes) is not { } request) return Array.Empty<LauncherResult>();
        var (baseCurrency, targets) = CurrencyConversion.Request(request.From, request.To);
        var quotes = await CurrencyService.GetAsync(baseCurrency, targets).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        if (CurrencyConversion.Convert(request.Amount, request.From, request.To, quotes) is not { } converted)
            return Array.Empty<LauncherResult>();

        string value = LauncherUnits.Format(converted);
        var date = quotes.Select(q => q.Date).DefaultIfEmpty(DateTime.Today).Min();
        return new[]
        {
            new LauncherResult
            {
                Title = $"{LauncherUnits.Format(request.Amount)} {request.From} = {value} {request.To}",
                Subtitle = L.T("Rate of {0}. Press Enter to copy the result.", date.ToString("d", CultureInfo.CurrentCulture)),
                Glyph = "\uE8C7",
                Score = 1000,
                Run = _ => Clipboard.SetText(value),
            },
        };
    }
}
