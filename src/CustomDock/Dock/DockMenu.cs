using System.Windows.Controls;

namespace CustomDock.Dock;

/// <summary>Shortcuts for context menu items.</summary>
public static class DockMenu
{
    public static MenuItem Item(string header, string? glyph, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (glyph is not null)
            item.Icon = new TextBlock { Text = glyph };
        item.Click += (_, _) => action();
        return item;
    }

    public static MenuItem Check(string header, bool isChecked, Action action)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked };
        item.Click += (_, _) => action();
        return item;
    }

    public static MenuItem Header(string text) => new() { Header = text, IsEnabled = false };

    public static MenuItem Submenu(string header, string? glyph, IEnumerable<MenuItem> children)
    {
        var item = new MenuItem { Header = header };
        if (glyph is not null)
            item.Icon = new TextBlock { Text = glyph };
        foreach (var child in children)
            item.Items.Add(child);
        return item;
    }

    public static Separator Separator() => new();
}
