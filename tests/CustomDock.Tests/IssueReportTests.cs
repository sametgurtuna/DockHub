using CustomDock.Core;

namespace CustomDock.Tests;

public class IssueReportTests
{
    private static IssueEnvironment Environment(IReadOnlyList<string>? widgets = null, string windows = "Windows 11 24H2 (build 26100.4061)")
        => new("0.9.0", windows, "Deutsch (de)", "Replace", 2, widgets ?? new[] { "clock", "weather", "clock" });

    private static Dictionary<string, string> Query(string url)
    {
        var uri = new Uri(url);
        return uri.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
    }

    [Theory]
    [InlineData(22631, "23H2", 4037, "Windows 11 23H2 (build 22631.4037)")]
    [InlineData(19045, "22H2", 0, "Windows 10 22H2 (build 19045)")]
    [InlineData(17763, null, 0, "Windows 10 (build 17763)")]
    [InlineData(22000, " ", 1, "Windows 11 (build 22000.1)")]
    public void Windows_is_named_by_its_build(int build, string? displayVersion, int revision, string expected)
    {
        Assert.Equal(expected, IssueReport.WindowsName(build, displayVersion, revision));
    }

    [Fact]
    public void The_url_fills_the_bug_report_form()
    {
        string url = IssueReport.BuildUrl(Environment());
        Assert.StartsWith(IssueReport.NewIssueUrl + "?template=bug_report.yml&", url);

        var query = Query(url);
        Assert.Equal("0.9.0", query["version"]);
        Assert.Equal("Windows 11 24H2 (build 26100.4061)", query["windows"]);
        Assert.Contains("Language: Deutsch (de)", query["setup"]);
        Assert.Contains("Taskbar mode: Replace", query["setup"]);
        Assert.Contains("Displays: 2", query["setup"]);
        Assert.Contains("Widgets: clock ×2, weather", query["setup"]);
    }

    [Fact]
    public void The_form_fields_in_the_url_exist_in_the_template()
    {
        string template = File.ReadAllText(Path.Combine(TestEnvironment.RepositoryRoot, ".github", "ISSUE_TEMPLATE", IssueReport.Template));
        foreach (string field in Query(IssueReport.BuildUrl(Environment())).Keys.Where(k => k != "template"))
            Assert.Contains($"id: {field}", template);
    }

    [Fact]
    public void A_dock_without_widgets_says_so()
    {
        Assert.EndsWith("Widgets: none", IssueReport.Setup(Environment(Array.Empty<string>())));
    }

    [Fact]
    public void Long_widget_lists_are_shortened_to_fit()
    {
        var widgets = Enumerable.Range(0, 400).Select(i => $"web:com.example.widget-with-a-long-name-{i}").ToList();
        string url = IssueReport.BuildUrl(Environment(widgets));

        Assert.True(url.Length <= IssueReport.MaxUrlLength, $"{url.Length} characters");
        Assert.Matches(@"and \d+ more$", Query(url)["setup"]);
    }

    [Fact]
    public void Non_latin_text_never_makes_the_url_too_long()
    {
        var widgets = Enumerable.Range(0, 200).Select(i => new string('ç', 50) + i).ToList();
        string url = IssueReport.BuildUrl(Environment(widgets, windows: new string('ğ', 500)));

        Assert.True(url.Length <= IssueReport.MaxUrlLength, $"{url.Length} characters");
        Assert.True(Query(url)["windows"].Length <= 100);
    }

    [Fact]
    public void Nothing_personal_is_in_the_report()
    {
        string setup = IssueReport.Setup(Environment());
        Assert.DoesNotContain(System.Environment.UserName, setup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@":\", setup);
    }
}
