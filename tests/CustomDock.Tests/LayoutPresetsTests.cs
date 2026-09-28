using System.Text.Json.Nodes;
using CustomDock.Core;

namespace CustomDock.Tests;

[Collection(ConfigFileCollection.Name)]
public class LayoutPresetsTests : IDisposable
{
    private readonly ConfigService _service;

    public LayoutPresetsTests()
    {
        Directory.CreateDirectory(AppPaths.Root);
        foreach (var file in Directory.EnumerateFiles(AppPaths.Root, "config.json*")) File.Delete(file);
        _service = new ConfigService();
        _service.Load();
        _service.Config.Items = new List<DockItem>();
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(AppPaths.Root, "config.json*")) File.Delete(file);
    }

    private static LayoutPreset Preset(string id) => LayoutPresets.All.Single(p => p.Id == id);

    private DockItem AddWeather(string city)
    {
        var weather = DockItem.ForWidget("weather");
        weather.Settings = new JsonObject { ["cityName"] = city, ["latitude"] = 52.52, ["longitude"] = 13.4 };
        _service.Config.Items.Add(weather);
        return weather;
    }

    [Theory]
    [InlineData("minimal")]
    [InlineData("mac")]
    [InlineData("dashboard")]
    public void Horizontal_presets_keep_the_chosen_edge(string preset)
    {
        _service.Config.Edge = DockEdge.Right;
        LayoutPresets.Apply(Preset(preset), _service);
        Assert.Equal(DockEdge.Right, _service.Config.Edge);

        _service.Config.Edge = DockEdge.Top;
        LayoutPresets.Apply(Preset(preset), _service);
        Assert.Equal(DockEdge.Top, _service.Config.Edge);
    }

    [Fact]
    public void Side_bar_keeps_a_side_edge_and_moves_a_horizontal_dock_to_the_left()
    {
        _service.Config.Edge = DockEdge.Right;
        LayoutPresets.Apply(Preset("vertical"), _service);
        Assert.Equal(DockEdge.Right, _service.Config.Edge);

        _service.Config.Edge = DockEdge.Bottom;
        LayoutPresets.Apply(Preset("vertical"), _service);
        Assert.Equal(DockEdge.Left, _service.Config.Edge);
    }

    [Fact]
    public void Saved_layout_does_not_move_the_dock()
    {
        _service.Config.Edge = DockEdge.Bottom;
        var saved = LayoutPresets.SaveCurrent("Mine", _service);
        _service.Config.Edge = DockEdge.Right;

        LayoutPresets.Apply(LayoutPresets.FromCustom(saved), _service);

        Assert.Equal(DockEdge.Right, _service.Config.Edge);
    }

    [Fact]
    public void Weather_widget_keeps_its_city_across_presets_that_keep_it()
    {
        var weather = AddWeather("Berlin");

        LayoutPresets.Apply(Preset("dashboard"), _service);

        var kept = _service.Config.Items.Single(i => i.Widget == "weather");
        Assert.Same(weather, kept);
        Assert.Equal("Berlin", kept.Settings!["cityName"]!.GetValue<string>());
    }

    [Fact]
    public void Weather_city_comes_back_after_a_preset_without_weather()
    {
        AddWeather("Berlin");

        LayoutPresets.Apply(Preset("minimal"), _service);
        Assert.DoesNotContain(_service.Config.Items, i => i.Widget == "weather");

        LayoutPresets.Apply(Preset("mac"), _service);
        var weather = _service.Config.Items.Single(i => i.Widget == "weather");
        Assert.Equal("Berlin", weather.Settings!["cityName"]!.GetValue<string>());
        Assert.Equal(52.52, weather.Settings!["latitude"]!.GetValue<double>());
    }

    [Fact]
    public void Theme_import_does_not_move_the_dock()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "look" + ThemeFile.Extension);
        _service.Config.Edge = DockEdge.Bottom;
        ThemeFile.Export(_service.Config, path);

        // Files from 0.9.0 and earlier still carry an edge; it must be ignored.
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["appearance"]!["Edge"] = "Bottom";
        File.WriteAllText(path, root.ToJsonString());

        _service.Config.Edge = DockEdge.Right;
        Assert.Null(ThemeFile.Import(_service, path));
        Assert.Equal(DockEdge.Right, _service.Config.Edge);
    }
}
