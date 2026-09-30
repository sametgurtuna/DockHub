using CustomDock.Widgets.Web;

namespace CustomDock.Tests;

public class WebWidgetUpdatesTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.2.10", "1.2.9", 1)]
    [InlineData("2.0", "1.9.9", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("v1.4.0", "1.4.0", 0)]
    [InlineData("1.0.0", "1.0.0-beta.2", 1)]
    [InlineData("1.0.0-beta.10", "1.0.0-beta.2", 1)]
    [InlineData("1.0.0-rc.1", "1.0.0-beta.5", 1)]
    [InlineData("1.0.0-beta", "1.0.0-beta.1", -1)]
    [InlineData("1.0.0+build.7", "1.0.0", 0)]
    public void Versions_compare_like_semver(string a, string b, int expected)
    {
        Assert.Equal(expected, Math.Sign(WidgetVersion.Compare(a, b)));
        Assert.Equal(-expected, Math.Sign(WidgetVersion.Compare(b, a)));
    }

    private static WebWidgetManifest Manifest(string[] hosts, bool notifications = false, params string[] servers) => new()
    {
        Id = "dev.test.update",
        Name = "Update",
        Settings = servers.Select(key => new WebWidgetSetting { Key = key, Type = "text", Label = "Server " + key }).ToList(),
        Permissions = new WebWidgetPermissions { Network = hosts.ToList(), Notifications = notifications, NetworkFromSettings = servers.ToList() },
    };

    [Fact]
    public void Only_new_permissions_are_listed()
    {
        var installed = Manifest(new[] { "api.example.com", "*.cdn.example.org" });
        var same = Manifest(new[] { "API.example.com", "img.cdn.example.org", "cdn.example.org" });
        Assert.False(PermissionChange.Between(installed, same).Any);

        var wider = Manifest(new[] { "api.example.com", "*.example.org", "tracker.example.net" }, notifications: true, "server");
        var change = PermissionChange.Between(installed, wider);
        Assert.True(change.Any);
        Assert.Equal(new[] { "*.example.org", "tracker.example.net" }, change.Hosts);
        Assert.True(change.Notifications);
        Assert.Equal(new[] { "Server server" }, change.ServerSettings);
    }

    [Fact]
    public void Fewer_permissions_ask_nothing()
    {
        var installed = Manifest(new[] { "a.example.com", "b.example.com" }, notifications: true, "server");
        Assert.False(PermissionChange.Between(installed, Manifest(new[] { "a.example.com" })).Any);
    }

    [Fact]
    public void The_source_is_kept_next_to_the_widget()
    {
        using var dir = new TempDir();
        Assert.Null(WidgetSource.Read(dir.Path));
        new WidgetSource { Link = "https://github.com/me/widgets/tree/main/clock", Version = "1.2.0", InstalledAt = DateTime.UtcNow }.Write(dir.Path);
        var source = WidgetSource.Read(dir.Path)!;
        Assert.Equal("https://github.com/me/widgets/tree/main/clock", source.Link);
        Assert.Equal("1.2.0", source.Version);

        File.WriteAllText(Path.Combine(dir.Path, WidgetSource.FileName), "{ broken");
        Assert.Null(WidgetSource.Read(dir.Path));
    }

    [Theory]
    [InlineData("https://github.com/me/widgets/tree/main/clock", "https://raw.githubusercontent.com/me/widgets/main/clock/manifest.json")]
    [InlineData("https://example.com/widgets/clock/manifest.json", "https://example.com/widgets/clock/manifest.json")]
    [InlineData("https://example.com/widgets/clock/", "https://example.com/widgets/clock/manifest.json")]
    [InlineData("https://example.com/clock.dockwidget", null)]
    [InlineData("https://example.com/clock.zip", null)]
    [InlineData("http://example.com/clock/manifest.json", null)]
    public void Updates_are_looked_up_in_the_manifest(string link, string? expected)
        => Assert.Equal(expected, WebWidgetUpdates.ManifestUri(link)?.ToString());

    [Fact]
    public void A_version_for_a_newer_DockHub_is_not_offered()
    {
        Assert.True(WebWidgetUpdates.Supported(new WebWidgetManifest { MinDockHubVersion = "0.1.0" }));
        Assert.True(WebWidgetUpdates.Supported(new WebWidgetManifest()));
        Assert.False(WebWidgetUpdates.Supported(new WebWidgetManifest { MinDockHubVersion = "99.0.0" }));
    }
}
