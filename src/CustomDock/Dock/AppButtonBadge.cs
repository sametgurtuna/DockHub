using System.Text.RegularExpressions;

namespace CustomDock.Dock;

/// <summary>
/// Taskbar-style badge read from an app's window titles: a count such as "(3) Inbox" or "Chat [12+]", or a dot for
/// titles marked with "•" or "*" (unsaved or unread).
/// </summary>
public static class AppButtonBadge
{
    private static readonly Regex Count = new(
        @"(?:^|[\(\[])\s*(\d{1,4}\+?)\s*(?:[\)\]]|$)|[\(\[]\s*(\d{1,4}\+?)\s*[\)\]]",
        RegexOptions.Compiled);

    /// <summary>The first count found in any title, and whether some title carries a dot marker.</summary>
    public static (string? Count, bool Dot) FromTitles(IEnumerable<string?> titles)
    {
        bool dot = false;
        foreach (var title in titles)
        {
            if (string.IsNullOrWhiteSpace(title)) continue;

            var match = Count.Match(title);
            if (match.Success)
                return (!string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value, dot);

            if (title.StartsWith("•") || title.StartsWith("*") || title.Contains(" • ") || title.Contains(" * "))
                dot = true;
        }
        return (null, dot);
    }
}
