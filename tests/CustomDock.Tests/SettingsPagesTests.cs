using System.Text.Json;
using CustomDock.Core;
using CustomDock.Settings;

namespace CustomDock.Tests;

public class SettingsPagesTests
{
    // Tags used by links from the dock, the tray, notifications and older versions.
    public static readonly TheoryData<string> OldTags = new()
    {
        "general", "taskbar", "appearance", "items", "gallery", "profiles", "keyboard", "backup", "about",
    };

    [Theory]
    [MemberData(nameof(OldTags))]
    public void Every_old_link_still_opens_its_page(string tag)
    {
        Assert.Equal(tag, SettingsPages.Resolve(tag).Tag);
        Assert.Equal(tag, SettingsPages.Resolve(tag.ToUpperInvariant()).Tag);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-such-page")]
    public void An_unknown_link_opens_the_overview(string? tag)
        => Assert.Equal(SettingsPages.Overview, SettingsPages.Resolve(tag).Tag);

    [Fact]
    public void The_overview_opens_first_and_the_menu_is_grouped()
    {
        Assert.Equal(SettingsPages.Overview, SettingsPages.Default.Tag);
        Assert.Equal(new[] { "Dock", "Widgets", "System", null }, SettingsPages.Groups.Select(g => g.EnglishName));
        Assert.Equal(new[] { "overview", "appearance", "items", "taskbar" }, SettingsPages.Groups[0].Pages.Select(p => p.Tag));
        Assert.Equal("about", SettingsPages.All.Last().Tag);
    }

    [Fact]
    public void Every_page_appears_once_with_a_name_and_an_icon()
    {
        var tags = SettingsPages.All.Select(p => p.Tag).ToList();
        Assert.Equal(tags.Count, tags.Distinct().Count());
        Assert.Equal(10, tags.Count);
        Assert.All(SettingsPages.All, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.EnglishName));
            Assert.Single(p.Glyph);
        });
    }

    [Fact]
    public void Menu_texts_are_translated()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("CustomDock.Resources.Strings_tr.json")!;
        var turkish = JsonSerializer.Deserialize<Dictionary<string, string>>(stream,
            new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        var texts = SettingsPages.All.Select(p => p.EnglishName)
            .Concat(SettingsPages.Groups.Select(g => g.EnglishName).OfType<string>());
        Assert.All(texts, text => Assert.True(turkish.ContainsKey(text), $"Missing Turkish: {text}"));
    }
}
