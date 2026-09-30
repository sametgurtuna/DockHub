using CustomDock.Core;
using CustomDock.Dock;

namespace CustomDock.Tests;

public class ConfigDockSurfaceTests
{
    [Fact]
    public void The_surface_shows_the_settings_as_they_are()
    {
        var config = new AppConfig
        {
            Edge = DockEdge.Left, Size = DockSize.Large, Layout = DockLayout.Attached, WidthMode = DockWidthMode.Fit,
            Alignment = DockAlignment.Start, AutoHide = true, EdgeMargin = 12, SmartAutoHide = true,
            Backdrop = BackdropKind.Solid, TintOpacity = 0.4,
        };
        using var surface = new ConfigDockSurface(config, DockRole.Main);

        Assert.Equal(DockRole.Main, surface.Role);
        Assert.Equal(config.Edge, surface.Edge);
        Assert.Equal(config.Size, surface.Size);
        Assert.Equal(config.Layout, surface.Layout);
        Assert.Equal(config.WidthMode, surface.WidthMode);
        Assert.Equal(config.Alignment, surface.Alignment);
        Assert.Equal(config.AutoHide, surface.AutoHide);
        Assert.Equal(config.EdgeMargin, surface.EdgeMargin);
        Assert.Equal(config.SmartAutoHide, surface.SmartAutoHide);
        Assert.Equal(config.Backdrop, surface.Backdrop);
        Assert.Equal(config.TintOpacity, surface.TintOpacity);
        Assert.True(surface.IsVertical);
    }

    [Fact]
    public void Changing_a_layout_setting_tells_the_surface()
    {
        var config = new AppConfig();
        using var surface = new ConfigDockSurface(config, DockRole.Secondary);
        var changed = new List<string>();
        surface.Changed += changed.Add;

        config.Edge = DockEdge.Top;
        config.AutoHide = !config.AutoHide;
        config.ShowClock = !config.ShowClock; // not a layout setting of the surface
        Assert.Equal(new[] { nameof(DockSurface.Edge), nameof(DockSurface.AutoHide) }, changed);
    }

    [Fact]
    public void Setting_through_the_surface_changes_the_config()
    {
        var config = new AppConfig();
        using var surface = new ConfigDockSurface(config, DockRole.Main);
        surface.Edge = DockEdge.Right;
        surface.AutoHide = true;
        Assert.Equal(DockEdge.Right, config.Edge);
        Assert.True(config.AutoHide);
    }

    [Fact]
    public void A_disposed_surface_no_longer_listens()
    {
        var config = new AppConfig();
        var surface = new ConfigDockSurface(config, DockRole.Main);
        int count = 0;
        surface.Changed += _ => count++;
        surface.Dispose();
        config.Edge = DockEdge.Top;
        Assert.Equal(0, count);
    }

    [Fact]
    public void Every_listed_setting_is_a_surface_property_and_a_config_property()
    {
        foreach (var name in ConfigDockSurface.Properties)
        {
            Assert.NotNull(typeof(DockSurface).GetProperty(name));
            Assert.NotNull(typeof(AppConfig).GetProperty(name));
        }
        var surfaceSettings = typeof(DockSurface).GetProperties()
            .Where(p => p.CanWrite && p.Name != nameof(DockSurface.Role)).Select(p => p.Name).ToHashSet();
        Assert.True(surfaceSettings.SetEquals(ConfigDockSurface.Properties));
    }

    [Fact]
    public void The_top_bar_needs_a_surface_of_its_own()
        => Assert.Throws<ArgumentException>(() => new ConfigDockSurface(new AppConfig(), DockRole.Bar));
}
