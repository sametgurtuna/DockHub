namespace CustomDock.Core;

/// <summary>%AppData%\CustomDock altındaki tüm dosya yolları.</summary>
public static class AppPaths
{
    /// <summary>Varsayılan %AppData%\CustomDock; CUSTOMDOCK_HOME ortam değişkeniyle değiştirilebilir (test/taşınabilir kullanım).</summary>
    public static readonly string Root =
        Environment.GetEnvironmentVariable("CUSTOMDOCK_HOME") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CustomDock");

    public static readonly string ConfigFile = Path.Combine(Root, "config.json");

    /// <summary>Widget verileri (notlar, hatırlatıcılar, su sayacı...).</summary>
    public static readonly string DataDir = Path.Combine(Root, "data");

    /// <summary>Taskbar gizleme durumunu çökmelere karşı saklayan dosya.</summary>
    public static readonly string SessionFile = Path.Combine(Root, "session.json");

    public static readonly string LogFile = Path.Combine(Root, "log.txt");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(DataDir);
    }
}
