namespace CustomDock.Core;

/// <summary>Keeps the launcher's selection on the same result while slower results (files, rates) arrive.</summary>
public static class LauncherRanking
{
    /// <summary>
    /// The index to select once the results changed from <paramref name="before"/> to <paramref name="after"/>: the
    /// result that was selected, where it is now; the first result when it is gone; -1 when there are none.
    /// </summary>
    public static int SelectionAfter<T>(IReadOnlyList<T> before, int selected, IReadOnlyList<T> after) where T : class
    {
        if (after.Count == 0) return -1;
        if (selected < 0 || selected >= before.Count) return 0;
        for (int i = 0; i < after.Count; i++)
            if (ReferenceEquals(after[i], before[selected])) return i;
        return 0;
    }
}
