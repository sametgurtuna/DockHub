using System.Text.Json;
using CustomDock.Core;
using CustomDock.Widgets;

namespace CustomDock.Tests;

public class WidgetRegistryTests
{
    private static Dictionary<string, string> Turkish()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("CustomDock.Resources.Strings_tr.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        })!;
    }

    [Fact]
    public void Widget_ids_and_variant_ids_are_unique()
    {
        var ids = WidgetRegistry.All.Select(d => d.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (var descriptor in WidgetRegistry.All)
        {
            Assert.NotEmpty(descriptor.Variants);
            Assert.Equal(descriptor.Variants.Count, descriptor.Variants.Select(v => v.Id).Distinct().Count());
        }
    }

    [Fact]
    public void Gallery_texts_are_translated()
    {
        var turkish = Turkish();
        var missing = WidgetRegistry.All
            .SelectMany(d => new[] { d.EnglishName, d.EnglishDescription, d.Category }.Concat(d.Variants.Select(v => v.EnglishName)))
            .Where(text => !string.IsNullOrEmpty(text) && !turkish.ContainsKey(text))
            .Distinct()
            .ToList();
        Assert.True(missing.Count == 0, "Missing Turkish: " + string.Join(" | ", missing));
    }

    [Fact]
    public void Widget_settings_have_a_template_type_that_can_be_created()
    {
        foreach (var descriptor in WidgetRegistry.All.Where(d => d.SettingsType is not null))
            Assert.NotNull(Activator.CreateInstance(descriptor.SettingsType!));
    }

    [Fact]
    public void Old_battery_widget_settings_get_the_new_defaults()
    {
        var old = JsonSerializer.Deserialize<BatteryDevicesSettings>("""{ "order": ["bt-38184C12ABCD"] }""", JsonStore.Options)!;
        Assert.Equal(new[] { "bt-38184C12ABCD" }, old.Order);
        Assert.Equal(15, old.LowBatteryAlert);

        var clamped = JsonSerializer.Deserialize<BatteryDevicesSettings>("""{ "lowBatteryAlert": 99 }""", JsonStore.Options)!;
        Assert.Equal(50, clamped.LowBatteryAlert);
    }
}
