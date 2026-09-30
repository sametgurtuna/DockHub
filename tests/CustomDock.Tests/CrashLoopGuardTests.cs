using CustomDock.Core;

namespace CustomDock.Tests;

public class CrashLoopGuardTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0);

    [Fact]
    public void Two_crashes_are_not_a_loop()
    {
        var crashes = CrashLoopGuard.Record(new[] { Now.AddMinutes(-3) }, Now);
        Assert.Equal(2, crashes.Count);
        Assert.False(CrashLoopGuard.IsLooping(crashes, Now));
    }

    [Fact]
    public void Three_crashes_within_ten_minutes_are_a_loop()
    {
        var crashes = CrashLoopGuard.Record(new[] { Now.AddMinutes(-9), Now.AddMinutes(-2) }, Now);
        Assert.True(CrashLoopGuard.IsLooping(crashes, Now));
    }

    [Fact]
    public void Crashes_outside_the_window_are_forgotten()
    {
        var crashes = CrashLoopGuard.Record(new[] { Now.AddMinutes(-25), Now.AddMinutes(-11) }, Now);

        Assert.Equal(new[] { Now }, crashes);
        Assert.False(CrashLoopGuard.IsLooping(crashes, Now));
        // The same list is no loop any more once the window has passed.
        var loop = new[] { Now.AddMinutes(-4), Now.AddMinutes(-2), Now };
        Assert.True(CrashLoopGuard.IsLooping(loop, Now));
        Assert.False(CrashLoopGuard.IsLooping(loop, Now.AddMinutes(12)));
    }

    [Fact]
    public void Times_are_kept_in_order()
    {
        var crashes = CrashLoopGuard.Record(new[] { Now.AddMinutes(-1), Now.AddMinutes(-5) }, Now);
        Assert.Equal(new[] { Now.AddMinutes(-5), Now.AddMinutes(-1), Now }, crashes);
    }

    [Fact]
    public void Setting_the_clock_back_neither_hides_a_loop_nor_keeps_one_forever()
    {
        // The clock went back 5 minutes: the earlier crashes now look like they are in the future.
        Assert.True(CrashLoopGuard.IsLooping(new[] { Now.AddMinutes(2), Now.AddMinutes(4), Now }, Now));
        // A day back: those are not recent crashes.
        var crashes = CrashLoopGuard.Record(new[] { Now.AddDays(1), Now.AddDays(1).AddMinutes(1) }, Now);
        Assert.Equal(new[] { Now }, crashes);
    }

    [Fact]
    public void Only_restarts_by_Windows_make_a_loop()
    {
        var history = new List<DateTime> { Now.AddMinutes(-6), Now.AddMinutes(-3) };

        // Ended in Task Manager and started by hand: not recorded, no loop.
        var manual = CrashLoopGuard.OnStart(history, endedUnexpectedly: true, restartedByWindows: false, restartOff: false, Now);
        Assert.Equal(history, manual.Crashes);
        Assert.False(manual.LoopDetected);

        // The third restart by Windows within ten minutes stops the loop.
        var restarted = CrashLoopGuard.OnStart(history, endedUnexpectedly: true, restartedByWindows: true, restartOff: false, Now);
        Assert.Equal(3, restarted.Crashes.Count);
        Assert.True(restarted.LoopDetected);

        // Already off: nothing to stop.
        Assert.False(CrashLoopGuard.OnStart(history, true, true, restartOff: true, Now).LoopDetected);

        // A clean previous session is no crash, whatever started this one.
        var clean = CrashLoopGuard.OnStart(history, endedUnexpectedly: false, restartedByWindows: true, restartOff: false, Now);
        Assert.Equal(2, clean.Crashes.Count);
        Assert.False(clean.LoopDetected);
    }

    [Fact]
    public void Pruning_keeps_recent_crashes_in_order()
    {
        var crashes = CrashLoopGuard.Prune(new[] { Now.AddMinutes(-1), Now.AddMinutes(-30), Now.AddMinutes(-4) }, Now);
        Assert.Equal(new[] { Now.AddMinutes(-4), Now.AddMinutes(-1) }, crashes);
        Assert.Empty(CrashLoopGuard.Prune(null, Now));
    }

    [Fact]
    public void No_history_is_no_loop()
    {
        Assert.False(CrashLoopGuard.IsLooping(null, Now));
        Assert.Equal(new[] { Now }, CrashLoopGuard.Record(null, Now));
    }
}
