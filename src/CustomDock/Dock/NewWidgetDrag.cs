namespace CustomDock.Dock;

/// <summary>
/// Drag data of a widget dragged from the gallery onto the dock (<see cref="DockDragHelper.NewWidgetFormat"/>): the
/// widget type id, and the layout after a line break (neither can contain one).
/// </summary>
public static class NewWidgetDrag
{
    public static string Encode(string widgetId, string? variant) =>
        string.IsNullOrEmpty(variant) ? widgetId : widgetId + "\n" + variant;

    /// <summary>Reads <see cref="Encode"/> data; null when it isn't any. An empty layout means the default one.</summary>
    public static (string WidgetId, string? Variant)? Decode(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        int split = data.IndexOf('\n');
        string id = (split < 0 ? data : data[..split]).Trim();
        string? variant = split < 0 ? null : data[(split + 1)..].Trim();
        if (id.Length == 0) return null;
        return (id, string.IsNullOrEmpty(variant) ? null : variant);
    }
}
