using CustomDock.Dock;

namespace CustomDock.Tests;

public class AppButtonBadgeTests
{
    [Theory]
    [InlineData("(3) Inbox - Outlook", "3")]
    [InlineData("Inbox [12+] - Mail", "12+")]
    [InlineData("Discord (99+)", "99+")]
    [InlineData("WhatsApp ( 7 )", "7")]
    [InlineData("42", "42")]
    public void Counts_are_read_from_titles(string title, string count)
    {
        Assert.Equal((count, false), AppButtonBadge.FromTitles(new[] { title }));
    }

    [Theory]
    [InlineData("Report 2024 - Word")]
    [InlineData("Windows 11 Settings")]
    [InlineData("(12345) too long")]
    [InlineData("")]
    public void Ordinary_titles_have_no_badge(string title)
    {
        Assert.Equal(((string?)null, false), AppButtonBadge.FromTitles(new[] { title }));
    }

    [Theory]
    [InlineData("• Untitled")]
    [InlineData("*notes.txt - Notepad")]
    [InlineData("notes.txt * - Editor")]
    [InlineData("Chat • Team")]
    public void Unsaved_or_unread_markers_give_a_dot(string title)
    {
        Assert.Equal(((string?)null, true), AppButtonBadge.FromTitles(new[] { title }));
    }

    [Fact]
    public void A_count_in_any_window_wins_and_empty_titles_are_skipped()
    {
        var (count, dot) = AppButtonBadge.FromTitles(new[] { null, "  ", "*draft", "(5) Inbox" });
        Assert.Equal("5", count);
        Assert.True(dot);
    }
}
