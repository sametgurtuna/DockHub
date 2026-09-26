using System.Text.Json.Nodes;
using CustomDock.Core;

namespace CustomDock.Tests;

[Collection(ConfigFileCollection.Name)]
public class ConfigHistoryTests
{
    private static ConfigService NewService()
    {
        foreach (var file in Directory.EnumerateFiles(AppPaths.Root, "config.json*")) File.Delete(file);
        File.WriteAllText(AppPaths.ConfigFile, "{ \"version\": 2, \"items\": [] }");
        var service = new ConfigService();
        service.Load();
        return service;
    }

    [Fact]
    public void Undo_brings_back_a_removed_item_with_its_settings()
    {
        var service = NewService();
        var widget = DockItem.ForWidget("ai-usage");
        widget.Settings = new JsonObject { ["refreshMinutes"] = 60 };
        service.AddItem(widget);
        service.RemoveItem(widget.Id);
        Assert.Empty(service.Config.Items);

        Assert.StartsWith("Removed", service.Undo());
        var restored = Assert.Single(service.Config.Items);
        Assert.Equal(widget.Id, restored.Id);
        Assert.Equal(60, restored.Settings?["refreshMinutes"]?.GetValue<int>());
    }

    [Fact]
    public void Undo_keeps_the_live_instances_of_untouched_items()
    {
        var service = NewService();
        var a = DockItem.App(@"C:\a.exe");
        var b = DockItem.App(@"C:\b.exe");
        service.AddItem(a);
        service.AddItem(b);
        service.MoveItem(b.Id, 0);

        service.Undo();
        Assert.Same(a, service.Config.Items[0]);
        Assert.Same(b, service.Config.Items[1]);
    }

    [Fact]
    public void Undo_restores_a_deleted_folder_and_its_children()
    {
        var service = NewService();
        var a = DockItem.App(@"C:\a.exe");
        var b = DockItem.App(@"C:\b.exe");
        service.AddItem(a);
        service.AddItem(b);
        var folder = service.CreateGroupFromItems("Tools", a.Id, b.Id);
        folder.GroupAccent = "AccentGreenBrush";

        service.RemoveItem(folder.Id);
        service.Undo();

        var restored = Assert.Single(service.Config.Items);
        Assert.Equal("Tools", restored.GroupName);
        Assert.Equal(new[] { a.Id, b.Id }, restored.Children!.Select(c => c.Id));
    }

    [Fact]
    public void History_is_capped_and_empty_undo_returns_null()
    {
        var service = NewService();
        Assert.Null(service.Undo());
        for (int i = 0; i < 30; i++) service.AddItem(DockItem.App($@"C:\{i}.exe"));
        int undone = 0;
        while (service.Undo() is not null) undone++;
        Assert.Equal(20, undone);
        Assert.Equal(10, service.Config.Items.Count);
    }
}
