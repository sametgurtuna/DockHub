using System.Runtime.InteropServices;
using CustomDock.Services;

namespace CustomDock.Tests;

public class InstallerAssetTests
{
    private static readonly string[] Both = { "DockHub-Setup-1.0.0-x64.exe", "DockHub-Setup-1.0.0-arm64.exe", "SHA256SUMS.txt" };
    private static readonly string[] OnlyX64 = { "DockHub-Setup-0.9.2-x64.exe", "SHA256SUMS.txt" };

    private static string? Pick(string[] assets, Architecture architecture) => InstallerAsset.Pick(assets, a => a, architecture);

    [Fact]
    public void Each_processor_gets_its_own_setup()
    {
        Assert.Equal("DockHub-Setup-1.0.0-x64.exe", Pick(Both, Architecture.X64));
        Assert.Equal("DockHub-Setup-1.0.0-arm64.exe", Pick(Both, Architecture.Arm64));
    }

    [Fact]
    public void An_ARM64_PC_takes_the_x64_setup_of_an_older_release()
        => Assert.Equal("DockHub-Setup-0.9.2-x64.exe", Pick(OnlyX64, Architecture.Arm64));

    [Fact]
    public void Nothing_fits_other_processors_or_a_release_without_setups()
    {
        Assert.Null(Pick(Both, Architecture.X86));
        Assert.Null(Pick(new[] { "SHA256SUMS.txt" }, Architecture.X64));
        Assert.Null(Pick(new[] { "DockHub-Setup-1.0.0-arm64.exe" }, Architecture.X64));
    }

    [Fact]
    public void The_download_keeps_the_setups_name()
    {
        var release = new ReleaseInfo(new Version(1, 0, 0), "v1.0.0", "DockHub 1.0.0", "", "https://github.com/x",
            "https://github.com/sametgurtuna/DockHub/releases/download/v1.0.0/DockHub-Setup-1.0.0-arm64.exe", 1, null);
        Assert.Equal("DockHub-Setup-1.0.0-arm64.exe", release.InstallerFileName);
    }
}
