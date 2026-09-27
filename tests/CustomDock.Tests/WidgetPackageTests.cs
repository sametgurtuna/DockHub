using System.IO.Compression;
using CustomDock.Core;
using CustomDock.Widgets.Web;

namespace CustomDock.Tests;

public class WidgetPackageTests
{
    private const string Manifest = """{ "id": "dev.test.package", "name": "Package", "entry": "index.html" }""";

    private static string Zip(TempDir dir, params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(dir.Path, Guid.NewGuid().ToString("N")[..6] + ".dockwidget");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
        return path;
    }

    [Fact]
    public void A_valid_package_is_read_from_its_root_or_one_folder()
    {
        using var dir = new TempDir();
        var flat = WebWidgetCatalog.Inspect(Zip(dir, ("manifest.json", Manifest), ("index.html", "<p>hi</p>")), out _, out var error);
        Assert.Null(error);
        Assert.Equal("dev.test.package", flat!.Id);

        var nested = WebWidgetCatalog.Inspect(Zip(dir, ("widget/manifest.json", Manifest), ("widget/index.html", "<p>hi</p>")), out _, out error);
        Assert.Null(error);
        Assert.Equal("dev.test.package", nested!.Id);
    }

    [Fact]
    public void A_package_without_manifest_or_entry_is_refused()
    {
        using var dir = new TempDir();
        Assert.Null(WebWidgetCatalog.Inspect(Zip(dir, ("index.html", "<p>hi</p>")), out _, out var noManifest));
        Assert.NotNull(noManifest);
        Assert.Null(WebWidgetCatalog.Inspect(Zip(dir, ("manifest.json", Manifest)), out _, out var noEntry));
        Assert.NotNull(noEntry);
    }

    [Fact]
    public void Entries_that_leave_the_folder_are_refused()
    {
        using var dir = new TempDir();
        string package = Zip(dir, ("manifest.json", Manifest), ("index.html", "<p>hi</p>"), ("../../escaped.txt", "x"));
        Assert.Null(WebWidgetCatalog.Inspect(package, out _, out var error));
        Assert.NotNull(error);
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escaped.txt")));
    }

    [Fact]
    public void Too_many_entries_are_refused_before_extracting()
    {
        using var dir = new TempDir();
        var entries = Enumerable.Range(0, WebWidgetCatalog.MaxPackageEntries + 1).Select(i => ($"f{i}.txt", "x")).ToArray();
        Assert.NotNull(WebWidgetCatalog.CheckPackage(Zip(dir, entries)));
    }

    [Fact]
    public void Packages_that_unpack_too_large_are_refused()
    {
        using var dir = new TempDir();
        // Highly compressible: tiny on disk, over the limit once unpacked.
        string big = new('a', (int)(WebWidgetCatalog.MaxUnpackedBytes / 4) + 1);
        string package = Zip(dir, ("manifest.json", Manifest), ("a.txt", big), ("b.txt", big), ("c.txt", big), ("d.txt", big));
        Assert.True(new FileInfo(package).Length < WebWidgetCatalog.MaxPackageBytes);
        Assert.NotNull(WebWidgetCatalog.CheckPackage(package));
    }

    [Fact]
    public void Broken_files_are_refused_with_a_reason()
    {
        using var dir = new TempDir();
        string notZip = dir.File("fake.dockwidget", "this is not a zip");
        Assert.NotNull(WebWidgetCatalog.CheckPackage(notZip));
        Assert.Null(WebWidgetCatalog.Inspect(notZip, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Queued_paths_are_taken_once()
    {
        var queue = new RequestQueue("test-requests-" + Guid.NewGuid().ToString("N")[..6] + ".txt", @"Local\DockHub.Tests.Queue");
        queue.Enqueue(@"C:\widgets\a.dockwidget");
        queue.Enqueue(@"C:\widgets\a.dockwidget");
        queue.Enqueue(@"C:\widgets\b.dockwidget");
        var taken = queue.Dequeue();
        Assert.Equal(2, taken.Count);
        Assert.Empty(queue.Dequeue());
    }
}

public class WebWidgetDevReloadTests
{
    [Fact]
    public void Changed_paths_map_to_their_widget_folder()
    {
        string root = Path.Combine(Path.GetTempPath(), "widgets");
        string folder = Path.Combine(root, "dev.test.clock");
        Assert.Equal(folder, WebWidgetDevReload.WidgetFolderOf(root, Path.Combine(folder, "index.html")));
        Assert.Equal(folder, WebWidgetDevReload.WidgetFolderOf(root, Path.Combine(folder, "img", "icon.png")));
        Assert.Equal(folder, WebWidgetDevReload.WidgetFolderOf(root, folder));
        Assert.Null(WebWidgetDevReload.WidgetFolderOf(root, root));
        Assert.Null(WebWidgetDevReload.WidgetFolderOf(root, Path.Combine(Path.GetTempPath(), "elsewhere", "index.html")));
        Assert.Null(WebWidgetDevReload.WidgetFolderOf(root, root + "-other" + Path.DirectorySeparatorChar + "x.html"));
    }
}
