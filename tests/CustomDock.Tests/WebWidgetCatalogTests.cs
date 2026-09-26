using CustomDock.Widgets.Web;

namespace CustomDock.Tests;

public class WebWidgetCatalogTests
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

    [Theory]
    [InlineData("hello-world", "dev.dockhub.hello-world")]
    [InlineData("github-stars", "dev.dockhub.github-stars")]
    public void Sample_widgets_have_valid_manifests(string folder, string id)
    {
        var manifest = WebWidgetCatalog.Read(Path.Combine(SamplesDir, folder), out var error);
        Assert.Null(error);
        Assert.Equal(id, manifest!.Id);
        Assert.Equal("web." + id, manifest.WidgetId);
    }

    [Fact]
    public void Invalid_ids_and_missing_entries_are_rejected()
    {
        using var dir = new TempDir();
        dir.File("manifest.json", "{ \"id\": \"Bad Id!\", \"name\": \"x\", \"entry\": \"index.html\" }");
        dir.File("index.html", "<p>hi</p>");
        Assert.Null(WebWidgetCatalog.Read(dir.Path, out var badId));
        Assert.NotNull(badId);

        dir.File("manifest.json", "{ \"id\": \"good.id\", \"name\": \"x\", \"entry\": \"../outside.html\" }");
        Assert.Null(WebWidgetCatalog.Read(dir.Path, out var outside));
        Assert.NotNull(outside);
    }

    [Theory]
    [InlineData("api.github.com", true)]
    [InlineData("example.org", true)]
    [InlineData("cdn.example.org", true)]
    [InlineData("evil-example.org", false)]
    [InlineData("github.com", false)]
    [InlineData("dev-dockhub-test.widget.dockhub", true)]
    public void Network_permission_matches_hosts(string host, bool allowed)
    {
        var manifest = new WebWidgetManifest { Id = "dev.dockhub.test" };
        manifest.Permissions.Network.AddRange(new[] { "api.github.com", "*.example.org" });
        Assert.Equal(allowed, WebWidgetCatalog.IsHostAllowed(manifest, host));
    }
}
