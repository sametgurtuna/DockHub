namespace CustomDock.Core;

/// <summary>
/// Spreads the first network, process and device work of widgets over the seconds after DockHub starts, so the dock
/// appears first and sign-in isn't met with every request at once. Later work is not delayed.
/// </summary>
public static class StartupPacing
{
    public static readonly TimeSpan DeviceBatteries = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Weather = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan AIUsage = TimeSpan.FromSeconds(8);

    private static DateTime s_startedAt = DateTime.UtcNow;

    /// <summary>Called when the app starts; the offsets count from here.</summary>
    public static void MarkStart() => s_startedAt = DateTime.UtcNow;

    /// <summary>How long to wait before work that belongs <paramref name="offset"/> after startup (zero once it has passed).</summary>
    public static TimeSpan DelayFor(TimeSpan offset) => DelayFor(offset, s_startedAt, DateTime.UtcNow);

    internal static TimeSpan DelayFor(TimeSpan offset, DateTime startedAt, DateTime now)
    {
        var remaining = startedAt + offset - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public static Task WaitAsync(TimeSpan offset)
        => DelayFor(offset) is var delay && delay > TimeSpan.Zero ? Task.Delay(delay) : Task.CompletedTask;
}
