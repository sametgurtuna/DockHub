using CustomDock.Core;
using CustomDock.Native;
using ManagedShell.WindowsTasks;

namespace CustomDock.Shell;

/// <summary>
/// Matches windows and pinned applications with the same "application key".
/// Rule: standard desktop apps are grouped by .exe path, UWP, packaged (MSIX) and PWA (Chrome/Edge web apps)
/// apps are grouped by AppUserModelID. Executable keys are version-independent (see <see cref="AppPathResolver"/>),
/// so an app keeps matching its pin after it updates itself into a new version folder.
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
        uint pid = SafeGet(() => window.ProcId) ?? 0;
        if (pid == 0) NativeMethods.GetWindowThreadProcessId(window.Handle, out pid);
        if (string.IsNullOrEmpty(exe) && pid != 0)
            exe = NativeMethods.GetProcessPath(pid);

        if (!string.IsNullOrEmpty(exe))
        {
            if (AppPathResolver.ParseMsix(exe) is not null)
                return PackagedKey(exe, string.IsNullOrEmpty(aumid) && pid != 0 ? NativeMethods.GetProcessAumid(pid) : aumid);
            return "exe:" + AppPathResolver.NormalizeExe(exe);
        }

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
            var link = ShellIcons.ReadShortcutInfo(path);
            if (!string.IsNullOrEmpty(link.AppId) && (IsWebApp(link.AppId) || string.IsNullOrEmpty(link.Target)))
                return "aumid:" + link.AppId.ToLowerInvariant();
            if (!string.IsNullOrEmpty(link.Target) && link.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return ForExecutable(link.Target, link.Arguments);
        }

        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return ForExecutable(path, item.Arguments);

        return "path:" + path.ToLowerInvariant();
    }

    private static string ForExecutable(string exe, string? arguments)
        => AppPathResolver.ParseMsix(exe) is not null ? PackagedKey(exe, null) : "exe:" + AppPathResolver.NormalizeExe(exe, arguments);

    /// <summary>Packaged apps: AppUserModelID when it can be determined, otherwise a version-independent package path.</summary>
    private static string PackagedKey(string exe, string? aumid)
    {
        if (string.IsNullOrEmpty(aumid) && AppPathResolver.ParseMsix(exe) is { } msix)
            aumid = AppPathResolver.PackageAumid(msix.FamilyName);
        return !string.IsNullOrEmpty(aumid)
            ? "aumid:" + aumid.ToLowerInvariant()
            : "exe:" + AppPathResolver.NormalizeExe(exe);
    }

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
