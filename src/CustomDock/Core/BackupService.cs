using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CustomDock.Core;

/// <summary>
/// Settings backups: manual export/import as a .zip (config + widget data), and a daily copy of config.json
/// kept for a week that is used automatically if config.json ever turns out to be corrupt.
/// </summary>
public static class BackupService
{
    private const int DailyBackupsKept = 7;
    private const string ManifestName = "manifest.json";

    public static string BackupDir => Path.Combine(AppPaths.Root, "backups");

    /// <summary>Data files that aren't worth backing up (caches, temporary files).</summary>
    private static bool IsBackedUp(string dataFile)
    {
        string name = Path.GetFileName(dataFile);
        return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
               !name.Equals("weather-cache.json", StringComparison.OrdinalIgnoreCase) &&
               !name.Equals("update-state.json", StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ Export / import

    public static void Export(string zipPath)
    {
        AppServices.ConfigService.SaveNow();
        string temp = zipPath + ".tmp";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            var manifest = new JsonObject
            {
                ["app"] = AppInfo.Name,
                ["version"] = AppInfo.Version,
                ["configVersion"] = AppConfig.CurrentVersion,
                ["createdAt"] = DateTime.Now.ToString("O"),
            };
            var entry = zip.CreateEntry(ManifestName);
            using (var writer = new StreamWriter(entry.Open()))
                writer.Write(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            zip.CreateEntryFromFile(AppPaths.ConfigFile, "config.json");
            if (Directory.Exists(AppPaths.DataDir))
                foreach (var file in Directory.EnumerateFiles(AppPaths.DataDir).Where(IsBackedUp))
                    zip.CreateEntryFromFile(file, "data/" + Path.GetFileName(file));
        }
        File.Move(temp, zipPath, overwrite: true);
        Log.Info($"Settings exported to {zipPath}");
    }

    /// <summary>Checks a backup before importing. Returns an error message, or null when it can be imported.</summary>
    public static string? Validate(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            if (zip.GetEntry(ManifestName) is not { } manifestEntry || zip.GetEntry("config.json") is null)
                return "This file isn't a DockHub backup.";
            using var reader = new StreamReader(manifestEntry.Open());
            var manifest = JsonNode.Parse(reader.ReadToEnd()) as JsonObject;
            if (manifest?["app"]?.GetValue<string>() != AppInfo.Name)
                return "This file isn't a DockHub backup.";
            if ((manifest["configVersion"]?.GetValue<int>() ?? 0) > AppConfig.CurrentVersion)
                return $"This backup was made by a newer DockHub ({manifest["version"]}). Update DockHub first.";
            return null;
        }
        catch (Exception ex)
        {
            return $"The backup can't be read: {ex.Message}";
        }
    }

    /// <summary>Replaces the current settings with a backup. The current state is saved first; DockHub must restart afterwards.</summary>
    public static void Import(string zipPath)
    {
        Directory.CreateDirectory(BackupDir);
        Export(Path.Combine(BackupDir, $"before-import-{DateTime.Now:yyyyMMdd-HHmmss}.zip"));

        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName == ManifestName || entry.FullName.EndsWith('/')) continue;
            string target = entry.FullName == "config.json"
                ? AppPaths.ConfigFile
                : entry.FullName.StartsWith("data/", StringComparison.Ordinal)
                    ? Path.Combine(AppPaths.DataDir, Path.GetFileName(entry.FullName))
                    : "";
            if (target.Length == 0) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target + ".tmp", overwrite: true);
            File.Move(target + ".tmp", target, overwrite: true);
        }
        Log.Info($"Settings imported from {zipPath}");
    }

    // ------------------------------------------------------------------ Daily backups

    /// <summary>Keeps one copy of config.json per day (the first save of the day), for a week.</summary>
    public static void DailyBackup()
    {
        try
        {
            if (!File.Exists(AppPaths.ConfigFile)) return;
            Directory.CreateDirectory(BackupDir);
            string today = Path.Combine(BackupDir, $"config-{DateTime.Now:yyyyMMdd}.json");
            if (File.Exists(today)) return;
            File.Copy(AppPaths.ConfigFile, today);
            foreach (var old in DailyBackups().Skip(DailyBackupsKept))
                File.Delete(old);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Daily config backup failed");
        }
    }

    /// <summary>Daily backups, newest first.</summary>
    public static IEnumerable<string> DailyBackups()
        => Directory.Exists(BackupDir)
            ? Directory.EnumerateFiles(BackupDir, "config-*.json").OrderByDescending(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    /// <summary>Newest daily backup that is valid JSON, used when config.json is corrupt.</summary>
    public static string? LatestValidDailyBackup()
    {
        foreach (var file in DailyBackups())
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                    }) is JsonObject)
                    return file;
            }
            catch
            {
                // Try an older one.
            }
        }
        return null;
    }
}
