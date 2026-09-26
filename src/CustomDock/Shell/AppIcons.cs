using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Shell;

/// <summary>Single entry point for the icon of a pinned app item (dock buttons and folder contents).</summary>
public static class AppIcons
{
    /// <summary>Icon for a pinned item. Never null: falls back to the generic app icon and reports it.</summary>
    public static ImageSource For(DockItem item, int sizePx, out bool isFallback)
    {
        var image = TryFor(item, sizePx);
        isFallback = image is null;
        return image ?? ShellIcons.GetDefaultAppIcon();
    }

    public static ImageSource? TryFor(DockItem item, int sizePx)
    {
        var path = item.Path;
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return ForShortcut(path, sizePx);

        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return ForExecutable(path, item.Arguments, sizePx);

        return ShellIcons.GetIcon(path, sizePx);
    }

    /// <summary>Clears cached lookups for an item before a retry.</summary>
    public static void Invalidate(DockItem item)
    {
        if (item.Path is { } path) ShellIcons.ClearCache(path);
    }

    private static ImageSource? ForShortcut(string path, int sizePx)
    {
        var link = ShellIcons.ReadShortcutInfo(path);

        // The target executable's icon is clearer than the shortcut's (no arrow overlay).
        if (link.Target is { } target && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            !AppPathResolver.IsSquirrelUpdater(target) && File.Exists(target) &&
            ShellIcons.GetIcon(target, sizePx) is { } targetIcon)
            return targetIcon;

        // Squirrel shortcuts point at Update.exe; their icon location points at the real app icon.
        if (link.IconFile is { } iconFile && ShellIcons.GetIconFromLocation(iconFile, link.IconIndex, sizePx) is { } locationIcon)
            return locationIcon;

        if (link.Target is { } updater && ForExecutable(updater, link.Arguments, sizePx) is { } updaterIcon)
            return updaterIcon;

        return ShellIcons.GetIcon(path, sizePx);
    }

    private static ImageSource? ForExecutable(string path, string? arguments, int sizePx)
    {
        // Update.exe --processStart App.exe: use the app's own icon.
        if (AppPathResolver.IsSquirrelUpdater(path) && AppPathResolver.ParseProcessStart(arguments) is { } started &&
            Path.GetDirectoryName(path) is { } root &&
            AppPathResolver.NewestSquirrelFile(root, started) is { } appExe &&
            ShellIcons.GetIcon(appExe, sizePx) is { } appIcon)
            return appIcon;

        if (ShellIcons.GetIcon(path, sizePx) is { } icon) return icon;

        // Versioned install folder removed by an update: look for the current one.
        if (!File.Exists(path) && AppPathResolver.Repair(path) is { } repaired)
            return ShellIcons.GetIcon(repaired, sizePx);

        return null;
    }
}
