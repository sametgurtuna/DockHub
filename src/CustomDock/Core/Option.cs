namespace CustomDock.Core;

/// <summary>ComboBox'larda değer + görünen ad çifti.</summary>
public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public static class Options
{
    public static IReadOnlyList<Option<int>> Hours(int from, int to)
        => Enumerable.Range(from, to - from + 1).Select(h => new Option<int>(h, $"{h:00}:00")).ToList();

    public static IReadOnlyList<Option<int>> Of(params (int Value, string Label)[] items)
        => items.Select(i => new Option<int>(i.Value, i.Label)).ToList();
}
