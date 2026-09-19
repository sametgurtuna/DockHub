using CustomDock.Core;
using CustomDock.Native;
using ManagedShell.WindowsTasks;

namespace CustomDock.Shell;

/// <summary>
/// Matches windows and pinned applications with the same "application key".
/// Rule: standard desktop apps are grouped by .exe path, UWP and PWA (Chrome/Edge web apps)
/// are grouped by AppUserModelID.
/// </summary>
public static class AppKeys
{
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    public static string ForWindow(ApplicationWindow window)
    {
        string? aumid = SafeGet(() => window.AppUserModelID);
        if (!string.IsNullOrEmpty(aumid) && (SafeGet(() => window.IsUWP) || IsWebApp(aumid)))
            return "aumid:" + aumid.ToLowerInvariant();

        string? exe = SafeGet(() => window.WinFileName);
        if (string.IsNullOrEmpty(exe))
        {
            uint pid = SafeGet(() => window.ProcId) ?? 0;
            if (pid == 0) NativeMethods.GetWindowThreadProcessId(window.Handle, out pid);
            if (pid != 0) exe = NativeMethods.GetProcessPath(pid);
        }

        if (!string.IsNullOrEmpty(exe))
            return "exe:" + exe.ToLowerInvariant();

        return "hwnd:" + window.Handle;
    }

    /// <summary>Key for pinned item (path-based unique key if no match).</summary>
    public static string ForItem(DockItem item)
    {
        var path = item.Path ?? "";
        if (path.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
            return "aumid:" + path[AppsFolderPrefix.Length..].ToLowerInvariant();

        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var (target, appId) = ShellIcons.ReadShortcut(path);
            if (!string.IsNullOrEmpty(appId) && (IsWebApp(appId) || string.IsNullOrEmpty(target)))
                return "aumid:" + appId.ToLowerInvariant();
            if (!string.IsNullOrEmpty(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return "exe:" + target.ToLowerInvariant();
        }

        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return "exe:" + path.ToLowerInvariant();

        return "path:" + path.ToLowerInvariant();
    }

    /// <summary>Returns executable path if the key represents an .exe.</summary>
    public static string? ExecutableOf(string key)
        => key.StartsWith("exe:", StringComparison.Ordinal) ? key[4..] : null;

    public static string? AppIdOf(string key)
        => key.StartsWith("aumid:", StringComparison.Ordinal) ? key[6..] : null;

    private static bool IsWebApp(string aumid) => aumid.Contains("_crx_", StringComparison.OrdinalIgnoreCase);

    private static T? SafeGet<T>(Func<T> getter)
    {
        try { return getter(); }
        catch { return default; }
    }
}

/// <summary>Categorizes ManagedShell windows by application key.</summary>
public sealed class AppCategoryProvider : ITaskCategoryProvider
{
    public string GetCategory(ApplicationWindow window) => AppKeys.ForWindow(window);

    public void SetCategoryChangeDelegate(TaskCategoryChangeDelegate changeDelegate)
    {
    }

    public void Dispose()
    {
    }
}
