using System.Text.Json;
using System.Text.Json.Nodes;
using CustomDock.Core;

namespace CustomDock.Tests;

public class SyncMergeTests
{
    private static JsonObject Json(AppConfig config) => (JsonObject)JsonSerializer.SerializeToNode(config, JsonStore.Options)!;

    private static AppConfig Config(JsonObject json) => json.Deserialize<AppConfig>(JsonStore.Options)!;

    private const string Secret = "AQAAANCMnd8BFdERjHoAwE";

    /// <summary>A PC with a widget on its second display, a Todoist token, its own tray and display choices.</summary>
    private static AppConfig Pc(string name)
    {
        var todo = new DockItem { Id = "todo1", Kind = DockItemKind.Widget, Widget = "todo", Display = @"\\.\DISPLAY2" };
        todo.Settings = new JsonObject { ["source"] = "Todoist", ["protectedToken"] = Secret + name };
        var config = new AppConfig
        {
            Edge = DockEdge.Bottom,
            MonitorDevice = $@"\\.\{name}-MAIN",
            StartWithWindows = name == "A",
            PinnedTrayIcons = new List<string> { name + "-tray" },
            Items = new List<DockItem>
            {
                new() { Id = "app1", Kind = DockItemKind.App, Path = @"C:\Windows\notepad.exe" },
                todo,
            },
        };
        config.DisplaySizes[@"\\.\DISPLAY2"] = DockSize.Small;
        return config;
    }

    [Fact]
    public void Shares_the_look_and_the_items_but_not_this_PCs_own_settings()
    {
        var shared = SyncMerge.Extract(Json(Pc("A")));
        Assert.True(shared.ContainsKey("edge"));
        Assert.True(shared.ContainsKey("items"));
        Assert.True(shared.ContainsKey("topBar"));
        foreach (var local in new[] { "monitorDevice", "startWithWindows", "pinnedTrayIcons", "displaySizes", "taskbarMode", "version", "language", "syncFolder", "syncDeviceId" })
            Assert.False(shared.ContainsKey(local), local);
        Assert.False(shared["items"]![1]!.AsObject().ContainsKey("display"));
    }

    [Fact]
    public void Secrets_are_never_written()
    {
        var config = Pc("A");
        config.Profiles.Add(new DockProfile
        {
            Name = "Work",
            Items = new JsonArray(new JsonObject { ["id"] = "x", ["kind"] = "Widget", ["settings"] = new JsonObject { ["protectedApiKey"] = "k" } }),
        });
        string text = new SyncFile("pc-a", "A", DateTime.UtcNow, SyncMerge.Extract(Json(config))).ToJson();
        Assert.DoesNotContain(Secret, text);
        Assert.DoesNotContain("protected", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Todoist", text);
    }

    [Fact]
    public void Taking_shared_settings_keeps_this_PCs_own()
    {
        var a = Pc("A");
        a.Edge = DockEdge.Left;
        a.Items.Add(new DockItem { Id = "sep", Kind = DockItemKind.Separator });
        var b = Pc("B");
        b.Items[1].Display = @"\\.\DISPLAY3";

        var merged = Config(SyncMerge.Merge(Json(b), SyncMerge.Extract(Json(a))));
        Assert.Equal(DockEdge.Left, merged.Edge);                 // shared
        Assert.Equal(3, merged.Items.Count);                      // shared
        Assert.Equal(@"\\.\B-MAIN", merged.MonitorDevice);         // B's own
        Assert.False(merged.StartWithWindows);                     // B's own
        Assert.Equal(new[] { "B-tray" }, merged.PinnedTrayIcons);  // B's own
        Assert.Equal(@"\\.\DISPLAY3", merged.Items[1].Display);    // B's own display for the widget
        Assert.Equal(Secret + "B", merged.Items[1].Settings!["protectedToken"]!.GetValue<string>()); // B's own token
    }

    [Fact]
    public void A_new_widget_from_another_PC_has_no_display_and_no_secret()
    {
        var a = Pc("A");
        var weather = new DockItem { Id = "weather", Kind = DockItemKind.Widget, Widget = "weather", Display = @"\\.\DISPLAY9" };
        weather.Settings = new JsonObject { ["city"] = "Ankara", ["protectedX"] = "s" };
        a.Items.Add(weather);
        var merged = Config(SyncMerge.Merge(Json(Pc("B")), SyncMerge.Extract(Json(a))));
        var item = merged.Items.Single(i => i.Id == "weather");
        Assert.Null(item.Display);
        Assert.Equal("Ankara", item.Settings!["city"]!.GetValue<string>());
        Assert.False(item.Settings.ContainsKey("protectedX"));
    }

    [Fact]
    public void Only_another_PCs_newer_file_is_taken()
    {
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var file = new SyncFile("pc-a", "A", now, new JsonObject());
        Assert.True(file.IsNewFor("pc-b", null));
        Assert.True(file.IsNewFor("pc-b", now.AddMinutes(-1)));
        Assert.False(file.IsNewFor("pc-b", now));                 // B wrote or took it already
        Assert.False(file.IsNewFor("pc-b", now.AddMinutes(1)));   // B's own later change wins
        Assert.False(file.IsNewFor("pc-a", null));                // A's own file
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"format\":1}")]
    [InlineData("{\"format\":2,\"deviceId\":\"a\",\"updatedAt\":\"2026-09-30T12:00:00Z\",\"settings\":{}}")]
    [InlineData("{\"format\":1,\"deviceId\":\"a\",\"updatedAt\":\"yesterday\",\"settings\":{}}")]
    [InlineData("{\"format\":1,\"deviceId\":\"a\",\"updatedAt\":\"2026-09-30T12:00:00Z\",\"settings\":[]}")]
    public void A_damaged_or_newer_file_is_not_read(string text) => Assert.Null(SyncFile.Parse(text));

