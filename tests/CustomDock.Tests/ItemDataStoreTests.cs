using CustomDock.Core;

namespace CustomDock.Tests;

[Collection(ConfigFileCollection.Name)]
public class ItemDataStoreTests
{
    [Fact]
    public void Removed_widget_data_moves_to_trash_and_can_be_restored()
    {
        var item = DockItem.ForWidget("notes");
        JsonStore.SaveData($"notes-{item.Id}", new { text = "hello" });
        string original = JsonStore.DataPath($"notes-{item.Id}");

        var moved = ItemDataStore.Trash(new[] { item });
        Assert.False(File.Exists(original));
        Assert.Single(moved);
        Assert.True(File.Exists(moved[0].Trashed));

        ItemDataStore.Restore(moved);
        Assert.True(File.Exists(original));
    }

    [Fact]
    public void Folder_contents_are_included()
    {
        var note = DockItem.ForWidget("notes");
        JsonStore.SaveData($"notes-{note.Id}", new { text = "x" });
        var folder = DockItem.Group("F", new List<DockItem> { note });

        Assert.Single(ItemDataStore.Trash(new[] { folder }));
    }

    [Fact]
    public void Old_trash_is_purged()
    {
        Directory.CreateDirectory(ItemDataStore.TrashDir);
        string old = Path.Combine(ItemDataStore.TrashDir, $"{DateTime.Now.AddDays(-8):yyyyMMddHHmmss}-notes-old.json");
        string recent = Path.Combine(ItemDataStore.TrashDir, $"{DateTime.Now:yyyyMMddHHmmss}-notes-new.json");
        File.WriteAllText(old, "{}");
        File.WriteAllText(recent, "{}");

        ItemDataStore.PurgeOld();
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }
}
