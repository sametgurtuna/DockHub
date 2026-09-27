using System.Text.Json.Nodes;
using CustomDock.Widgets.Web;

namespace CustomDock.Tests;

public class WebWidgetIndexTests
{
    private static string SamplesDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples"))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "samples", "widgets");
        }
    }

    [Fact]
    public void The_repository_index_is_valid_and_its_samples_exist()
    {
        var errors = new List<string>();
        var entries = WebWidgetIndex.Parse(File.ReadAllText(Path.Combine(SamplesDir, "index.json")), errors);
        Assert.Empty(errors);
        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            string folder = Path.Combine(SamplesDir, entry.Links[0].Split("/samples/widgets/")[1].Split('/')[0]);
            var manifest = WebWidgetCatalog.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")), out var error);
            Assert.True(manifest is not null, $"{entry.Id}: {error}");
            Assert.Equal(entry.Id, manifest!.Id);
            Assert.True(File.Exists(Path.Combine(folder, manifest.Entry)), $"{entry.Id}: entry file missing");
        }
    }

    [Fact]
    public void The_built_in_list_matches_the_repository_index()
    {
        var index = WebWidgetIndex.Parse(File.ReadAllText(Path.Combine(SamplesDir, "index.json"))).Select(e => e.Id);
        Assert.Equal(index.OrderBy(i => i), WebWidgetIndex.BuiltIn.Select(e => e.Id).OrderBy(i => i));
    }

    [Fact]
    public void Invalid_entries_are_skipped()
    {
        var errors = new List<string>();
        var entries = WebWidgetIndex.Parse("""
            { "widgets": [
                { "id": "com.example.good", "name": "Good", "description": "Fine.", "link": "https://github.com/a/b/tree/main/w" },
                { "id": "Bad Id", "name": "x", "description": "x", "link": "https://example.com/manifest.json" },
                { "id": "com.example.nodesc", "name": "x", "link": "https://example.com/manifest.json" },
                { "id": "com.example.http", "name": "x", "description": "x", "link": "http://example.com/manifest.json" },
                { "id": "com.example.path", "name": "x", "description": "x", "path": "../escape" },
                { "id": "com.example.nested", "name": "x", "description": "x", "path": "a/b" },
                { "id": "com.example.good", "name": "Twice", "description": "x", "link": "https://example.com/w.dockwidget" }
            ] }
            """, errors);
        Assert.Equal("com.example.good", Assert.Single(entries).Id);
        Assert.Equal(6, errors.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{ \"widgets\": {} }")]
    public void Broken_indexes_give_nothing(string json)
    {
        Assert.Empty(WebWidgetIndex.Parse(json));
    }

    [Fact]
    public void Long_lists_are_cut()
    {
        var items = Enumerable.Range(0, WebWidgetIndex.MaxEntries + 5)
            .Select(i => $$"""{ "id": "com.example.w{{i}}", "name": "W", "description": "D", "link": "https://example.com/w{{i}}.dockwidget" }""");
        Assert.Equal(WebWidgetIndex.MaxEntries, WebWidgetIndex.Parse($$"""{ "widgets": [{{string.Join(",", items)}}] }""").Count);
    }
}

public class WebWidgetNetworkTests
{
    private static WebWidgetManifest HomeAssistant()
        => WebWidgetCatalog.Parse("""
            { "id": "dev.test.ha", "name": "HA",
              "settings": [ { "key": "server", "type": "text", "label": "Server" }, { "key": "count", "type": "number", "label": "N" } ],
              "permissions": { "network": ["api.example.com"], "networkFromSettings": ["server"] } }
            """, out _)!;

    [Fact]
    public void Server_settings_must_be_text_settings()
    {
        Assert.NotNull(HomeAssistant());
        Assert.Null(WebWidgetCatalog.Parse("""
            { "id": "dev.test.bad", "name": "Bad", "settings": [ { "key": "count", "type": "number", "label": "N" } ],
              "permissions": { "networkFromSettings": ["count"] } }
            """, out var error));
        Assert.NotNull(error);
        Assert.Null(WebWidgetCatalog.Parse("""{ "id": "dev.test.bad", "name": "Bad", "permissions": { "networkFromSettings": ["missing"] } }""", out _));
    }

    [Fact]
    public void A_server_setting_cannot_bring_its_own_address()
    {
        Assert.Null(WebWidgetCatalog.Parse("""
            { "id": "dev.test.sneaky", "name": "Sneaky",
              "settings": [ { "key": "server", "type": "text", "label": "Server", "default": "http://collector.evil.example" } ],
              "permissions": { "networkFromSettings": ["server"] } }
            """, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("http://homeassistant.local:8123", "homeassistant.local")]
    [InlineData(" https://ha.example.net/ ", "ha.example.net")]
    [InlineData("homeassistant.local:8123", null)]
    [InlineData("ftp://files.local", null)]
    [InlineData("", null)]
    public void The_host_the_user_entered_is_allowed(string server, string? host)
    {
        var hosts = WebWidgetCatalog.SettingsHosts(HomeAssistant(), new JsonObject { ["server"] = server, ["count"] = 3 });
        if (host is null) Assert.Empty(hosts);
        else Assert.Equal(host, Assert.Single(hosts));
    }

    [Fact]
    public void Settings_hosts_add_to_the_manifest_hosts()
    {
        var manifest = HomeAssistant();
        var hosts = new[] { "homeassistant.local" };
        Assert.True(WebWidgetCatalog.IsHostAllowed(manifest, "api.example.com", hosts));
        Assert.True(WebWidgetCatalog.IsHostAllowed(manifest, "HomeAssistant.local", hosts));
        Assert.False(WebWidgetCatalog.IsHostAllowed(manifest, "evil.example", hosts));
        Assert.False(WebWidgetCatalog.IsHostAllowed(manifest, "homeassistant.local", Array.Empty<string>()));
    }

    private static readonly Func<string, bool> OnlyHa = host => host == "homeassistant.local";

    [Fact]
    public void Http_requests_are_checked()
    {
        var request = WebWidgetHttp.Parse(new JsonObject
        {
            ["url"] = "http://homeassistant.local:8123/api/services/light/toggle",
            ["method"] = "post",
            ["headers"] = new JsonObject { ["Authorization"] = "Bearer x", ["Content-Type"] = "application/json" },
            ["body"] = "{\"entity_id\":\"light.desk\"}",
        }, OnlyHa);
        Assert.Equal("POST", request.Method);
        Assert.Equal(2, request.Headers.Count);
        Assert.Equal(8123, request.Url.Port);
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "url": "file:///C:/secret.txt" }""")]
    [InlineData("""{ "url": "http://evil.example/steal" }""")]
    [InlineData("""{ "url": "http://homeassistant.local/", "method": "PATCH" }""")]
    [InlineData("""{ "url": "http://homeassistant.local/", "headers": { "Cookie": "a=b" } }""")]
    [InlineData("""{ "url": "http://homeassistant.local/", "headers": { "host": "other" } }""")]
    public void Bad_http_requests_are_refused(string json)
    {
        Assert.Throws<InvalidOperationException>(() => WebWidgetHttp.Parse(JsonNode.Parse(json) as JsonObject, OnlyHa));
    }

    [Fact]
    public void Large_request_bodies_are_refused()
    {
        var args = new JsonObject { ["url"] = "http://homeassistant.local/", ["method"] = "POST", ["body"] = new string('x', WebWidgetHttp.MaxBodyBytes + 1) };
        Assert.Throws<InvalidOperationException>(() => WebWidgetHttp.Parse(args, OnlyHa));
    }
}
