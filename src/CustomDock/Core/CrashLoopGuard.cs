namespace CustomDock.Core;

/// <summary>
/// Stops Windows from restarting DockHub over and over: after <see cref="MaxCrashes"/> unexpected exits within
/// <see cref="Window"/>, automatic restart is turned off until the user turns it back on.
/// </summary>
public static class CrashLoopGuard
{
    public const int MaxCrashes = 3;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>The crash times to keep after one at <paramref name="crash"/>: those still inside the window, oldest first.</summary>
    public static List<DateTime> Record(IEnumerable<DateTime>? previous, DateTime crash)
    {
        var kept = Prune(previous, crash);
        kept.Add(crash);
        kept.Sort();
        return kept;
    }

    /// <summary>The crash times still inside the window at <paramref name="now"/>, oldest first.</summary>
    public static List<DateTime> Prune(IEnumerable<DateTime>? crashes, DateTime now)
        => (crashes ?? Enumerable.Empty<DateTime>()).Where(t => InWindow(t, now)).OrderBy(t => t).ToList();

    /// <summary>True when the crashes inside the window reach <see cref="MaxCrashes"/>.</summary>
    public static bool IsLooping(IEnumerable<DateTime>? crashes, DateTime now)
        => (crashes ?? Enumerable.Empty<DateTime>()).Count(t => InWindow(t, now)) >= MaxCrashes;

    /// <summary>What a start means for the loop guard: the crash times to keep and whether this start ends a loop.</summary>
    public readonly record struct StartDecision(List<DateTime> Crashes, bool LoopDetected);

    /// <summary>
    /// A new start. Only a start by Windows after a crash (<paramref name="restartedByWindows"/>) counts: ending DockHub
    /// in Task Manager and starting it again is no loop. With restart already off there is nothing left to stop.
    /// </summary>
    public static StartDecision OnStart(IEnumerable<DateTime>? previous, bool endedUnexpectedly, bool restartedByWindows,
        bool restartOff, DateTime now)
    {
        bool counts = endedUnexpectedly && restartedByWindows;
        var crashes = counts ? Record(previous, now) : Prune(previous, now);
        return new StartDecision(crashes, counts && !restartOff && IsLooping(crashes, now));
    }

    // Either side of now: after the clock is set back, recent crashes look like they are in the future.
    private static bool InWindow(DateTime crash, DateTime now) => (now - crash).Duration() <= Window;
}
