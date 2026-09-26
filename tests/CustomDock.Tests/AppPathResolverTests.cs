using CustomDock.Shell;

namespace CustomDock.Tests;

public class AppPathResolverTests
{
    [Theory]
    [InlineData(@"C:\Users\u\AppData\Local\Discord\app-1.0.9259\Discord.exe", @"C:\Users\u\AppData\Local\Discord", "Discord.exe")]
    [InlineData(@"C:\Users\u\AppData\Local\slack\app-4.41.105\slack.exe", @"C:\Users\u\AppData\Local\slack", "slack.exe")]
    [InlineData(@"C:\X\App\APP-1.2\resources\tool.exe", @"C:\X\App", @"resources\tool.exe")]
    [InlineData(@"C:\X\App\app-2.0.0-beta3\App.exe", @"C:\X\App", "App.exe")]
    public void ParseSquirrel_finds_root_and_relative_path(string path, string root, string relative)
    {
        var result = AppPathResolver.ParseSquirrel(path);
        Assert.NotNull(result);
        Assert.Equal(root, result!.Value.Root);
        Assert.Equal(relative, result.Value.RelativePath);
    }

    [Theory]
    [InlineData(@"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Games\app-store\app.exe")]
    [InlineData(@"C:\X\app-\App.exe")]
    public void ParseSquirrel_ignores_normal_paths(string path) => Assert.Null(AppPathResolver.ParseSquirrel(path));

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Claude_2.9939.2.0_x64__pzs8sxrjxfjjc\app\claude.exe", "Claude_pzs8sxrjxfjjc", @"app\claude.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.21.2361.0_x64__8wekyb3d8bbwe\WindowsTerminal.exe", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "WindowsTerminal.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\Some.App_1.0.0.0_neutral_split.scale-100_abcdefghijk\x.exe", "Some.App_abcdefghijk", "x.exe")]
    public void ParseMsix_finds_family_and_relative_path(string path, string family, string relative)
    {
        var result = AppPathResolver.ParseMsix(path);
        Assert.NotNull(result);
        Assert.Equal(family, result!.Value.FamilyName);
        Assert.Equal(relative, result.Value.RelativePath);
    }

    [Theory]
    [InlineData("--processStart Discord.exe", "Discord.exe")]
    [InlineData("--processStart \"Slack.exe\" --process-start-args \"--x\"", "Slack.exe")]
    [InlineData("--processStartAndWait \"Teams.exe\"", "Teams.exe")]
    [InlineData("--update", null)]
    [InlineData(null, null)]
    public void ParseProcessStart_reads_the_started_exe(string? arguments, string? expected)
        => Assert.Equal(expected, AppPathResolver.ParseProcessStart(arguments));

    [Fact]
    public void NormalizeExe_is_version_independent_for_squirrel_and_msix()
    {
        Assert.Equal(
            AppPathResolver.NormalizeExe(@"C:\U\Discord\app-1.0.9259\Discord.exe"),
            AppPathResolver.NormalizeExe(@"C:\U\Discord\app-1.0.9999\Discord.exe"));
        Assert.Equal(
            AppPathResolver.NormalizeExe(@"C:\Program Files\WindowsApps\Claude_2.1.0.0_x64__pzs\app\claude.exe"),
            AppPathResolver.NormalizeExe(@"C:\Program Files\WindowsApps\Claude_2.9.0.0_x64__pzs\app\claude.exe"));
        Assert.Equal(@"c:\program files\app\app.exe", AppPathResolver.NormalizeExe(@"C:\Program Files\App\App.exe"));
    }

    [Fact]
    public void Squirrel_stub_updater_and_version_folder_share_one_key()
    {
        using var dir = new TempDir();
        dir.File(@"Discord\Update.exe");
        string stub = dir.File(@"Discord\Discord.exe");
        string versioned = dir.File(@"Discord\app-1.0.9259\Discord.exe");
        string updater = Path.Combine(dir.Path, @"Discord\Update.exe");

        string key = AppPathResolver.NormalizeExe(versioned);
        Assert.Equal(key, AppPathResolver.NormalizeExe(stub));
        Assert.Equal(key, AppPathResolver.NormalizeExe(updater, "--processStart Discord.exe"));
    }

    [Fact]
    public void StablePinPath_prefers_the_root_stub_then_the_updater()
    {
        using var dir = new TempDir();
        string versioned = dir.File(@"App\app-3.1.0\App.exe");
        dir.File(@"App\Update.exe");

        var (viaUpdater, arguments) = AppPathResolver.StablePinPath(versioned, null);
        Assert.Equal(Path.Combine(dir.Path, @"App\Update.exe"), viaUpdater);
        Assert.Equal("--processStart \"App.exe\"", arguments);

        string stub = dir.File(@"App\App.exe");
        var (viaStub, noArguments) = AppPathResolver.StablePinPath(versioned, null);
        Assert.Equal(stub, viaStub);
        Assert.Null(noArguments);
    }

    [Fact]
    public void StablePinPath_keeps_normal_paths()
    {
        var (path, arguments) = AppPathResolver.StablePinPath(@"C:\Program Files\App\App.exe", "Some.Aumid");
        Assert.Equal(@"C:\Program Files\App\App.exe", path);
        Assert.Null(arguments);
    }

    [Fact]
    public void StablePinPath_uses_the_aumid_for_packaged_apps()
    {
        var (path, _) = AppPathResolver.StablePinPath(@"C:\Program Files\WindowsApps\Claude_2.9939.2.0_x64__pzs8sxrjxfjjc\app\claude.exe", "Claude_pzs8sxrjxfjjc!Claude");
        Assert.Equal(@"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude", path);
    }

    [Fact]
    public void Repair_finds_the_newest_version_folder()
    {
        using var dir = new TempDir();
        dir.File(@"App\app-1.9.0\App.exe");
        string newest = dir.File(@"App\app-1.10.0\App.exe");
        dir.File(@"App\app-1.11.0\Other.exe");

        string missing = Path.Combine(dir.Path, @"App\app-1.2.0\App.exe");
        Assert.Equal(newest, AppPathResolver.Repair(missing));
    }

    [Fact]
    public void Repair_returns_existing_paths_unchanged_and_null_when_nothing_is_found()
    {
        using var dir = new TempDir();
        string existing = dir.File(@"App\app-1.0.0\App.exe");
        Assert.Equal(existing, AppPathResolver.Repair(existing));
        Assert.Null(AppPathResolver.Repair(Path.Combine(dir.Path, @"Gone\app-1.0.0\Gone.exe")));
    }
}