    [Fact]
    public void The_file_round_trips()
    {
        var at = new DateTime(2026, 9, 30, 12, 34, 56, DateTimeKind.Utc);
        var file = SyncFile.Parse(new SyncFile("pc-a", "Laptop", at, new JsonObject { ["edge"] = "Left" }).ToJson())!;
        Assert.Equal("pc-a", file.DeviceId);
        Assert.Equal("Laptop", file.DeviceName);
        Assert.Equal(at, file.UpdatedAt.ToUniversalTime());
        Assert.Equal("Left", file.Settings["edge"]!.GetValue<string>());
    }

    [Fact]
    public void Two_PCs_share_through_one_folder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "dockhub-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, SyncFile.FileName);
        try
        {
            var t0 = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
            // PC A changes its edge and shares.
            var a = Pc("A");
            a.Edge = DockEdge.Top;
            File.WriteAllText(path, new SyncFile("pc-a", "A", t0, SyncMerge.Extract(Json(a))).ToJson());

            // PC B, which last synced before that, takes it.
            var b = Pc("B");
            DateTime? bSynced = t0.AddHours(-1);
            var file = SyncFile.Parse(File.ReadAllText(path))!;
            Assert.True(file.IsNewFor("pc-b", bSynced));
            b = Config(SyncMerge.Merge(Json(b), file.Settings));
            bSynced = file.UpdatedAt;
            Assert.Equal(DockEdge.Top, b.Edge);
            Assert.Equal(@"\\.\B-MAIN", b.MonitorDevice);

            // B's shared settings now match the file: nothing to write back.
            Assert.Equal(file.Settings.ToJsonString(), SyncMerge.Extract(Json(b)).ToJsonString());

            // B changes the size and shares; A takes it, keeping its own token.
            b.Size = DockSize.Large;
            var t1 = t0.AddMinutes(5);
            File.WriteAllText(path, new SyncFile("pc-b", "B", t1, SyncMerge.Extract(Json(b))).ToJson());
            file = SyncFile.Parse(File.ReadAllText(path))!;
            Assert.True(file.IsNewFor("pc-a", t0));
            Assert.False(file.IsNewFor("pc-b", t1));
            a = Config(SyncMerge.Merge(Json(a), file.Settings));
            Assert.Equal(DockSize.Large, a.Size);
            Assert.Equal(DockEdge.Top, a.Edge);
            Assert.Equal(Secret + "A", a.Items[1].Settings!["protectedToken"]!.GetValue<string>());
            Assert.True(a.StartWithWindows);
            Assert.DoesNotContain(Secret, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
