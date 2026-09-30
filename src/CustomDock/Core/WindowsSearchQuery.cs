using System.Text;

namespace CustomDock.Core;

/// <summary>
/// The Windows Search (SystemIndex) query of the launcher's file search: files and folders under the user's folder
/// whose name starts with what was typed or whose text contains its words, newest match first. Everything typed goes
/// into the SQL only through <see cref="Literal"/>, <see cref="LikeText"/> and <see cref="ContainsTerms"/>.
/// </summary>
public static class WindowsSearchQuery
{
    public const int MaxResults = 20;

    /// <summary>Shorter text isn't searched (too many matches, and the query runs on each keystroke).</summary>
    public const int MinLength = 3;

    /// <summary>The query, or null when there is nothing to search for.</summary>
    /// <param name="scope">Folder searched, with everything under it (the user's folder).</param>
    /// <param name="excluded">Folders left out (AppData).</param>
    public static string? Build(string text, string scope, IEnumerable<string> excluded)
    {
        string query = Clean(text);
        if (query.Length < MinLength || ContainsTerms(query) is not { } terms) return null;

        var sql = new StringBuilder();
        sql.Append("SELECT TOP ").Append(MaxResults)
            .Append(" System.ItemPathDisplay, System.ItemNameDisplay, System.DateModified, System.ItemType FROM SystemIndex WHERE ");
        sql.Append("SCOPE='").Append(Literal("file:" + scope)).Append('\'');
        foreach (var folder in excluded)
            sql.Append(" AND NOT System.ItemPathDisplay LIKE '").Append(Literal(LikeText(folder.TrimEnd('\\') + "\\"))).Append("%'");
        sql.Append(" AND (System.ItemNameDisplay LIKE '").Append(Literal(LikeText(query))).Append("%'");
        sql.Append(" OR CONTAINS(*, '").Append(Literal(terms)).Append("'))");
        sql.Append(" ORDER BY System.Search.Rank DESC");
        return sql.ToString();
    }

    /// <summary>Text inside a SQL string literal ('...'): a single quote is doubled.</summary>
    public static string Literal(string text) => text.Replace("'", "''");

    /// <summary>Text that LIKE matches as typed: its wildcards (%, _) and [ are put in brackets.</summary>
    public static string LikeText(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '%' or '_' or '[') builder.Append('[').Append(c).Append(']');
            else builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>
    /// The words as CONTAINS prefix terms, all required: <c>"word1*" AND "word2*"</c>. Characters with a meaning in
    /// CONTAINS (quotes, *, parentheses) are dropped from the words; null when no word is left.
    /// </summary>
    public static string? ContainsTerms(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(c => c is not ('"' or '*' or '(' or ')')).ToArray()))
            .Where(w => w.Length > 0)
            .Take(8)
            .ToList();
        return words.Count == 0 ? null : string.Join(" AND ", words.Select(w => "\"" + w + "*\""));
    }

    /// <summary>The typed text on one line: control characters become spaces, runs of spaces one space.</summary>
    private static string Clean(string text)
    {
        var chars = text.Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
