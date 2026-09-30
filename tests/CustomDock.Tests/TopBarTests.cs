using System.Text.Json;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Native;

namespace CustomDock.Tests;

public class TopBarPlacementTests
{
    private const string Second = @"\\.\DISPLAY2";
    private static readonly string[] Docks = { Second };
    private const string Bar = DockItem.BarSurface;

    private static bool Shows(DockItemKind kind, string? surface, string? display, DockRole role, string? device = null, bool barOn = true)
        => WidgetPlacement.ShowsOn(kind, surface, display, role, device, Docks, barOn);

    [Fact]
    public void The_bar_shows_only_its_widgets()
    {
        Assert.True(Shows(DockItemKind.Widget, Bar, null, DockRole.Bar));
        Assert.False(Shows(DockItemKind.Widget, null, null, DockRole.Bar));
        Assert.False(Shows(DockItemKind.App, null, null, DockRole.Bar));
        Assert.False(Shows(DockItemKind.Separator, null, null, DockRole.Bar));
        Assert.False(Shows(DockItemKind.Group, null, null, DockRole.Bar));
    }

    [Fact]
    public void Bar_widgets_leave_the_docks_while_the_bar_is_on()
    {
        Assert.False(Shows(DockItemKind.Widget, Bar, null, DockRole.Main));
        Assert.False(Shows(DockItemKind.Widget, Bar, null, DockRole.Secondary, Second));
        Assert.True(Shows(DockItemKind.Widget, null, null, DockRole.Main));
        Assert.True(Shows(DockItemKind.App, null, null, DockRole.Main));
        Assert.True(Shows(DockItemKind.App, null, null, DockRole.Secondary, Second));
    }

    [Fact]
    public void With_the_bar_off_its_widgets_come_back_to_the_main_dock()
    {
        Assert.True(Shows(DockItemKind.Widget, Bar, null, DockRole.Main, barOn: false));
        Assert.False(Shows(DockItemKind.Widget, Bar, null, DockRole.Secondary, Second, barOn: false));
        // Even one that had a display assigned before it went to the bar.
        Assert.True(Shows(DockItemKind.Widget, Bar, Second, DockRole.Main, barOn: false));
        Assert.False(Shows(DockItemKind.Widget, Bar, null, DockRole.Bar, barOn: false));
    }

    [Fact]
    public void Display_widgets_follow_the_display_rule_as_before()
    {
        Assert.False(Shows(DockItemKind.Widget, null, Second, DockRole.Main));
        Assert.True(Shows(DockItemKind.Widget, null, Second, DockRole.Secondary, Second));
        Assert.False(Shows(DockItemKind.Widget, null, Second, DockRole.Bar));
    }
}

public class TopBarSettingsTests
{
    [Theory]
    [InlineData(DockEdge.Top, DockEdge.Bottom, DockEdge.Top)]
    [InlineData(DockEdge.Top, DockEdge.Top, DockEdge.Bottom)]
    [InlineData(DockEdge.Bottom, DockEdge.Bottom, DockEdge.Top)]
    [InlineData(DockEdge.Left, DockEdge.Left, DockEdge.Right)]
    [InlineData(DockEdge.Right, DockEdge.Right, DockEdge.Left)]
    [InlineData(DockEdge.Left, DockEdge.Bottom, DockEdge.Left)]
    public void The_bar_never_shares_the_docks_edge(DockEdge bar, DockEdge dock, DockEdge expected)
    {
        Assert.Equal(expected, TopBarSettings.EdgeFor(bar, dock));
        Assert.NotEqual(dock, TopBarSettings.EdgeFor(bar, dock));
    }

    [Fact]
    public void Settings_from_before_the_bar_load_with_the_bar_off()
    {
        var config = JsonSerializer.Deserialize<AppConfig>("""{ "edge": "Bottom" }""", JsonStore.Options)!;
        Assert.False(config.TopBar.Enabled);
        Assert.Equal(DockEdge.Top, config.TopBar.Edge);
        Assert.True(config.TopBar.ShowClock);
        Assert.Null(config.TopBar.Backdrop);

        var nulled = JsonSerializer.Deserialize<AppConfig>("""{ "topBar": null }""", JsonStore.Options)!;
        Assert.NotNull(nulled.TopBar);
    }

