using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>Finds what Windows recorded about a DockHub crash in the Application event log.</summary>
public static class CrashEventLog
{
    // Other apps' crashes share the providers and come first in reverse order; reading a few hundred is still quick.
    private const int MaxEvents = 200;

    /// <summary>
    /// A crash of DockHub between <paramref name="from"/> (the start of the session in question, so a crash before
    /// it that Windows restarted from is not blamed on it) and <paramref name="to"/>, or null. Runs on any thread.
    /// </summary>
    public static CrashRecord? Find(DateTime from, DateTime to)
    {
        string since = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        string until = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        string query =
            "*[System[Provider[@Name='Application Error' or @Name='.NET Runtime'] and " +
            "(EventID=1000 or EventID=1023 or EventID=1025 or EventID=1026) and " +
            $"TimeCreated[@SystemTime>='{since}' and @SystemTime<='{until}']]]";

        var records = new List<CrashRecord>();
        using var reader = new EventLogReader(new EventLogQuery("Application", PathType.LogName, query) { ReverseDirection = true });
        for (int i = 0; i < MaxEvents && reader.ReadEvent() is { } entry; i++)
        {
            using (entry)
            {
                if (Read(entry) is { } record) records.Add(record);
            }
        }
        if (records.Count == 0) return null;

        // The newest crash; Windows writes its .NET and Application Error events within a few seconds of each other.
        var newest = records.Max(r => r.Time);
        return CrashRecord.Merge(records.Where(r => newest - r.Time <= TimeSpan.FromMinutes(1)));
    }

    private static CrashRecord? Read(EventRecord entry)
    {
        var time = entry.TimeCreated ?? DateTime.Now;
        var data = entry.Properties.Select(p => p.Value?.ToString()).ToList();
        if (entry.ProviderName == CrashRecord.ApplicationErrorSource)
            return CrashRecord.FromApplicationError(data, time);
        // .NET Runtime puts its whole (English) message into the first data item.
        return CrashRecord.Parse(data.FirstOrDefault(), time);
    }
}
