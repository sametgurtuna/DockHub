using System.Text.Json;
using System.Text.Json.Nodes;
using CustomDock.Core;

namespace CustomDock.Tests;

/// <summary>
/// tests/fixtures/config-windows.json is a settings file as Windows writes it, with every setting in use. DockHub for
/// Mac reads and writes the same file in its own tests (macos/Tests/DockHubCoreTests/WindowsConfigTests.swift), so a
/// setting added here without the Mac following fails one side.
/// </summary>
public class ConfigFixtureTests
{
    private static string Fixture
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures"))) dir = dir.Parent;
            return Path.Combine(dir!.FullName, "tests", "fixtures", "config-windows.json");
        }
    }

    [Fact]
    public void The_fixture_is_exactly_what_Windows_writes()
    {
        string json = File.ReadAllText(Fixture);
        var config = JsonSerializer.Deserialize<AppConfig>(json, JsonStore.Options)!;
        var written = JsonNode.Parse(JsonSerializer.Serialize(config, JsonStore.Options))!.AsObject();
        var fixture = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(fixture.Select(p => p.Key).Order(), written.Select(p => p.Key).Order());
        foreach (var (key, value) in fixture)
            Assert.True(JsonNode.DeepEquals(value, written[key]), $"{key}: {value?.ToJsonString()} ≠ {written[key]?.ToJsonString()}");
    }

    [Fact]
    public void Every_setting_differs_from_its_default()
    {
        // A value left at its default would not show whether the Mac keeps it.
        var fixture = JsonNode.Parse(File.ReadAllText(Fixture))!.AsObject();
        var defaults = JsonNode.Parse(JsonSerializer.Serialize(new AppConfig(), JsonStore.Options))!.AsObject();
        var same = defaults.Where(p => p.Key != "version" && JsonNode.DeepEquals(p.Value, fixture[p.Key])).Select(p => p.Key).ToList();
        Assert.Empty(same);
    }
}