    [Fact]
    public void The_bar_and_bar_widgets_are_saved()
    {
        var config = new AppConfig();
        config.TopBar.Enabled = true;
        config.TopBar.Backdrop = BackdropKind.Solid;
        var widget = DockItem.ForWidget("clock");
        widget.Surface = DockItem.BarSurface;
        config.Items = new List<DockItem> { widget };

        var back = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config, JsonStore.Options), JsonStore.Options)!;
        Assert.True(back.TopBar.Enabled);
        Assert.Equal(BackdropKind.Solid, back.TopBar.Backdrop);
        Assert.Equal(DockItem.BarSurface, back.Items[0].Surface);
        // Widgets on the dock don't write the field.
        Assert.DoesNotContain("\"surface\"", JsonSerializer.Serialize(DockItem.ForWidget("clock"), JsonStore.Options), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_profiles_bar_is_copied_into_the_live_one()
    {
        var live = new TopBarSettings();
        var profile = new TopBarSettings { Enabled = true, Edge = DockEdge.Left, Size = DockSize.Large, AutoHide = true, ShowClock = false };
        live.CopyFrom(profile);
        Assert.True(live.Enabled);
        Assert.Equal(DockEdge.Left, live.Edge);
        Assert.Equal(DockSize.Large, live.Size);
        Assert.True(live.AutoHide);
        Assert.False(live.ShowClock);
    }

    [Fact]
    public void Moving_a_widget_to_the_bar_is_undone_with_the_rest()
    {
        var history = new ConfigHistory();
        var widget = DockItem.ForWidget("clock");
        var config = new AppConfig { Items = new List<DockItem> { widget } };
        var session = history.BeginSession(config, "Edited the dock");
        widget.Surface = DockItem.BarSurface;
        Assert.True(session.End(config));
        history.Undo(config);
        Assert.Null(widget.Surface);
    }
}

public class BarDockSurfaceTests
{
    [Fact]
    public void The_bar_takes_its_own_settings_and_shares_the_rest()
    {
        var config = new AppConfig { Edge = DockEdge.Bottom, Backdrop = BackdropKind.Acrylic, EdgeMargin = 10, TintOpacity = 0.3 };
        config.TopBar.Size = DockSize.Medium;
        config.TopBar.AutoHide = true;
        using var surface = new BarDockSurface(config);

        Assert.Equal(DockRole.Bar, surface.Role);
        Assert.Equal(DockEdge.Top, surface.Edge);
        Assert.Equal(DockSize.Medium, surface.Size);
        Assert.Equal(DockLayout.Attached, surface.Layout);
        Assert.Equal(DockWidthMode.Full, surface.WidthMode);
        Assert.Equal(DockAlignment.Start, surface.Alignment);
        Assert.True(surface.AutoHide);
        Assert.Equal(BackdropKind.Acrylic, surface.Backdrop); // the dock's until the bar has its own
        Assert.Equal(10, surface.EdgeMargin);
        Assert.Equal(0.3, surface.TintOpacity);

        config.TopBar.Backdrop = BackdropKind.Solid;
        Assert.Equal(BackdropKind.Solid, surface.Backdrop);
    }

    [Fact]
    public void Moving_the_dock_to_the_bars_edge_moves_the_bar()
    {
        var config = new AppConfig { Edge = DockEdge.Bottom };
        using var surface = new BarDockSurface(config);
        var changed = new List<string>();
        surface.Changed += changed.Add;

        config.Edge = DockEdge.Top;
        Assert.Equal(DockEdge.Bottom, surface.Edge);
        Assert.Contains(nameof(DockSurface.Edge), changed);
    }

