using CustomDock.Core;
using CustomDock.Dock;

namespace CustomDock.Tests;

public class WidgetPlacementTests
{
    private const string Second = @"\\.\DISPLAY2";
    private const string Third = @"\\.\DISPLAY3";
    private static readonly string[] Docks = { Second, Third };

    [Fact]
    public void Unassigned_widgets_stay_on_the_main_dock()
    {
        Assert.True(WidgetPlacement.ShowsOn(null, null, Docks));
        Assert.False(WidgetPlacement.ShowsOn(null, Second, Docks));
    }

    [Fact]
    public void An_assigned_widget_shows_only_on_its_display()
    {
        Assert.False(WidgetPlacement.ShowsOn(Second, null, Docks));
        Assert.True(WidgetPlacement.ShowsOn(Second, Second, Docks));
        Assert.True(WidgetPlacement.ShowsOn(@"\\.\display2", Second, Docks)); // device names ignore case
        Assert.False(WidgetPlacement.ShowsOn(Second, Third, Docks));
    }

    [Fact]
    public void A_widget_whose_display_has_no_dock_falls_back_to_the_main_dock()
    {
        // Display unplugged, "Show on all displays" off, or it became the main display.
        Assert.True(WidgetPlacement.ShowsOn(Second, null, Array.Empty<string>()));
        Assert.True(WidgetPlacement.ShowsOn(Second, null, new[] { Third }));
        Assert.False(WidgetPlacement.ShowsOn(Second, Third, new[] { Third }));
    }

    [Fact]
    public void Every_widget_is_on_exactly_one_dock()
    {
        string?[] assignments = { null, Second, Third, @"\\.\DISPLAY9" };
        string?[] docks = { null, Second, Third };
        foreach (var assigned in assignments)
            Assert.Equal(1, docks.Count(dock => WidgetPlacement.ShowsOn(assigned, dock, Docks)));
    }

    [Fact]
    public void The_display_survives_save_and_load()
    {
        var item = new DockItem { Kind = DockItemKind.Widget, Widget = "clock", Display = Second };
        var json = System.Text.Json.JsonSerializer.Serialize(item, JsonStore.Options);
        Assert.Contains("\"display\"", json);
        Assert.Equal(Second, System.Text.Json.JsonSerializer.Deserialize<DockItem>(json, JsonStore.Options)!.Display);

        var plain = System.Text.Json.JsonSerializer.Serialize(new DockItem { Kind = DockItemKind.Widget, Widget = "clock" }, JsonStore.Options);
        Assert.DoesNotContain("display", plain);
    }
}
