using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Services;

namespace CustomDock.Shell;

public sealed record JumpTask(string Title, string Glyph, Action Action);

public sealed record RecentItem(string Title, string Path, ImageSource? Icon);

/// <summary>
/// Uygulamalar için Windows Jump List (Görevler, Sık Kullanılanlar ve Son Açılan Belgeler) desteği sağlar.
/// </summary>
public static class JumpListService
{
    private static readonly string RecentFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Windows", "Recent");

    public static List<JumpTask> GetTasks(string? exePath)
    {
        var tasks = new List<JumpTask>();
        if (string.IsNullOrWhiteSpace(exePath)) return tasks;

        string fileName = Path.GetFileName(exePath).ToLowerInvariant();

        // 1. Web Tarayıcıları (Chrome, Edge, Firefox, Brave, Opera, Vivaldi)
        if (fileName is "chrome.exe" or "msedge.exe" or "brave.exe" or "opera.exe" or "vivaldi.exe")
        {
            string incognitoArg = fileName is "msedge.exe" ? "-inprivate" : "-incognito";
            string incognitoLabel = fileName is "msedge.exe" ? "Yeni InPrivate penceresi" : "Yeni gizli pencere";

            tasks.Add(new JumpTask("Yeni pencere", "\uE737", () => AppLauncher.Launch(exePath, "--new-window", true)));
            tasks.Add(new JumpTask(incognitoLabel, "\uE727", () => AppLauncher.Launch(exePath, incognitoArg, true)));
        }
        else if (fileName is "firefox.exe")
        {
            tasks.Add(new JumpTask("Yeni pencere", "\uE737", () => AppLauncher.Launch(exePath, "-new-window", true)));
            tasks.Add(new JumpTask("Yeni gizli pencere", "\uE727", () => AppLauncher.Launch(exePath, "-private-window", true)));
        }
        // 2. Kod Editörleri (VS Code, VS Code Insiders, Visual Studio, Notepad++)
        else if (fileName is "code.exe" or "code - insiders.exe")
        {
            tasks.Add(new JumpTask("Yeni boş pencere", "\uE737", () => AppLauncher.Launch(exePath, "-n", true)));
        }
        else if (fileName is "notepad.exe" or "notepad++.exe")
        {
            tasks.Add(new JumpTask("Yeni pencere", "\uE737", () => AppLauncher.Launch(exePath, "", true)));
        }
        // 3. Dosya Gezgini (Explorer)
        else if (fileName is "explorer.exe")
        {
            tasks.Add(new JumpTask("İndirilenler", "\uE896", () => Process.Start("explorer.exe", "shell:Downloads")));
            tasks.Add(new JumpTask("Belgeler", "\uE8A5", () => Process.Start("explorer.exe", "shell:Personal")));
            tasks.Add(new JumpTask("Masaüstü", "\uE7C3", () => Process.Start("explorer.exe", "shell:Desktop")));
            tasks.Add(new JumpTask("Resimler", "\uEB9F", () => Process.Start("explorer.exe", "shell:My Pictures")));
        }
        // 4. Komut Satırı / Terminaller (Windows Terminal, PowerShell, CMD)
        else if (fileName is "wt.exe" or "powershell.exe" or "pwsh.exe" or "cmd.exe")
        {
            tasks.Add(new JumpTask("Yeni pencere", "\uE737", () => AppLauncher.Launch(exePath, "", true)));
            tasks.Add(new JumpTask("Yönetici olarak yeni pencere", "\uE7EF", () => AppLauncher.RunAsAdmin(exePath)));
        }
        // 5. Discord
        else if (fileName is "discord.exe" or "update.exe" && exePath.Contains("Discord", StringComparison.OrdinalIgnoreCase))
        {
            tasks.Add(new JumpTask("Ses Ayarlarını Aç", "\uE720", () => AppLauncher.Launch(exePath, "--open-settings", false)));
        }

        return tasks;
    }

