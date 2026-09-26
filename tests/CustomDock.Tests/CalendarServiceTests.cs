using CustomDock.Services;

namespace CustomDock.Tests;

public class CalendarServiceTests
{
    private static string Ics(string events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//DockHub//Test//EN\r\n" + events + "END:VCALENDAR\r\n";

    [Fact]
    public void Recurring_events_are_expanded_and_meeting_links_found()
    {
        var monday = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek + 1).AddHours(10);
        string ics = Ics(
            "BEGIN:VEVENT\r\nUID:standup\r\nSUMMARY:Standup\r\n" +
            $"DTSTART:{monday:yyyyMMdd'T'HHmmss}\r\nDTEND:{monday.AddMinutes(15):yyyyMMdd'T'HHmmss}\r\n" +
            "RRULE:FREQ=DAILY;COUNT=5\r\nLOCATION:https://meet.google.com/abc-defg-hij\r\nEND:VEVENT\r\n");

        var entries = CalendarService.Parse(ics, monday.Date, monday.Date.AddDays(7)).ToList();
        Assert.Equal(5, entries.Count);
        Assert.All(entries, e => Assert.Equal("Standup", e.Title));
        Assert.Equal("https://meet.google.com/abc-defg-hij", entries[0].JoinUrl);
        Assert.Equal(monday, entries[0].Start);
    }

    [Fact]
    public void All_day_events_and_missing_titles_are_handled()
    {
        var today = DateTime.Today;
        string ics = Ics(
            "BEGIN:VEVENT\r\nUID:holiday\r\n" +
            $"DTSTART;VALUE=DATE:{today:yyyyMMdd}\r\nDTEND;VALUE=DATE:{today.AddDays(1):yyyyMMdd}\r\nEND:VEVENT\r\n");

        var entry = Assert.Single(CalendarService.Parse(ics, today, today.AddDays(2)));
        Assert.True(entry.AllDay);
        Assert.Equal("(No title)", entry.Title);
    }

    [Fact]
    public void Broken_feeds_give_no_events()
        => Assert.Empty(CalendarService.Parse("not a calendar", DateTime.Today, DateTime.Today.AddDays(1)));
}
