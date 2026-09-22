namespace CustomDock.Core;

/// <summary>All file paths under %AppData%\DockHub.</summary>
public static class AppPaths
{
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string LegacyRoot = Path.Combine(AppData, "CustomDock");

    /// <summary>Default %AppData%\DockHub; can be overridden via DOCKHUB_HOME environment variable (for testing/portable use).</summary>
    public static readonly string Root = CustomRoot() is { } custom ? Path.GetFullPath(custom) : Path.Combine(AppData, "DockHub");

    public static readonly string ConfigFile = Path.Combine(Root, "config.json");

    /// <summary>Widget data (notes, reminders, hydration counter...).</summary>
    public static readonly string DataDir = Path.Combine(Root, "data");

    /// <summary>File preserving taskbar hide state against crashes.</summary>
    public static readonly string SessionFile = Path.Combine(Root, "session.json");

    public static readonly string LogFile = Path.Combine(Root, "log.txt");

    private static string? CustomRoot() =>
        Environment.GetEnvironmentVariable("DOCKHUB_HOME") is { Length: > 0 } home ? home
        : Environment.GetEnvironmentVariable("CUSTOMDOCK_HOME") is { Length: > 0 } legacy ? legacy
        : null;

    public static void EnsureCreated()
    {
        MigrateLegacyFolder();
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DataDir);
    }

    /// <summary>One-time migration of settings and session.json saved under legacy name (%AppData%\CustomDock) to new directory.</summary>
    private static void MigrateLegacyFolder()
    {
        if (CustomRoot() is not null || Directory.Exists(Root) || !Directory.Exists(LegacyRoot))
            return;
        try
        {
            Directory.Move(LegacyRoot, Root);
        }
        catch
        {
            try
            {
                CopyDirectory(LegacyRoot, Root);
            }
            catch
            {
                // If migration fails, proceed with defaults.
            }
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }
}
