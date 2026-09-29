namespace CustomDock.Settings;

/// <summary>One page of the settings window: its tag (used by links such as ShowSettings("items")), name and icon.</summary>
public sealed record SettingsPage(string Tag, string EnglishName, string Glyph);

/// <summary>A heading in the settings menu and the pages under it (a heading can't be selected).</summary>
public sealed record SettingsGroup(string? EnglishName, IReadOnlyList<SettingsPage> Pages);

/// <summary>The settings menu, grouped: Dock, Widgets, System and About. The first page opens by default.</summary>
public static class SettingsPages
{
    public const string Overview = "overview";

    public static IReadOnlyList<SettingsGroup> Groups { get; } = new[]
    {
        new SettingsGroup("Dock", new[]
        {
            new SettingsPage(Overview, "Overview", ""),
            new SettingsPage("appearance", "Appearance", ""),
            new SettingsPage("items", "Dock items", ""),
            new SettingsPage("taskbar", "Taskbar", ""),
        }),
        new SettingsGroup("Widgets", new[]
        {
            new SettingsPage("gallery", "Widget gallery", ""),
        }),
        new SettingsGroup("System", new[]
        {
            new SettingsPage("general", "General", ""),
            new SettingsPage("profiles", "Profiles", ""),
            new SettingsPage("keyboard", "Keyboard shortcuts", ""),
            new SettingsPage("backup", "Backup and troubleshooting", ""),
        }),
        new SettingsGroup(null, new[]
        {
            new SettingsPage("about", "About", ""),
        }),
    };

    public static IEnumerable<SettingsPage> All => Groups.SelectMany(g => g.Pages);

    public static SettingsPage Default => All.First();

    /// <summary>The page for <paramref name="tag"/>; an unknown or empty tag (an old link) opens the default page.</summary>
    public static SettingsPage Resolve(string? tag) =>
        All.FirstOrDefault(p => string.Equals(p.Tag, tag, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
