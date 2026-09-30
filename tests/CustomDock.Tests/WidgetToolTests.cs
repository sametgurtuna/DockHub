using System.Text.Json;
using CustomDock.Widgets.Web;

namespace CustomDock.Tests;

/// <summary>tools/dockhub-widget checks and packs widgets as DockHub does (its own tests read the same cases).</summary>
public class WidgetToolTests
{
    private static string Fixtures
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures"))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "tests", "fixtures");
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "widget-manifest-cases.json")));
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return new object[] { item.GetProperty("name").GetString()!, item.GetProperty("valid").GetBoolean(), item.GetProperty("manifest").GetRawText() };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Manifests_are_judged_as_the_tool_judges_them(string name, bool valid, string manifest)
    {
        var parsed = WebWidgetCatalog.Parse(manifest, out var error);
        Assert.True((parsed is not null) == valid, $"{name}: {error}");
    }

    [Fact]
    public void A_package_made_by_the_tool_installs()
    {
        string package = Path.Combine(Fixtures, "hello-world.dockwidget");
        Assert.Null(WebWidgetCatalog.CheckPackage(package));
        var manifest = WebWidgetCatalog.Inspect(package, out string folder, out var error);
        try
        {
            Assert.Null(error);
            Assert.Equal("dev.dockhub.hello-world", manifest!.Id);
            Assert.True(File.Exists(Path.Combine(folder, manifest.Entry)));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