    [Fact]
    public void The_bar_hears_about_its_own_settings_but_not_about_being_turned_on()
    {
        var config = new AppConfig();
        using var surface = new BarDockSurface(config);
        var changed = new List<string>();
        surface.Changed += changed.Add;

        config.TopBar.Size = DockSize.Large;
        config.TopBar.ShowClock = false;
        config.TopBar.Enabled = true;
        config.Backdrop = BackdropKind.Solid;            // shared while the bar has none of its own
        config.TopBar.Backdrop = BackdropKind.Blur;
        config.Backdrop = BackdropKind.Acrylic;          // not the bar's any more
        config.ShowTray = !config.ShowTray;              // not a bar setting
        Assert.Equal(new[] { "Size", "ShowClock", "Backdrop", "Backdrop" }, changed);
    }

    [Fact]
    public void A_replaced_bar_is_followed()
    {
        var config = new AppConfig();
        using var surface = new BarDockSurface(config);
        config.TopBar = new TopBarSettings { Size = DockSize.Large };
        Assert.Equal(DockSize.Large, surface.Size);
        int count = 0;
        surface.Changed += _ => count++;
        config.TopBar.AutoHide = true;
        Assert.Equal(1, count);
    }

    [Fact]
    public void Setting_the_bars_edge_to_the_docks_keeps_them_apart()
    {
        var config = new AppConfig { Edge = DockEdge.Bottom };
        using var surface = new BarDockSurface(config);
        surface.Edge = DockEdge.Bottom;
        Assert.Equal(DockEdge.Top, surface.Edge);
    }

    [Fact]
    public void A_closed_bar_stops_following_the_settings()
    {
        var config = new AppConfig();
        BarDockSurface? surface = null;
        // App, listening from before the bar opened, closes it while the same change still reaches the bar.
        config.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppConfig.TopBar)) surface?.Dispose();
        };
        surface = new BarDockSurface(config);
        int count = 0;
        surface.Changed += _ => count++;
        config.TopBar = new TopBarSettings();
        config.TopBar.Size = DockSize.Large;
        config.EdgeMargin = 12;
        Assert.Equal(0, count);
    }
}

public class WidgetDisplaysTests
{
    private static readonly WidgetDisplays.Choice Main = new(null, "Main dock");
    private static readonly WidgetDisplays.Choice Bar = new(null, "Top bar", Bar: true);
    private static readonly WidgetDisplays.Choice Second = new(@"\\.\DISPLAY2", "Display 2");

    [Fact]
    public void A_bar_widget_is_on_the_bar_or_while_the_bar_is_off_on_the_main_dock()
    {
        var widget = new DockItem { Kind = DockItemKind.Widget, Surface = DockItem.BarSurface, Display = Second.Device };
        Assert.Same(Bar, WidgetDisplays.Current(new[] { Main, Bar, Second }, widget));
        // WidgetPlacement shows it on the main dock whatever its display.
        Assert.Same(Main, WidgetDisplays.Current(new[] { Main, Second }, widget));
    }

    [Fact]
    public void A_dock_widget_is_on_its_display()
    {
        var widget = new DockItem { Kind = DockItemKind.Widget, Display = Second.Device };
        Assert.Same(Second, WidgetDisplays.Current(new[] { Main, Bar, Second }, widget));
        Assert.Same(Main, WidgetDisplays.Current(new[] { Main, Bar }, widget));
    }
}

public class DockAreaTests
{
    private static readonly RECT Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void Nothing_reserved_leaves_the_whole_display()
        => Assert.Equal(Screen, DockArea.Beside(Screen, Array.Empty<(DockEdge, RECT)>()));

    [Fact]
    public void The_other_windows_bands_are_left_free()
    {
        var area = DockArea.Beside(Screen, new[]
        {
            (DockEdge.Top, new RECT(0, 0, 1920, 32)),
            (DockEdge.Left, new RECT(0, 32, 64, 1080)),
        });
        Assert.Equal(new RECT(64, 32, 1920, 1080), area);

        area = DockArea.Beside(Screen, new[]
        {
            (DockEdge.Bottom, new RECT(0, 1016, 1920, 1080)),
            (DockEdge.Right, new RECT(1856, 0, 1920, 1016)),
        });
        Assert.Equal(new RECT(0, 0, 1856, 1016), area);
    }
}
