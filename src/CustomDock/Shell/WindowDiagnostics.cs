using System.Text;
using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Shell;

/// <summary>Text report of how DockHub sees windows and pinned apps (Settings › About › Copy diagnostics).</summary>
public static class WindowDiagnostics
{
    public static string Dump(ShellHost shell, AppConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DockHub {AppInfo.Version} diagnostics, {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Mode: {config.TaskbarMode}, running apps: {config.ShowRunningApps}, all displays: {config.ShowOnAllDisplays}");
        sb.AppendLine();

        sb.AppendLine("== Task list (all windows known to ManagedShell)");
        var windows = shell.VisibilityWatcher.AllWindows?.ToList() ?? new();
        foreach (var window in windows)
        {
            try
            {
                sb.AppendLine($"- 0x{window.Handle.ToInt64():X} \"{window.Title}\" [{NativeMethods.GetClassName(window.Handle)}]");
                sb.AppendLine($"    exe: {window.WinFileName}");
                sb.AppendLine($"    aumid: {window.AppUserModelID} uwp: {window.IsUWP}");
                sb.AppendLine($"    showInTaskbar (cached): {window.ShowInTaskbar}, eligible now: {TaskbarEligibility.IsEligible(window.Handle)}");
                sb.AppendLine($"    {TaskbarEligibility.Describe(window.Handle)}");
                sb.AppendLine($"    key: {AppKeys.ForWindow(window)} category: {window.Category}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    (error: {ex.Message})");
            }
        }
        sb.AppendLine();

        sb.AppendLine("== Running app groups");
        foreach (var group in shell.RunningApps.Groups)
            sb.AppendLine($"- {group.Key} \"{group.Title}\" windows: {group.WindowCount} exe: {group.ExecutablePath}");
        sb.AppendLine();

        sb.AppendLine("== Pinned apps");
        foreach (var item in Flatten(config.Items).Where(i => i.Kind == DockItemKind.App))
        {
            string key = AppKeys.ForItem(item);
            bool exists = item.Path is { } p && (File.Exists(p) || Directory.Exists(p) || p.StartsWith(AppKeys.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase));
            var group = shell.RunningApps.Find(key);
            sb.AppendLine($"- \"{item.Name}\" {item.Path} {item.Arguments}");
            sb.AppendLine($"    key: {key} exists: {exists} running: {(group is null ? "no" : $"yes ({group.WindowCount})")} icon: {(AppIcons.TryFor(item, 96) is null ? "MISSING" : "ok")}");
        }

        return sb.ToString();
    }

    private static IEnumerable<DockItem> Flatten(IEnumerable<DockItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item.Children is { } children)
                foreach (var child in Flatten(children))
                    yield return child;
        }
    }
}
