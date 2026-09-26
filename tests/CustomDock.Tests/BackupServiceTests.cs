using System.IO.Compression;
using CustomDock.Core;

namespace CustomDock.Tests;

[Collection(ConfigFileCollection.Name)]
public class BackupServiceTests
{
    private static void ResetFiles()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        foreach (var file in Directory.EnumerateFiles(AppPaths.Root, "config.json*")) File.Delete(file);
        foreach (var file in Directory.EnumerateFiles(AppPaths.DataDir)) File.Delete(file);
        if (Directory.Exists(BackupService.BackupDir)) Directory.Delete(BackupService.BackupDir, recursive: true);
    }

    [Fact]
    public void Export_then_import_round_trips_config_and_widget_data()
    {
        ResetFiles();
        File.WriteAllText(AppPaths.ConfigFile, "{ \"version\": 2, \"edge\": \"Left\", \"items\": [] }");
        JsonStore.SaveData("notes-abc", new { text = "keep me" });
        JsonStore.SaveData("weather-cache", new { });

        using var dir = new TempDir();
        string zipPath = Path.Combine(dir.Path, "backup.zip");
        BackupService.Export(zipPath);

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            Assert.NotNull(zip.GetEntry("manifest.json"));
            Assert.NotNull(zip.GetEntry("data/notes-abc.json"));
            Assert.Null(zip.GetEntry("data/weather-cache.json"));
        }
        Assert.Null(BackupService.Validate(zipPath));

        File.WriteAllText(AppPaths.ConfigFile, "{ \"version\": 2, \"edge\": \"Top\", \"items\": [] }");
        File.Delete(JsonStore.DataPath("notes-abc"));
        BackupService.Import(zipPath);

        Assert.Contains("Left", File.ReadAllText(AppPaths.ConfigFile));
        Assert.True(File.Exists(JsonStore.DataPath("notes-abc")));
        Assert.NotEmpty(Directory.EnumerateFiles(BackupService.BackupDir, "before-import-*.zip"));
    }

    [Fact]
    public void Validate_rejects_other_zip_files()
    {
        using var dir = new TempDir();
        string zipPath = Path.Combine(dir.Path, "other.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            zip.CreateEntry("readme.txt");
        Assert.NotNull(BackupService.Validate(zipPath));
    }

    [Fact]
    public void Corrupt_config_is_recovered_from_the_daily_backup()
    {
        ResetFiles();
        File.WriteAllText(AppPaths.ConfigFile, "{ \"version\": 2, \"edge\": \"Right\", \"items\": [] }");
        BackupService.DailyBackup();
        File.WriteAllText(AppPaths.ConfigFile, "\0\0\0");

        var service = new ConfigService();
        service.Load();
        Assert.Equal(DockEdge.Right, service.Config.Edge);
        Assert.NotNull(service.RecoveredFromBackup);
    }

    [Fact]
    public void Only_seven_daily_backups_are_kept()
    {
        ResetFiles();
        Directory.CreateDirectory(BackupService.BackupDir);
        for (int i = 1; i <= 9; i++)
            File.WriteAllText(Path.Combine(BackupService.BackupDir, $"config-2026010{i}.json"), "{}");
        File.WriteAllText(AppPaths.ConfigFile, "{}");

        BackupService.DailyBackup();
        Assert.Equal(7, BackupService.DailyBackups().Count());
        Assert.Contains(BackupService.DailyBackups(), f => f.EndsWith($"config-{DateTime.Now:yyyyMMdd}.json"));
    }
}
