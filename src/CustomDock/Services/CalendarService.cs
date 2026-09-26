using System.Net.Http;
using System.Text.RegularExpressions;
using CustomDock.Core;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace CustomDock.Services;

/// <summary>An event occurrence from a subscribed calendar.</summary>
public sealed record CalendarEntry(string Title, DateTime Start, DateTime End, bool AllDay, string? Location, string? JoinUrl);

/// <summary>
/// Reads calendars from their iCal (.ics) subscription links: Google Calendar's "secret address in iCal format",
/// Outlook's "publish calendar" ICS link, or any other ICS feed. No sign-in, no API keys; feeds are cached for
/// 15 minutes and shared by all calendar widgets.
/// </summary>
public static class CalendarService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);
    private static readonly Dictionary<string, (DateTime FetchedAt, string Text)> Feeds = new(StringComparer.Ordinal);

    private static readonly Regex MeetingLinkRx = new(
        @"https://(?:teams\.microsoft\.com|teams\.live\.com|meet\.google\.com|[\w.-]*zoom\.us|[\w.-]*webex\.com)/[^\s""'<>)\]]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Events from all feeds between now (minus the running ones) and <paramref name="days"/> days ahead.</summary>
    public static async Task<List<CalendarEntry>> GetUpcomingAsync(IEnumerable<string> feedUrls, int days = 7)
    {
        var entries = new List<CalendarEntry>();
        var from = DateTime.Now.Date;
        var to = from.AddDays(days + 1);
        foreach (var url in feedUrls.Select(u => u.Trim()).Where(u => u.Length > 0))
        {
            string text = await FetchAsync(url).ConfigureAwait(true);
            entries.AddRange(Parse(text, from, to));
        }
        return entries.Where(e => e.End > DateTime.Now || (e.AllDay && e.Start.Date == DateTime.Today))
            .OrderBy(e => e.Start).ToList();
    }

    private static async Task<string> FetchAsync(string url)
    {
        if (Feeds.TryGetValue(url, out var cached) && DateTime.Now - cached.FetchedAt < CacheFor) return cached.Text;
        // webcal:// links are plain https feeds.
        string httpUrl = url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + url[9..] : url;
        string text = await Http.GetStringAsync(httpUrl).ConfigureAwait(true);
        Feeds[url] = (DateTime.Now, text);
        return text;
    }

    /// <summary>Expands recurring events into occurrences inside the range.</summary>
    internal static IEnumerable<CalendarEntry> Parse(string ics, DateTime from, DateTime to)
    {
        Calendar? calendar;
        try { calendar = Calendar.Load(ics); }
        catch (Exception ex)
        {
            Log.Warn($"Calendar feed can't be read: {ex.Message}");
            yield break;
        }
        if (calendar is null) yield break;

        foreach (var occurrence in calendar.GetOccurrences(new CalDateTime(from), new CalDateTime(to)))
        {
            if (occurrence.Source is not CalendarEvent ev) continue;
            var start = occurrence.Period.StartTime.AsSystemLocal;
            var end = occurrence.Period.EndTime?.AsSystemLocal ?? start.AddHours(1);
            string text = $"{ev.Location} {ev.Description} {ev.Url}";
            string? join = MeetingLinkRx.Match(text) is { Success: true } m ? m.Value : null;
            yield return new CalendarEntry(string.IsNullOrWhiteSpace(ev.Summary) ? L.T("(No title)") : ev.Summary.Trim(),
                start, end, ev.IsAllDay, string.IsNullOrWhiteSpace(ev.Location) ? null : ev.Location.Trim(), join);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DockHub (+https://github.com/sametgurtuna/DockHub)");
        return client;
    }
}
