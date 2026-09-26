namespace CustomDock.Core;

/// <summary>A ready-made look: appearance settings plus a set of widgets. Pinned apps and folders are always kept.</summary>
public sealed record LayoutPreset(
    string Id,
    string Name,
    string Description,
    Action<AppConfig> Appearance,
    IReadOnlyList<(string Widget, string Variant)> Widgets);

public static class LayoutPresets
{
    public static IReadOnlyList<LayoutPreset> All { get; } = new[]
    {
        new LayoutPreset("minimal", "Minimal", "A slim floating dock with just your apps and the clock.",
            c =>
            {
                c.Edge = DockEdge.Bottom; c.Layout = DockLayout.Floating; c.WidthMode = DockWidthMode.Fit;
                c.Alignment = DockAlignment.Center; c.Size = DockSize.Small; c.Backdrop = BackdropKind.Blur;
                c.TintOpacity = 0.45; c.ShowSearchButton = false; c.ShowTaskViewButton = false; c.ShowClock = true;
            },
            Array.Empty<(string, string)>()),

        new LayoutPreset("mac", "macOS style", "Centered, rounded and roomy, with the time and weather at hand.",
            c =>
            {
                c.Edge = DockEdge.Bottom; c.Layout = DockLayout.Floating; c.WidthMode = DockWidthMode.Fit;
                c.Alignment = DockAlignment.Center; c.Size = DockSize.Medium; c.Backdrop = BackdropKind.Blur;
                c.TintOpacity = 0.35; c.EdgeMargin = 8; c.ShowSearchButton = false; c.ShowTaskViewButton = false;
            },
            new[] { ("clock", "digital"), ("weather", "current"), ("media", "compact") }),

        new LayoutPreset("dashboard", "Dashboard", "A full-width bar packed with live information.",
            c =>
            {
                c.Edge = DockEdge.Bottom; c.Layout = DockLayout.Attached; c.WidthMode = DockWidthMode.Full;
                c.Alignment = DockAlignment.Start; c.Size = DockSize.Large; c.Backdrop = BackdropKind.Acrylic;
                c.ShowSearchButton = true; c.ShowClock = true;
            },
            new[] { ("clock", "calendar"), ("weather", "hourly"), ("system", "rings"), ("media", "full"), ("reminders", "next") }),

        new LayoutPreset("vertical", "Side bar", "A vertical dock on the left with compact widget tiles.",
            c =>
            {
                c.Edge = DockEdge.Left; c.Layout = DockLayout.Floating; c.WidthMode = DockWidthMode.Fit;
                c.Alignment = DockAlignment.Center; c.Size = DockSize.Medium; c.Backdrop = BackdropKind.Blur;
            },
            new[] { ("clock", "analog"), ("weather", "current"), ("system", "rings"), ("hydration", "timer") }),
    };

    /// <summary>
    /// Applies a preset as one undoable step. Apps, folders and separators stay; the widgets become the preset's,
    /// reusing existing widgets of the same type so their settings and data (notes, reminders) are kept.
    /// </summary>
    public static void Apply(LayoutPreset preset, ConfigService service)
    {
        var config = service.Config;
        service.History.Push(config, L.T("Applied the {0} layout", preset.Name), destructive: true, includeAppearance: true);

        var kept = config.Items.Where(i => i.Kind != DockItemKind.Widget).ToList();
        var widgetsByType = config.Items.Where(i => i.Kind == DockItemKind.Widget)
            .GroupBy(i => i.Widget ?? "").ToDictionary(g => g.Key, g => new Queue<DockItem>(g));

        var widgets = new List<DockItem>();
        foreach (var (type, variant) in preset.Widgets)
        {
            var item = widgetsByType.TryGetValue(type, out var queue) && queue.Count > 0 ? queue.Dequeue() : DockItem.ForWidget(type);
            item.Variant = variant;
            item.PinnedEnd = false;
            widgets.Add(item);
        }

        // Trailing separators look odd without widgets after them.
        while (kept.Count > 0 && kept[^1].Kind == DockItemKind.Separator) kept.RemoveAt(kept.Count - 1);
        if (widgets.Count > 0 && kept.Count > 0) kept.Add(DockItem.Separator());
        kept.AddRange(widgets);

        preset.Appearance(config);
        config.Items = kept;
        config.NotifyItemsChanged();
        Log.Info($"Layout preset applied: {preset.Id}");
    }
}
