namespace CustomDock.Core;

/// <summary>%AppData%\DockHub altındaki tüm dosya yolları.</summary>
public static class AppPaths
{
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string LegacyRoot = Path.Combine(AppData, "CustomDock");

    /// <summary>Varsayılan %AppData%\DockHub; DOCKHUB_HOME ortam değişkeniyle değiştirilebilir (test/taşınabilir kullanım).</summary>
    public static readonly string Root = CustomRoot() is { } custom ? Path.GetFullPath(custom) : Path.Combine(AppData, "DockHub");

    public static readonly string ConfigFile = Path.Combine(Root, "config.json");

    /// <summary>Widget verileri (notlar, hatırlatıcılar, su sayacı...).</summary>
    public static readonly string DataDir = Path.Combine(Root, "data");

    /// <summary>Taskbar gizleme durumunu çökmelere karşı saklayan dosya.</summary>
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

    /// <summary>Eski adla (%AppData%\CustomDock) kaydedilmiş ayarları ve olası session.json'u yeni klasöre bir kez taşır.</summary>
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
                // Taşınamazsa varsayılan ayarlarla devam edilir.
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
