using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Widgets;

namespace CustomDock.Tests;

public class WidgetResizeTests
{
    private static readonly IReadOnlyList<WidgetVariant> Weather = new[]
    {
        new WidgetVariant("current", "Current", WidgetWidth.Standard),
        new WidgetVariant("conditions", "Conditions", WidgetWidth.Standard),
        new WidgetVariant("hourly", "Hourly", WidgetWidth.Wide),
        new WidgetVariant("tile", "Tile", WidgetWidth.Compact),
    };

    [Theory]
    [InlineData("current", 1, "hourly")]
    [InlineData("current", -1, "tile")]
    [InlineData("conditions", 1, "hourly")]
    [InlineData("hourly", -1, "conditions")] // the closer of the two standard layouts
    [InlineData("tile", 1, "conditions")]
    [InlineData("hourly", 1, null)]
    [InlineData("tile", -1, null)]
    public void Resizing_steps_one_width_class(string current, int direction, string? expected)
    {
        Assert.Equal(expected, WidgetResize.Next(Weather, current, direction));
    }

    [Fact]
    public void Resizing_back_returns_to_the_layout_it_started_from()
    {
        var variants = new[]
        {
            new WidgetVariant("a", "A", WidgetWidth.Compact),
            new WidgetVariant("b", "B", WidgetWidth.Standard),
            new WidgetVariant("c", "C", WidgetWidth.Wide),
        };
        string wider = WidgetResize.Next(variants, "b", 1)!;
        Assert.Equal("b", WidgetResize.Next(variants, wider, -1));
    }

    [Fact]
    public void Layouts_without_a_width_count_as_standard()
    {
        var variants = new[]
        {
            new WidgetVariant("full", "Full"),
            new WidgetVariant("mini", "Mini", WidgetWidth.Compact),
        };
        Assert.Equal("mini", WidgetResize.Next(variants, "full", -1));
        Assert.Null(WidgetResize.Next(variants, "full", 1));
        Assert.Equal(WidgetResize.Rank(WidgetWidth.Standard), WidgetResize.RankOf(variants, "full"));
    }

    [Fact]
    public void Resizing_back_prefers_the_layout_the_widget_had_in_that_width()
    {
        var remembered = new Dictionary<int, string>();
        string wider = WidgetResize.Next(Weather, "current", 1, remembered)!;
        remembered[WidgetResize.RankOf(Weather, "current")] = "current";
        Assert.Equal("hourly", wider);
        // Without the memory the closer standard layout ("conditions") would win.
        Assert.Equal("current", WidgetResize.Next(Weather, wider, -1, remembered));
        Assert.Equal("conditions", WidgetResize.Next(Weather, wider, -1));
        // A remembered layout of another width is ignored.
        Assert.Equal("tile", WidgetResize.Next(Weather, "current", -1, remembered));
    }

    [Fact]
    public void An_unknown_or_missing_layout_starts_from_the_first()
    {
        Assert.Equal("hourly", WidgetResize.Next(Weather, null, 1));
        Assert.Equal("hourly", WidgetResize.Next(Weather, "gone", 1));
    }

    [Fact]
    public void Widgets_whose_layouts_share_one_width_cannot_be_resized()
    {
        var variants = new[] { new WidgetVariant("a", "A", WidgetWidth.Standard), new WidgetVariant("b", "B") };
        Assert.False(WidgetResize.CanResize(variants, "a"));
        Assert.False(WidgetResize.CanResize(new[] { new WidgetVariant("only", "Only") }, "only"));
        Assert.True(WidgetResize.CanResize(Weather, "current"));
        Assert.Null(WidgetResize.Next(Weather, "current", 0));
    }
}

public class ConfigHistorySessionTests
{
    private static AppConfig ConfigWith(params DockItem[] items) => new() { Items = items.ToList() };

    [Fact]
    public void A_session_without_changes_leaves_no_step()
    {
        var history = new ConfigHistory();
        var config = ConfigWith(DockItem.App(@"C:\a.exe"));
        var session = history.BeginSession(config, "Edited the dock");
        Assert.True(history.IsStepOpen);

        Assert.False(session.End(config));
        Assert.False(history.IsStepOpen);
        Assert.False(history.CanUndo);
        Assert.Null(history.Latest);
    }

