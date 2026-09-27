using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Dock;

/// <summary>The displays a widget can be moved to: the main dock and every other display with a dock of its own.</summary>
public static class WidgetDisplays
{
    public sealed record Choice(string? Device, string Label);

    /// <summary>Empty unless "Show on all displays" is on and there is more than one display.</summary>
    public static IReadOnlyList<Choice> Choices()
    {
        var config = AppServices.Config;
        if (!config.ShowOnAllDisplays) return Array.Empty<Choice>();
        var monitors = MonitorHelper.GetAll();
        if (monitors.Count < 2) return Array.Empty<Choice>();

        string main = MonitorHelper.GetPreferred(config.MonitorDevice).DeviceName;
        var choices = new List<Choice> { new(null, L.T("Main dock")) };
        foreach (var monitor in monitors.Where(m => !string.Equals(m.DeviceName, main, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new(monitor.DeviceName, L.T("Display {0}", monitor.Index)));
        return choices;
    }

    /// <summary>The choice a widget is on (the main dock when its display isn't connected).</summary>
    public static Choice? Current(IReadOnlyList<Choice> choices, DockItem item)
        => choices.FirstOrDefault(c => c.Device is not null && string.Equals(c.Device, item.Display, StringComparison.OrdinalIgnoreCase))
           ?? choices.FirstOrDefault();

    /// <summary>Moves a widget to a display's dock (null: the main dock).</summary>
    public static void Move(DockItem item, string? device)
    {
        if (string.Equals(item.Display, device, StringComparison.OrdinalIgnoreCase)) return;
        item.Display = device;
        AppServices.ConfigService.ScheduleSave();
        AppServices.Config.NotifyItemsChanged();
    }
}
