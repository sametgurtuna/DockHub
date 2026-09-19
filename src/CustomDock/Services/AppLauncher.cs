using System.Diagnostics;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Shell;
using ManagedShell.WindowsTasks;
using MsShellHelper = ManagedShell.Common.Helpers.ShellHelper;

namespace CustomDock.Services;

/// <summary>Application launch and taskbar click behaviors.</summary>
public static class AppLauncher
{
    /// <summary>
    /// Taskbar behavior: if no window, launch; if single window is active, minimize, otherwise bring to front;
    /// if multiple windows, cycle to next.
    /// </summary>
    public static void Activate(DockItem? item, AppGroup? group)
    {
        if (group is null || group.Windows.Count == 0)
        {
            if (item is not null) Launch(item);
            return;
        }

        var windows = group.Windows;
        var active = windows.FirstOrDefault(w => w.State == ApplicationWindow.WindowState.Active);

        if (windows.Count == 1)
        {
            var window = windows[0];
            if (active is not null && !window.IsMinimized)
                window.Minimize();
            else
                window.BringToFront();
            return;
        }

        if (active is null)
        {
            (windows.FirstOrDefault(w => !w.IsMinimized) ?? windows[0]).BringToFront();
            return;
        }

        int index = windows.IndexOf(active);
        windows[(index + 1) % windows.Count].BringToFront();
    }

    public static void Launch(DockItem item, bool newInstance = false)
    {
        if (string.IsNullOrWhiteSpace(item.Path)) return;
        Launch(item.Path, item.Arguments, newInstance);
    }

    public static void Launch(string path, string? arguments = null, bool newInstance = false)
    {
        try
        {
            if (path.StartsWith(AppKeys.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var aumid = path[AppKeys.AppsFolderPrefix.Length..];
                if (MsShellHelper.ActivateApplication(aumid, arguments ?? "")) return;
            }

            var info = new ProcessStartInfo(path) { UseShellExecute = true, Arguments = arguments ?? "" };
            if (File.Exists(path))
                info.WorkingDirectory = Path.GetDirectoryName(path) ?? "";
            Process.Start(info);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to launch application: {path}");
            AppServices.Notifications.Show("Could not launch app", $"{Path.GetFileName(path)}\n{ex.Message}");
        }
    }

    public static void RunAsAdmin(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to run as administrator: {path}");
        }
    }

    public static void OpenLocation(string path)
    {
        try
        {
            var target = path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShellIcons.ResolveShortcut(path) ?? path : path;
            if (File.Exists(target) || Directory.Exists(target))
                Process.Start("explorer.exe", $"/select,\"{target}\"");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open file location");
        }
    }

    /// <summary>Produces a pinnable path from a running window.</summary>
    public static string? PinnablePath(AppGroup group)
    {
        if (group.ExecutablePath is { } exe) return exe;
        if (AppKeys.AppIdOf(group.Key) is { } aumid)
        {
            // AUMID may have been lowercased; get the original case from the window.
            var original = group.Windows.Select(w => w.AppUserModelID).FirstOrDefault(a => !string.IsNullOrEmpty(a)) ?? aumid;
            return AppKeys.AppsFolderPrefix + original;
        }
        return null;
    }
}
