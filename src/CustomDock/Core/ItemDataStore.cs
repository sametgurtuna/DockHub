namespace CustomDock.Core;

/// <summary>
/// Data files that belong to a single dock item (e.g. <c>data\notes-&lt;id&gt;.json</c>, see WidgetBase.StateKey).
/// Removed items' files move to <c>data\trash</c> so an undo can bring them back; the trash is emptied after a week.
/// </summary>
public static class ItemDataStore
{
    private static readonly TimeSpan TrashLifetime = TimeSpan.FromDays(7);

    public static string TrashDir => Path.Combine(AppPaths.DataDir, "trash");

    /// <summary>Data files of an item: <c>&lt;widget&gt;-&lt;id&gt;.json</c> for widgets.</summary>
    public static IEnumerable<string> FilesOf(DockItem item)
    {
        if (item.Kind != DockItemKind.Widget || string.IsNullOrEmpty(item.Widget)) yield break;
        string path = JsonStore.DataPath($"{item.Widget}-{item.Id}");
        if (File.Exists(path)) yield return path;
    }

    /// <summary>Moves the data files of removed items (and their folder contents) to the trash.</summary>
    /// <returns>Pairs of (original path, trash path) so they can be restored.</returns>
    public static List<(string Original, string Trashed)> Trash(IEnumerable<DockItem> items)
    {
        var moved = new List<(string, string)>();
        foreach (var item in Flatten(items))
        {
            foreach (var file in FilesOf(item))
            {
                try
                {
                    Directory.CreateDirectory(TrashDir);
                    string target = Path.Combine(TrashDir, $"{DateTime.Now:yyyyMMddHHmmss}-{Path.GetFileName(file)}");
                    File.Move(file, target, overwrite: true);
                    moved.Add((file, target));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Failed to move {file} to trash");
                }
            }
        }
        return moved;
    }

    /// <summary>Puts trashed files back (undo).</summary>
    public static void Restore(IEnumerable<(string Original, string Trashed)> files)
    {
        foreach (var (original, trashed) in files)
        {
            try
            {
                if (File.Exists(trashed)) File.Move(trashed, original, overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to restore {original}");
            }
        }
    }

    /// <summary>Deletes trashed files older than a week.</summary>
    public static void PurgeOld()
    {
        try
        {
            if (!Directory.Exists(TrashDir)) return;
            foreach (var file in Directory.EnumerateFiles(TrashDir))
            {
                // Trashed at: the yyyyMMddHHmmss prefix (the file keeps its original write time).
                string name = Path.GetFileName(file);
                if (name.Length > 14 && DateTime.TryParseExact(name[..14], "yyyyMMddHHmmss", null,
                        System.Globalization.DateTimeStyles.None, out var trashedAt) && DateTime.Now - trashedAt > TrashLifetime)
                    File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to purge data trash");
        }
    }

    public static IEnumerable<DockItem> Flatten(IEnumerable<DockItem> items)
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
