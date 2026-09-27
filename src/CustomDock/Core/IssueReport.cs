using System.Text;

namespace CustomDock.Core;

/// <summary>What a bug report says about the PC. Nothing personal: no window titles, paths or names.</summary>
public sealed record IssueEnvironment(
    string Version, string Windows, string Language, string TaskbarMode, int Displays, IReadOnlyList<string> Widgets);

/// <summary>
/// The GitHub issue that Settings › About › Report a problem opens, prefilled through the bug report form
/// (<c>.github/ISSUE_TEMPLATE/bug_report.yml</c>; each query parameter fills the field with that id).
/// </summary>
public static class IssueReport
{
    public const string NewIssueUrl = "https://github.com/sametgurtuna/DockHub/issues/new";
    public const string Template = "bug_report.yml";

    /// <summary>GitHub and browsers refuse addresses of about 8 KB; the report stays well below that.</summary>
    public const int MaxUrlLength = 4000;

    private const int MaxSetupLength = 1500;

    /// <summary>"Windows 11 23H2 (build 22631.4037)"; Windows 11 still calls itself Windows 10 in the registry.</summary>
    public static string WindowsName(int build, string? displayVersion, int updateRevision)
    {
        string name = build >= 22000 ? "Windows 11" : "Windows 10";
        if (!string.IsNullOrWhiteSpace(displayVersion)) name += " " + displayVersion.Trim();
        return updateRevision > 0 ? $"{name} (build {build}.{updateRevision})" : $"{name} (build {build})";
    }

    /// <summary>The setup summary for the form's "setup" field.</summary>
    public static string Setup(IssueEnvironment environment, int maxWidgets = int.MaxValue)
    {
        var sb = new StringBuilder();
        sb.Append("Language: ").AppendLine(environment.Language);
        sb.Append("Taskbar mode: ").AppendLine(environment.TaskbarMode);
        sb.Append("Displays: ").AppendLine(environment.Displays.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var widgets = environment.Widgets
            .GroupBy(w => w, StringComparer.Ordinal)
            .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key)
            .ToList();
        var shown = widgets.Take(maxWidgets).ToList();
        sb.Append("Widgets: ");
        if (widgets.Count == 0) sb.Append("none");
        else if (shown.Count == 0) sb.Append($"{widgets.Count} kinds");
        else sb.Append(string.Join(", ", shown));
        if (shown.Count > 0 && shown.Count < widgets.Count) sb.Append($", and {widgets.Count - shown.Count} more");
        return sb.ToString();
    }

    /// <summary>The new-issue address with the form's fields filled in, never longer than <see cref="MaxUrlLength"/>.</summary>
    public static string BuildUrl(IssueEnvironment environment)
    {
        string version = Clip(environment.Version, 40);
        string windows = Clip(environment.Windows, 100);
        // Fewer widgets until it fits, so the list ends with "and N more" rather than a cut-off name
        // (non-Latin text grows up to nine times when escaped).
        foreach (int maxWidgets in new[] { int.MaxValue, 30, 15, 5, 0 })
        {
            string setup = Setup(environment, maxWidgets);
            if (setup.Length > MaxSetupLength) continue;
            string url = Url(version, windows, setup);
            if (url.Length <= MaxUrlLength) return url;
        }
        return Url(version, windows, Clip(Setup(environment, 0), 200));
    }

    private static string Url(string version, string windows, string setup)
        => $"{NewIssueUrl}?template={Template}&version={Uri.EscapeDataString(version)}"
           + $"&windows={Uri.EscapeDataString(windows)}&setup={Uri.EscapeDataString(setup)}";

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
