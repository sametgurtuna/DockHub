using CustomDock.Core;
using ManagedShell.WindowsTray;
using Microsoft.Win32;

namespace CustomDock.Shell;

/// <summary>
/// Tepsi ikonlarının dock'ta her zaman görünmesi (sabitleme) tercihleri.
/// Yeni görülen bir ikon, Windows'ta "görev çubuğunda göster" olarak işaretliyse bir kez sabitlenir.
/// </summary>
public static class TrayPreferences
{
    private static readonly Dictionary<string, string> KnownFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{6D809377-6AF0-444B-8957-A3773F02200E}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        ["{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ["{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}"] = Environment.GetFolderPath(Environment.SpecialFolder.System),
        ["{D65231B0-B2F1-4857-A4CE-A8E7C6EA7D27}"] = Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
        ["{F38BF404-1D43-42F2-9305-67DE0B28FC23}"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        ["{F1B32785-6FBA-4FCF-9D55-7B8E7F157091}"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ["{3EB685DB-65F9-4CF6-A03A-E3EF65729F3D}"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    };

    private static HashSet<string>? _promoted;

    public static string KeyOf(NotifyIcon icon) => icon.GUID != default ? icon.GUID.ToString() : $"{icon.Path}:{icon.UID}";

    public static void ImportWindowsPromotedIcons(NotificationArea tray) => ApplyNewIcons(tray);

    public static void ApplyNewIcons(NotificationArea tray)
    {
        var config = AppServices.Config;
        _promoted ??= ReadPromotedPaths();
        bool changed = false;

        foreach (var icon in tray.TrayIcons.ToList())
        {
            string key = KeyOf(icon);
            if (config.KnownTrayIcons.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;
            config.KnownTrayIcons.Add(key);
            changed = true;

            if (!icon.IsPinned && !string.IsNullOrEmpty(icon.Path) && _promoted.Contains(icon.Path))
                icon.Pin();
        }

        if (!changed) return;
        Save(tray);
    }

    public static void Save(NotificationArea tray)
    {
        AppServices.Config.PinnedTrayIcons = tray.PinnedNotifyIcons.ToList();
        AppServices.ConfigService.ScheduleSave();
    }

    /// <summary>HKCU\Control Panel\NotifyIconSettings altında "IsPromoted=1" olan uygulama yolları.</summary>
    private static HashSet<string> ReadPromotedPaths()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (root is null) return result;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key?.GetValue("IsPromoted") is not int promoted || promoted == 0) continue;
                if (key.GetValue("ExecutablePath") is string path && !string.IsNullOrWhiteSpace(path))
                    result.Add(ExpandKnownFolder(path));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Windows tepsi tercihleri okunamadı");
        }
        return result;
    }

    public static string ExpandKnownFolder(string path)
    {
        if (path.StartsWith('{') && path.IndexOf('}') is int end and > 0)
        {
            var guid = path[..(end + 1)];
            if (KnownFolders.TryGetValue(guid, out var folder))
                return folder + path[(end + 1)..];
        }
        return path;
    }
}
