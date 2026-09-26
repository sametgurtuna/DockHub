using System.Text.Json.Nodes;
using CustomDock.Core;

namespace CustomDock.Tests;

[Collection(ConfigFileCollection.Name)]
public class ConfigServiceTests : IDisposable
{
    public ConfigServiceTests()
    {
        Directory.CreateDirectory(AppPaths.Root);
        foreach (var file in Directory.EnumerateFiles(AppPaths.Root, "config.json*")) File.Delete(file);
        Directory.CreateDirectory(AppPaths.DataDir);
        foreach (var file in Directory.EnumerateFiles(AppPaths.DataDir)) File.Delete(file);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(AppPaths.Root, "config.json*")) File.Delete(file);
    }

    private static ConfigService LoadWith(string? json)
    {
        if (json is not null) File.WriteAllText(AppPaths.ConfigFile, json);
        var service = new ConfigService();
        service.Load();
        return service;
    }

    private static string ConfigJson(params object[] items) =>
        new JsonObject
        {
            ["version"] = 2,
            ["items"] = new JsonArray(items.Select(i => System.Text.Json.JsonSerializer.SerializeToNode(i, JsonStore.Options)).ToArray()),
        }.ToJsonString();

    [Fact]
    public void First_run_creates_a_saved_default_config()
    {
        var service = LoadWith(null);
        Assert.True(service.Config.IsFirstRun);
        Assert.True(File.Exists(AppPaths.ConfigFile));
        Assert.Equal(AppConfig.CurrentVersion, service.Config.Version);
    }

    [Fact]
    public void Version_1_config_is_migrated()
    {
        JsonStore.SaveData("pinned-apps", new { apps = new[] { new { path = @"C:\Windows\explorer.exe", name = "Explorer" } } });
        var config = LoadWith(TestEnvironment.Fixture(@"config\v1.json")).Config;

        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        Assert.Equal(TaskbarMode.Replace, config.TaskbarMode);
        Assert.Equal(BackdropKind.Blur, config.Backdrop);
        Assert.Equal(DockSize.Small, config.Size);
        Assert.Equal(6, config.EdgeMargin);
        Assert.Null(config.Widgets);

        Assert.Collection(config.Items,
            app => Assert.Equal(@"C:\Windows\explorer.exe", app.Path),
            separator => Assert.Equal(DockItemKind.Separator, separator.Kind),
            clock =>
            {
                Assert.Equal("clock", clock.Widget);
                Assert.Equal("analog", clock.Variant);
                Assert.True(clock.Settings?["showSeconds"]?.GetValue<bool>());
            },
            weather => Assert.Equal("weather", weather.Widget));
    }

    [Fact]
    public void Corrupt_config_is_backed_up_and_defaults_are_used()
    {
        var service = LoadWith("\0\0\0 not json");
        Assert.NotEmpty(Directory.EnumerateFiles(AppPaths.Root, "config.json.corrupt-*"));
        Assert.Equal(AppConfig.CurrentVersion, service.Config.Version);
    }

    [Fact]
    public void Widgets_unknown_to_this_version_are_kept()
    {
        var config = LoadWith(ConfigJson(new { id = "w1", kind = "Widget", widget = "future-widget" })).Config;
        Assert.Contains(config.Items, i => i.Widget == "future-widget");
    }

    [Fact]
    public void Removing_the_last_item_of_a_folder_removes_the_folder()
    {
        var service = LoadWith(ConfigJson());
        var a = DockItem.App(@"C:\a.exe");
        var b = DockItem.App(@"C:\b.exe");
        service.AddItem(a);
        service.AddItem(b);
        var group = service.CreateGroupFromItems("Tools", a.Id, b.Id);

        Assert.Single(service.Config.Items);
        service.RemoveItem(a.Id);
        Assert.Equal(new[] { b.Id }, group.Children!.Select(c => c.Id));
        service.RemoveItem(b.Id);
        Assert.Empty(service.Config.Items);
    }

    [Fact]
    public void MoveItem_uses_insertion_indices()
    {
        var service = LoadWith(ConfigJson());
        var items = Enumerable.Range(0, 4).Select(i => DockItem.App($@"C:\{i}.exe")).ToList();
        foreach (var item in items) service.AddItem(item);

        service.MoveItem(items[0].Id, 3); // before the last item
        Assert.Equal(new[] { 1, 2, 0, 3 }, service.Config.Items.Select(i => items.IndexOf(i)));

        service.MoveItem(items[3].Id, 0);
        Assert.Equal(new[] { 3, 1, 2, 0 }, service.Config.Items.Select(i => items.IndexOf(i)));
    }

    [Fact]
    public void MoveItem_pulls_children_out_of_folders()
    {
        var service = LoadWith(ConfigJson());
        var a = DockItem.App(@"C:\a.exe");
        var b = DockItem.App(@"C:\b.exe");
        var c = DockItem.App(@"C:\c.exe");
        service.AddItem(a);
        service.AddItem(b);
        service.AddItem(c);
        var group = service.CreateGroupFromItems("G", a.Id, b.Id);

        service.MoveItem(b.Id, 0);
        Assert.Equal(new[] { b.Id, group.Id, c.Id }, service.Config.Items.Select(i => i.Id));
    }

    [Fact]
    public void UngroupAll_puts_children_back_in_place()
    {
        var service = LoadWith(ConfigJson());
        var a = DockItem.App(@"C:\a.exe");
        var b = DockItem.App(@"C:\b.exe");
        var c = DockItem.App(@"C:\c.exe");
        service.AddItem(c);
        service.AddItem(a);
        service.AddItem(b);
        var group = service.CreateGroupFromItems("G", a.Id, b.Id);

        service.UngroupAll(group.Id);
        Assert.Equal(new[] { c.Id, a.Id, b.Id }, service.Config.Items.Select(i => i.Id));
    }

    [Fact]
    public void Item_settings_are_shared_and_written_back()
    {
        var service = LoadWith(ConfigJson());
        var item = DockItem.ForWidget("ai-usage");
        service.AddItem(item);

        var settings = service.GetItemSettings<CustomDock.Widgets.AIUsageSettings>(item);
        Assert.Same(settings, service.GetItemSettings<CustomDock.Widgets.AIUsageSettings>(item));
        settings.RefreshMinutes = 30;
        Assert.Equal(30, item.Settings?["refreshMinutes"]?.GetValue<int>());
    }

    [Fact]
    public void Broken_item_settings_fall_back_to_defaults()
    {
        var service = LoadWith(ConfigJson());
        var item = DockItem.ForWidget("ai-usage");
        item.Settings = new JsonObject { ["refreshMinutes"] = "not a number" };
        service.AddItem(item);

        Assert.Equal(15, service.GetItemSettings<CustomDock.Widgets.AIUsageSettings>(item).RefreshMinutes);
    }

    [Fact]
    public void Versioned_squirrel_pins_are_repaired_on_load()
    {
        using var dir = new TempDir();
        dir.File(@"Chat\Update.exe");
        string stub = dir.File(@"Chat\Chat.exe");
        dir.File(@"Chat\app-2.0.0\Chat.exe");
        string old = Path.Combine(dir.Path, @"Chat\app-1.0.0\Chat.exe");

        var config = LoadWith(ConfigJson(new { id = "a1", kind = "App", path = old })).Config;
        Assert.Equal(stub, config.Items.Single().Path);
    }
}
