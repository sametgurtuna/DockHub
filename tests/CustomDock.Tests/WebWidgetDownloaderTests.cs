using CustomDock.Widgets.Web;

namespace CustomDock.Tests;

public class WebWidgetDownloaderTests
{
    [Theory]
    [InlineData("https://github.com/you/widgets/tree/main/pomodoro", "https://raw.githubusercontent.com/you/widgets/main/pomodoro/manifest.json")]
    [InlineData("https://github.com/you/widgets/tree/main/pomodoro/", "https://raw.githubusercontent.com/you/widgets/main/pomodoro/manifest.json")]
    [InlineData("https://github.com/you/widgets/blob/main/pomodoro/manifest.json", "https://raw.githubusercontent.com/you/widgets/main/pomodoro/manifest.json")]
    [InlineData("https://example.com/w/manifest.json", "https://example.com/w/manifest.json")]
    [InlineData("  https://example.com/w.dockwidget ", "https://example.com/w.dockwidget")]
    public void GitHub_page_links_become_raw_links(string link, string expected)
        => Assert.Equal(expected, WebWidgetDownloader.Normalize(link)?.ToString());

    [Theory]
    [InlineData("http://example.com/manifest.json")]
    [InlineData("file:///C:/widgets/manifest.json")]
    [InlineData("example.com/manifest.json")]
    [InlineData("")]
    public void Only_https_links_are_accepted(string link)
        => Assert.Null(WebWidgetDownloader.Normalize(link));

    [Theory]
    [InlineData("index.html", true)]
    [InlineData("img/icon.png", true)]
    [InlineData("../secret.txt", false)]
    [InlineData("img/../../x.js", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("C:/Windows/win.ini", false)]
    [InlineData("img\\icon.png", false)]
    [InlineData("https://evil.example/x.js", false)]
    [InlineData("a//b.js", false)]
    [InlineData("", false)]
    public void Only_files_beside_the_manifest_are_downloaded(string path, bool safe)
        => Assert.Equal(safe, WebWidgetDownloader.IsSafeRelativePath(path));

    [Fact]
    public void Manifest_files_list_is_read()
    {
        var manifest = WebWidgetCatalog.Parse("""{ "id": "com.example.test", "name": "Test", "files": ["a.css", "img/b.svg"] }""", out var error);
        Assert.Null(error);
        Assert.Equal(new[] { "a.css", "img/b.svg" }, manifest!.Files);
    }

    [Fact]
    public void Invalid_ids_are_rejected()
    {
        Assert.Null(WebWidgetCatalog.Parse("""{ "id": "../x", "name": "Test" }""", out var error));
        Assert.NotNull(error);
    }
}
