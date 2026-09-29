namespace CustomDock.Widgets;

/// <summary>
/// Resizing a widget in the dock's edit mode: the widget switches to another of its own layouts (variants) of the next
/// smaller or larger width class, so there is no free-form width to store and every size is one the widget was
/// designed for.
/// </summary>
public static class WidgetResize
{
    /// <summary>Width classes in growing order; a layout without a class counts as a standard card.</summary>
    public static int Rank(WidgetWidth width) => width switch
    {
        WidgetWidth.Compact => 0,
        WidgetWidth.Wide => 2,
        _ => 1,
    };

    /// <summary>
    /// The layout one width class smaller (<paramref name="direction"/> &lt; 0) or larger (&gt; 0) than
    /// <paramref name="currentVariant"/>, or null when there is none. Among several layouts of that class the one the
    /// widget last had in it wins (<paramref name="remembered"/>: width rank → layout), so resizing back returns to
    /// where it started; otherwise the one listed closest to the current layout (the earlier one on a tie).
    /// </summary>
    public static string? Next(IReadOnlyList<WidgetVariant> variants, string? currentVariant, int direction,
        IReadOnlyDictionary<int, string>? remembered = null)
    {
        if (variants.Count < 2 || direction == 0) return null;
        int current = Math.Max(0, IndexOf(variants, currentVariant));
        int rank = Rank(variants[current].Width);

        var candidates = variants
            .Select((variant, index) => (Variant: variant, Index: index, Rank: Rank(variant.Width)))
            .Where(c => direction > 0 ? c.Rank > rank : c.Rank < rank)
            .ToList();
        if (candidates.Count == 0) return null;

        int target = direction > 0 ? candidates.Min(c => c.Rank) : candidates.Max(c => c.Rank);
        if (remembered?.GetValueOrDefault(target) is { } last && candidates.Any(c => c.Rank == target && c.Variant.Id == last))
            return last;
        return candidates
            .Where(c => c.Rank == target)
            .OrderBy(c => Math.Abs(c.Index - current))
            .ThenBy(c => c.Index)
            .First().Variant.Id;
    }

    public static string? Next(WidgetDescriptor descriptor, string? currentVariant, int direction,
        IReadOnlyDictionary<int, string>? remembered = null)
        => Next(descriptor.Variants, currentVariant, direction, remembered);

    /// <summary>Whether the widget has a layout of another width class to switch to.</summary>
    public static bool CanResize(IReadOnlyList<WidgetVariant> variants, string? currentVariant)
        => Next(variants, currentVariant, -1) is not null || Next(variants, currentVariant, +1) is not null;

    /// <summary>Width rank (see <see cref="Rank"/>) of the widget's current layout.</summary>
    public static int RankOf(IReadOnlyList<WidgetVariant> variants, string? currentVariant)
        => variants.Count == 0 ? Rank(WidgetWidth.Auto) : Rank(variants[Math.Max(0, IndexOf(variants, currentVariant))].Width);

    private static int IndexOf(IReadOnlyList<WidgetVariant> variants, string? id)
    {
        for (int i = 0; i < variants.Count; i++)
            if (variants[i].Id == id) return i;
        return -1;
    }
}
