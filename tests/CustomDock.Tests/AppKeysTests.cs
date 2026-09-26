using CustomDock.Core;
using CustomDock.Shell;

namespace CustomDock.Tests;

public class AppKeysTests
{
    [Fact]
    public void Store_apps_are_keyed_by_aumid_in_lower_case()
        => Assert.Equal("aumid:microsoft.windowsterminal_8wekyb3d8bbwe!app",
            AppKeys.ForItem(DockItem.App(@"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App")));

    [Fact]
    public void Executables_are_keyed_by_normalized_path()
        => Assert.Equal(@"exe:c:\program files\app\app.exe", AppKeys.ForItem(DockItem.App(@"C:\Program Files\App\App.exe")));

    [Fact]
    public void Squirrel_pins_match_regardless_of_version()
    {
        Assert.Equal(
            AppKeys.ForItem(DockItem.App(@"C:\U\Discord\app-1.0.1\Discord.exe")),
            AppKeys.ForItem(DockItem.App(@"C:\U\Discord\app-1.0.2\Discord.exe")));
    }

    [Fact]
    public void Updater_pins_match_the_started_app()
    {
        using var dir = new TempDir();
        dir.File(@"Slack\app-4.0.0\slack.exe");
        var updater = DockItem.App(Path.Combine(dir.Path, @"Slack\Update.exe"));
        updater.Arguments = "--processStart \"slack.exe\"";
        Assert.Equal(AppKeys.ForItem(DockItem.App(Path.Combine(dir.Path, @"Slack\app-4.0.0\slack.exe"))), AppKeys.ForItem(updater));
    }

    [Fact]
    public void Other_paths_get_a_path_key()
        => Assert.Equal(@"path:c:\docs\readme.txt", AppKeys.ForItem(DockItem.App(@"C:\Docs\readme.txt")));

    [Theory]
    [InlineData("aumid:abc!app", "abc!app")]
    [InlineData(@"exe:c:\x.exe", null)]
    public void AppIdOf_extracts_the_aumid(string key, string? expected) => Assert.Equal(expected, AppKeys.AppIdOf(key));
}
