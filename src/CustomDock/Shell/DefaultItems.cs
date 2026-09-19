using CustomDock.Core;

namespace CustomDock.Shell;

/// <summary>Initial dock content on first launch: Windows taskbar pins + a few widgets.</summary>
public static class DefaultItems
{
    public static readonly string TaskbarPinsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    public static List<DockItem> Create()
    {
        var items = ImportTaskbarPins();
        if (items.Count == 0)
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var path in new[]
                     {
                         Path.Combine(windows, "explorer.exe"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                     })
            {
                if (File.Exists(path)) items.Add(DockItem.App(path));
            }
        }

        items.Add(DockItem.Separator());
        items.Add(DockItem.ForWidget("media", "full"));
        items.Add(DockItem.ForWidget("weather", "current"));
        items.Add(DockItem.ForWidget("system", "rings"));
        return items;
    }

    /// <summary>Reads application shortcuts pinned to the Windows taskbar.</summary>
    public static List<DockItem> ImportTaskbarPins()
    {
        try
        {
            if (!Directory.Exists(TaskbarPinsFolder)) return new List<DockItem>();
            return new DirectoryInfo(TaskbarPinsFolder)
                .GetFiles("*.lnk", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f.CreationTimeUtc)
                .Select(f => DockItem.App(f.FullName))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read taskbar pins");
            return new List<DockItem>();
        }
    }
}