    /// <summary>
    /// Bu uygulamayla ilişkili son açılan belgeleri, projeleri veya sık kullanılan klasörleri döndürür.
    /// </summary>
    public static List<RecentItem> GetRecentItems(string? exePath, int maxCount = 6)
    {
        var items = new List<RecentItem>();
        if (string.IsNullOrWhiteSpace(exePath)) return items;

        string exeName = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Explorer için Windows AutomaticDestinations'tan sık kullanılan klasörleri al
        if (exeName == "explorer")
        {
            var frequentFolders = GetExplorerFrequentFolders(maxCount);
            foreach (var f in frequentFolders)
            {
                if (seenPaths.Add(f.Path))
                    items.Add(f);
            }
        }

        // 2. VS Code için son projeleri/çalışma alanlarını al
        if (exeName is "code" or "code - insiders")
        {
            var vsProjects = GetVsCodeProjects(maxCount);
            foreach (var p in vsProjects)
            {
                if (seenPaths.Add(p.Path))
                    items.Add(p);
            }
        }

        // 3. Kalan yuvalar için Windows Recent klasöründeki kısayolları tara
        if (items.Count < maxCount && Directory.Exists(RecentFolder))
        {
            try
            {
                var dir = new DirectoryInfo(RecentFolder);
                var lnkFiles = dir.GetFiles("*.lnk")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(80);

                foreach (var file in lnkFiles)
                {
                    if (items.Count >= maxCount) break;

                    try
                    {
                        var resolved = ShellIcons.ReadShortcut(file.FullName);
                        string? target = resolved.Target;
                        if (string.IsNullOrWhiteSpace(target)) continue;

                        bool isFile = File.Exists(target);
                        bool isDir = Directory.Exists(target);
                        if (!isFile && !isDir) continue;

                        if (!seenPaths.Add(target)) continue;

                        if (IsTargetMatchingApp(exeName, target, isDir))
                        {
                            string title = Path.GetFileName(target);
                            if (string.IsNullOrWhiteSpace(title)) title = target;
                            var icon = ShellIcons.GetIcon(target, 32);
                            items.Add(new RecentItem(title, target, icon));
                        }
                    }
                    catch
                    {
                        // tekil kısayol hatasını yoksay
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Son kullanılan belgeler okunamadı");
            }
        }

        return items;
    }

    private static bool IsTargetMatchingApp(string exeName, string target, bool isDir)
    {
        string ext = Path.GetExtension(target).ToLowerInvariant();

        if (exeName == "explorer")
            return isDir;

        if (exeName is "code" or "code - insiders")
            return isDir || ext is ".cs" or ".js" or ".ts" or ".json" or ".html" or ".css" or ".py" or ".cpp" or ".h" or ".md" or ".xml" or ".xaml" or ".sql" or ".vue" or ".jsx" or ".tsx";

        if (exeName is "notepad" or "notepad++")
            return ext is ".txt" or ".log" or ".ini" or ".cfg" or ".md" or ".json" or ".xml" or ".yaml" or ".yml";

        if (exeName is "vlc" or "wmplayer" or "potplayer")
            return ext is ".mp4" or ".mkv" or ".avi" or ".mp3" or ".flac" or ".wav" or ".mov" or ".m4a" or ".aac" or ".wmv";

        if (exeName is "chrome" or "msedge" or "firefox" or "brave" or "opera")
            return ext is ".pdf" or ".html" or ".htm" or ".svg";

        if (exeName is "winword")
            return ext is ".docx" or ".doc" or ".dotx" or ".rtf" or ".pdf";

        if (exeName is "excel")
            return ext is ".xlsx" or ".xls" or ".csv" or ".xlsm";

        if (exeName is "powerpnt")
            return ext is ".pptx" or ".ppt" or ".ppsx";

        if (exeName is "acrobat" or "foxitreader" or "sumatrapdf")
            return ext is ".pdf";

        if (exeName is "photoshop" or "gimp")
            return ext is ".psd" or ".png" or ".jpg" or ".jpeg" or ".tiff" or ".webp";

        return false;
    }

    /// <summary>
    /// Windows Explorer AutomaticDestinations dosyasından son/sık kullanılan gerçek klasörleri okur.
    /// </summary>
    private static List<RecentItem> GetExplorerFrequentFolders(int maxCount)
    {
        var items = new List<RecentItem>();
        try
        {
            var autoDest = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Recent", "AutomaticDestinations", "f01b4d95cf55d32a.automaticDestinations-ms");

            if (!File.Exists(autoDest)) return items;

            byte[] bytes = File.ReadAllBytes(autoDest);
            string text = Encoding.Unicode.GetString(bytes);
            var matches = Regex.Matches(text, @"[A-Za-z]:\\[^\x00\r\n]+");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in matches)
            {
                if (items.Count >= maxCount) break;
                string path = match.Value.Split('\0')[0].Trim();
                if (Directory.Exists(path) && seen.Add(path))
                {
                    string title = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(title)) title = path;
                    var icon = ShellIcons.GetIcon(path, 32);
                    items.Add(new RecentItem(title, path, icon));
                }
            }
        }
        catch { }
        return items;
    }

    /// <summary>
    /// VS Code'un depoladığı son çalışma alanlarını ve klasörleri okur.
    /// </summary>
    private static List<RecentItem> GetVsCodeProjects(int maxCount)
    {
        var items = new List<RecentItem>();
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var storagePath = Path.Combine(appData, "Code", "User", "globalStorage", "storage.json");
            if (!File.Exists(storagePath))
                storagePath = Path.Combine(appData, "Code - Insiders", "User", "globalStorage", "storage.json");
            if (!File.Exists(storagePath)) return items;

            string json = File.ReadAllText(storagePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("profileAssociations", out var prof) &&
                prof.TryGetProperty("workspaces", out var workspaces))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in workspaces.EnumerateObject())
                {
                    if (items.Count >= maxCount) break;
                    string rawUri = prop.Name;
                    if (rawUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var uri = new Uri(rawUri);
                            string localPath = uri.LocalPath;
                            if ((Directory.Exists(localPath) || File.Exists(localPath)) && seen.Add(localPath))
                            {
                                string title = Path.GetFileName(localPath);
                                if (string.IsNullOrEmpty(title)) title = localPath;
                                var icon = ShellIcons.GetIcon(localPath, 32);
                                items.Add(new RecentItem(title, localPath, icon));
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
        return items;
    }
}
