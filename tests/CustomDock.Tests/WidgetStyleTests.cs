using System.Text.Json;
using CustomDock.Core;

namespace CustomDock.Tests;

public class WidgetStyleTests
{
    [Fact]
    public void Settings_from_before_the_widget_style_keep_cards()
    {
        var config = JsonSerializer.Deserialize<AppConfig>("""{ "hoverEffect": true }""", JsonStore.Options)!;
        Assert.Equal(WidgetStyle.Cards, config.WidgetStyle);
        Assert.Equal(WidgetStyle.Cards, new AppConfig().WidgetStyle);
    }

    [Fact]
    public void The_widget_style_is_saved_by_name()
    {
        var json = JsonSerializer.Serialize(new AppConfig { WidgetStyle = WidgetStyle.Seamless }, JsonStore.Options);
        Assert.Contains("\"Seamless\"", json);
        Assert.Equal(WidgetStyle.Seamless, JsonSerializer.Deserialize<AppConfig>(json, JsonStore.Options)!.WidgetStyle);
    }

    [Fact]
    public void Undoing_a_preset_or_profile_change_brings_the_widget_style_back()
    {
        // Presets, profiles and undo keep the look through the appearance snapshot.
        var config = new AppConfig { WidgetStyle = WidgetStyle.Seamless };
        var snapshot = ConfigHistory.CaptureAppearance(config);
        config.WidgetStyle = WidgetStyle.Cards;
        ConfigHistory.RestoreAppearance(config, snapshot);
        Assert.Equal(WidgetStyle.Seamless, config.WidgetStyle);
    }

    [Fact]
    public void A_profile_saved_before_the_widget_style_gets_cards()
    {
        var config = new AppConfig { WidgetStyle = WidgetStyle.Seamless, HoverEffect = false };
        var old = ConfigHistory.CaptureAppearance(new AppConfig { HoverEffect = false });
        old.Remove(nameof(AppConfig.WidgetStyle));

        ConfigHistory.RestoreAppearance(config, old, missingAsDefault: true);
        Assert.Equal(WidgetStyle.Cards, config.WidgetStyle);
        Assert.False(config.HoverEffect);

        // Undo and theme files only set what they carry.
        config.WidgetStyle = WidgetStyle.Seamless;
        ConfigHistory.RestoreAppearance(config, old);
        Assert.Equal(WidgetStyle.Seamless, config.WidgetStyle);
    }
}
