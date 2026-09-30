using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Dock;

/// <summary>
/// Where a widget can be moved: the main dock, the top bar (while it is on) and every other display with a dock of its
/// own. Used by the widget's "Show on" menu and Settings › Dock items.
/// </summary>
public static class WidgetDisplays
{
    /// <param name="Device">The display's dock (null: the main dock or the bar).</param>
    /// <param name="Bar">The top bar.</param>
    public sealed record Choice(string? Device, string Label, bool Bar = false);

    /// <summary>Empty unless there is somewhere else to go: the top bar, or other displays with "Show on all displays".</summary>
    public static IReadOnlyList<Choice> Choices()
    {
        var config = AppServices.Config;
        var choices = new List<Choice> { new(null, L.T("Main dock")) };
        if (DockWindow.BarShown) choices.Add(new(null, L.T("Top bar"), Bar: true));
        if (config.ShowOnAllDisplays)
        {
            var monitors = MonitorHelper.GetAll();
            string main = MonitorHelper.GetPreferred(config.MonitorDevice).DeviceName;
            foreach (var monitor in monitors.Where(m => !string.Equals(m.DeviceName, main, StringComparison.OrdinalIgnoreCase)))
                choices.Add(new(monitor.DeviceName, L.T("Display {0}", monitor.Index)));
        }
        return choices.Count > 1 ? choices : Array.Empty<Choice>();
    }

    /// <summary>The choice a widget is on (the main dock when its display isn't connected or the bar is off).</summary>
    public static Choice? Current(IReadOnlyList<Choice> choices, DockItem item)
    {
        // A bar widget while the bar is off shows on the main dock, whatever its display.
        if (IsOnBar(item)) return choices.FirstOrDefault(c => c.Bar) ?? choices.FirstOrDefault();
        return choices.FirstOrDefault(c => c.Device is not null && string.Equals(c.Device, item.Display, StringComparison.OrdinalIgnoreCase))
               ?? choices.FirstOrDefault();
    }

    public static bool IsOnBar(DockItem item) => string.Equals(item.Surface, DockItem.BarSurface, StringComparison.OrdinalIgnoreCase);

    /// <summary>Moves a widget to the top bar or a display's dock.</summary>
    public static void Move(DockItem item, Choice choice)
    {
        string? surface = choice.Bar ? DockItem.BarSurface : null;
        string? device = choice.Bar ? null : choice.Device;
        if (IsOnBar(item) == choice.Bar && string.Equals(item.Display, device, StringComparison.OrdinalIgnoreCase)) return;
        item.Surface = surface;
        item.Display = device;
        AppServices.ConfigService.ScheduleSave();
        AppServices.Config.NotifyItemsChanged();
    }
}