    [Fact]
    public void Everything_done_in_a_session_is_one_step()
    {
        var history = new ConfigHistory();
        var a = DockItem.App(@"C:\a.exe");
        var b = DockItem.App(@"C:\b.exe");
        var config = ConfigWith(a, b);
        HistoryEntry? announced = null;
        history.Changed += (entry, undone) => { if (!undone) announced = entry; };

        var session = history.BeginSession(config, "Edited the dock");
        announced = null;
        // Changes made while the session is open record nothing of their own.
        Assert.Same(session.Entry, history.Push(config, "Moved b"));
        config.Items = new List<DockItem> { b };
        Assert.Same(session.Entry, history.Push(config, "Removed a", destructive: true));
        config.Items.Add(DockItem.Separator());
        Assert.Null(announced);

        Assert.True(session.End(config));
        Assert.Same(session.Entry, announced);
        Assert.True(announced!.Destructive); // shows the undo toast

        var undone = history.Undo(config);
        Assert.Same(session.Entry, undone);
        Assert.Equal(new[] { a.Id, b.Id }, config.Items.Select(i => i.Id));
        Assert.Same(b, config.Items[1]); // items still on the dock keep their live instance
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Undo_waits_while_the_session_is_open()
    {
        var history = new ConfigHistory();
        var config = ConfigWith(DockItem.App(@"C:\a.exe"));
        history.Push(config, "Earlier change");
        var session = history.BeginSession(config, "Edited the dock");
        config.Items.Clear();

        Assert.False(history.CanUndo);
        Assert.Null(history.Undo(config));
        Assert.Empty(config.Items);

        session.End(config);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void A_changed_layout_counts_as_a_change()
    {
        var history = new ConfigHistory();
        var widget = DockItem.ForWidget("weather", "current");
        var config = ConfigWith(widget);
        var session = history.BeginSession(config, "Edited the dock");
        widget.Variant = "hourly";

        Assert.True(session.End(config));
        history.Undo(config);
        Assert.Equal("current", widget.Variant);
    }

    [Fact]
    public void A_widget_changing_its_own_settings_is_not_an_edit()
    {
        var history = new ConfigHistory();
        var widget = DockItem.ForWidget("alarm");
        var config = ConfigWith(widget);
        var session = history.BeginSession(config, "Edited the dock");
        widget.Settings = new System.Text.Json.Nodes.JsonObject { ["enabled"] = false };

        Assert.False(session.End(config));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void A_session_ended_quietly_keeps_its_step_without_the_toast()
    {
        var history = new ConfigHistory();
        var config = ConfigWith();
        HistoryEntry? announced = null;
        history.Changed += (entry, undone) => { if (!undone) announced = entry; };
        var session = history.BeginSession(config, "Edited the dock");
        config.Items.Add(DockItem.Separator());

        Assert.True(session.End(config, announce: false));
        Assert.False(announced!.Destructive);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void Ending_twice_does_nothing_the_second_time()
    {
        var history = new ConfigHistory();
        var config = ConfigWith();
        var session = history.BeginSession(config, "Edited the dock");
        config.Items.Add(DockItem.Separator());
        Assert.True(session.End(config));
        Assert.False(session.End(config));
        Assert.False(session.IsOpen);
    }
}

public class DockEditModeTests
{
    private sealed class FakeDock : IEditableDock
    {
        public int Begun, Ended;

        public void BeginEditing() => Begun++;

        public void EndEditing() => Ended++;
    }

    [Fact]
    public void One_dock_is_edited_at_a_time()
    {
        var history = new ConfigHistory();
        var config = new AppConfig();
        var first = new FakeDock();
        var second = new FakeDock();
        try
        {
            DockEditMode.Enter(first, history, config);
            DockEditMode.Enter(first, history, config); // already editing: no second start
            Assert.Same(first, DockEditMode.Current);
            Assert.Equal(1, first.Begun);

            DockEditMode.Enter(second, history, config);
            Assert.Equal(1, first.Ended);
            Assert.Same(second, DockEditMode.Current);
            Assert.True(DockEditMode.IsActive);
        }
        finally
        {
            DockEditMode.Exit();
        }
        Assert.Equal(1, second.Ended);
        Assert.False(DockEditMode.IsActive);
        Assert.False(DockEditMode.Exit());
    }

    [Fact]
    public void Leaving_reports_whether_the_dock_changed()
    {
        var history = new ConfigHistory();
        var config = new AppConfig();
        var dock = new FakeDock();
        try
        {
            DockEditMode.Enter(dock, history, config);
            Assert.False(DockEditMode.Exit());
            Assert.False(history.CanUndo);

            DockEditMode.Enter(dock, history, config);
            config.Items.Add(DockItem.Separator());
            Assert.True(DockEditMode.Exit());
            Assert.True(history.CanUndo);
        }
        finally
        {
            DockEditMode.Exit();
        }
    }
}
