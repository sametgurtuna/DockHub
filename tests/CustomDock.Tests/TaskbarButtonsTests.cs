using CustomDock.Core;
using CustomDock.Dock;

namespace CustomDock.Tests;

public class TaskbarButtonsTests
{
    private static readonly HashSet<string> NoFolders = new();

    private static RunningApp<string> App(string key, long order, params string[] windows) => new(key, order, windows);

    /// <summary>The buttons as text: "pin:id=window", "+id=window" (one more window of a pin), "run:key=window".</summary>
    private static string[] Show(IReadOnlyList<TaskbarButton<string>> buttons) => buttons.Select(b => b.Slot switch
    {
        TaskbarSlot.Pinned => $"pin:{b.PinId}={b.Window}",
        TaskbarSlot.PinnedWindow => $"+{b.PinId}={b.Window}",
        _ => $"run:{b.Key}={b.Window}",
    }).ToArray();

    private static readonly (string Id, string Key)[] Pins = { ("edge", "exe:edge"), ("code", "exe:code") };

    private static readonly RunningApp<string>[] Running =
    {
        App("exe:notepad", 3, "n1"),
        App("exe:edge", 1, "e1", "e2"),
        App("exe:explorer", 2, "x1", "x2"),
    };

    [Fact]
    public void Combined_each_app_has_one_button_as_before()
    {
        var buttons = TaskbarButtons.Layout(Pins, NoFolders, Running, CombineButtons.Always);
        Assert.Equal(new[] { "pin:edge=", "pin:code=", "run:exe:explorer=", "run:exe:notepad=" }, Show(buttons));
        Assert.Equal("exe:edge", buttons[0].Key);
    }

    [Fact]
    public void Not_combined_every_window_has_a_button_and_an_apps_windows_stay_together()
    {
        var buttons = TaskbarButtons.Layout(Pins, NoFolders, Running, CombineButtons.Never);
        Assert.Equal(new[]
        {
            "pin:edge=e1", "+edge=e2", // the pinned app's button becomes its first window's; the others follow it
            "pin:code=",               // not running: just the pin
            "run:exe:explorer=x1", "run:exe:explorer=x2", "run:exe:notepad=n1",
        }, Show(buttons));
    }

    [Fact]
    public void A_closed_window_takes_only_its_own_button_away()
    {
        var before = Show(TaskbarButtons.Layout(Pins, NoFolders, Running, CombineButtons.Never));
        var running = new[] { App("exe:notepad", 3, "n1"), App("exe:edge", 1, "e2"), App("exe:explorer", 2, "x1", "x2") };
        var after = Show(TaskbarButtons.Layout(Pins, NoFolders, running, CombineButtons.Never));
        Assert.Equal(new[] { "pin:edge=e2", "pin:code=", "run:exe:explorer=x1", "run:exe:explorer=x2", "run:exe:notepad=n1" }, after);
        Assert.Equal(before.Skip(2), after.Skip(1));
    }

    [Fact]
    public void Apps_keep_their_order_when_windows_come_and_go()
    {
        // Apps are ordered by when they were first seen, not by their windows.
        var running = new[] { App("exe:b", 2, "b1"), App("exe:a", 1, "a1", "a2", "a3"), App("exe:c", 3, "c1") };
        var buttons = Show(TaskbarButtons.Layout(Array.Empty<(string, string)>(), NoFolders, running, CombineButtons.Never));
        Assert.Equal(new[] { "run:exe:a=a1", "run:exe:a=a2", "run:exe:a=a3", "run:exe:b=b1", "run:exe:c=c1" }, buttons);
    }

    [Fact]
    public void Apps_in_folders_get_no_button_of_their_own()
    {
        var folders = new HashSet<string> { "exe:explorer" };
        foreach (var mode in new[] { CombineButtons.Always, CombineButtons.Never })
            Assert.DoesNotContain(TaskbarButtons.Layout(Pins, folders, Running, mode), b => b.Key == "exe:explorer");
    }

    [Fact]
    public void An_app_pinned_twice_splits_into_its_windows_once()
    {
        var pins = new[] { ("a", "exe:edge"), ("b", "exe:edge") };
        var buttons = Show(TaskbarButtons.Layout(pins, NoFolders, Running, CombineButtons.Never));
        Assert.Equal(new[] { "pin:a=e1", "+a=e2", "pin:b=" }, buttons.Take(3));
    }

    [Fact]
    public void An_app_without_windows_here_shows_only_its_pin()
    {
        // With docks on several displays each dock gets the windows on its own display.
        var running = new[] { App("exe:edge", 1), App("exe:notepad", 2) };
        var buttons = Show(TaskbarButtons.Layout(Pins, NoFolders, running, CombineButtons.Never));
        Assert.Equal(new[] { "pin:edge=", "pin:code=" }, buttons);
    }

    [Fact]
    public void Older_settings_combine_buttons()
        => Assert.Equal(CombineButtons.Always, System.Text.Json.JsonSerializer.Deserialize<AppConfig>("{}", JsonStore.Options)!.CombineButtons);
}
