using System.Globalization;
using System.Text;
using CustomDock.Core;
using CustomDock.Widgets;
using CustomDock.Widgets.Web;

namespace CustomDock.Settings;

/// <summary>One card of the widget gallery: a built-in or installed web widget, or one from the community list.</summary>
/// <param name="Id">Widget type id (the community list's id for community widgets).</param>
/// <param name="Name">Name in the interface language.</param>
/// <param name="Description">Description in the interface language.</param>
/// <param name="Category">Category key (<see cref="WidgetCategories"/>, or <see cref="GalleryFilter.Community"/>).</param>
/// <param name="Keywords">More text that finds the card: the English name and description, the category, layout names.</param>
public sealed record GalleryEntry(string Id, string Name, string Description, string Category, IReadOnlyList<string> Keywords)
{
    public bool IsCommunity => Category == GalleryFilter.Community;

    /// <summary>The card of a registered widget (built in, or an installed web widget).</summary>
    public static GalleryEntry From(WidgetDescriptor descriptor)
    {
        var keywords = new List<string> { descriptor.EnglishName, descriptor.EnglishDescription, descriptor.Category, L.T(descriptor.Category) };
        foreach (var variant in descriptor.Variants)
        {
            keywords.Add(variant.EnglishName);
            keywords.Add(variant.Name);
        }
        return new GalleryEntry(descriptor.Id, descriptor.Name, descriptor.Description, descriptor.Category, keywords);
    }
}

/// <summary>Search and category filter of the widget gallery (Settings › Widget gallery).</summary>
public static class GalleryFilter
{
    /// <summary>Category of the widgets from the community list.</summary>
    public const string Community = "Community";

    /// <summary>
    /// The entries in <paramref name="category"/> (null or empty: all) that contain every word of
    /// <paramref name="query"/> in their name, description, category or keywords, ignoring case and accents (so
    /// "dunya" finds "Dünya"). Names that start with the query come first, then names that contain every word, then the
    /// rest; otherwise the order stays.
    /// </summary>
    public static IReadOnlyList<GalleryEntry> Apply(IEnumerable<GalleryEntry> entries, string? query, string? category)
    {
        string folded = Fold(query?.Trim() ?? "");
        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = entries
            .Where(e => string.IsNullOrEmpty(category) || e.Category == category)
            .Select(e => (Entry: e, Name: Fold(e.Name), Text: Fold(e.Description + " " + string.Join(" ", e.Keywords))))
            .Where(e => words.All(w => e.Name.Contains(w, StringComparison.Ordinal) || e.Text.Contains(w, StringComparison.Ordinal)))
            .ToList();
        if (words.Length == 0) return matches.Select(m => m.Entry).ToList();

        int Rank((GalleryEntry Entry, string Name, string Text) m) =>
            m.Name.StartsWith(folded, StringComparison.Ordinal) ? 0
            : words.All(w => m.Name.Contains(w, StringComparison.Ordinal)) ? 1
            : 2;
        // OrderBy is stable: the gallery order stays within each rank.
        return matches.OrderBy(Rank).Select(m => m.Entry).ToList();
    }

    /// <summary>
    /// Lower case without accents, the same in every language: Turkish sorts "ü" as a letter of its own, but a search
    /// typed without it should still find the word.
    /// </summary>
    internal static string Fold(string text)
    {
        var decomposed = text.Replace('ı', 'i').Replace('İ', 'i').ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) folded.Append(c);
        return folded.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>How many copies of the widget are on the dock, folders included.</summary>
    public static int CountOnDock(IEnumerable<DockItem> items, string widgetId) =>
        ItemDataStore.Flatten(items).Count(i => i.Kind == DockItemKind.Widget && string.Equals(i.Widget, widgetId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What an installed web widget may reach, in a few words for its card ("Internet: api.example.com ·
    /// Notifications"); <paramref name="settingLabel"/> names a setting that holds a server address.
    /// </summary>
    public static string PermissionSummary(WebWidgetPermissions permissions, Func<string, string> settingLabel)
    {
        var parts = new List<string>();
        if (permissions.Network.Count > 0) parts.Add(L.T("Internet: {0}", string.Join(", ", permissions.Network)));
        foreach (string key in permissions.NetworkFromSettings)
            parts.Add(L.T("Internet: the address in “{0}”", settingLabel(key)));
        if (permissions.Notifications) parts.Add(L.T("Notifications"));
        return parts.Count == 0 ? L.T("No internet access, no notifications") : string.Join(" · ", parts);
    }

    /// <summary>The width class the gallery shows for a layout (one without a class is a standard card).</summary>
    public static WidgetWidth DisplayWidth(WidgetWidth width) => width == WidgetWidth.Auto ? WidgetWidth.Standard : width;
}
