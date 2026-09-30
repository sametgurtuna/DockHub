using System.Text.Json;
using CustomDock.Core;
using CustomDock.Shell;

namespace CustomDock.Tests;

public class SessionStateTests
{
    private static SessionState Read(string json) => JsonSerializer.Deserialize<SessionState>(json, JsonStore.Options)!;

    [Fact]
    public void A_file_from_before_crash_tracking_counts_as_a_clean_exit()
    {
        var state = Read("""{ "taskbarHidden": true, "originalTaskbarState": 2 }""");

        Assert.True(state.TaskbarHidden);
        Assert.Equal(2, state.OriginalTaskbarState);
        Assert.True(state.CleanExit);
        Assert.False(state.EndedUnexpectedly);
        Assert.False(state.AutoRestartOff);
        Assert.Empty(state.RecentCrashes);
        Assert.Null(state.LastCrash);
    }

    [Fact]
    public void A_running_session_that_never_exited_ended_unexpectedly()
    {
        var state = Read("""{ "runningSince": "2026-09-29T10:00:00", "cleanExit": false }""");
        Assert.True(state.EndedUnexpectedly);

        state.CleanExit = true;
        Assert.False(state.EndedUnexpectedly);
    }

    [Fact]
    public void A_null_crash_list_reads_as_empty()
    {
        var state = Read("""{ "recentCrashes": null }""");
        Assert.NotNull(state.RecentCrashes);
        Assert.Empty(state.RecentCrashes);
    }

    [Fact]
    public void Crash_fields_round_trip()
    {
        var state = new SessionState
        {
            RunningSince = new DateTime(2026, 9, 29, 10, 0, 0),
            CleanExit = false,
            AutoRestartOff = true,
            RecentCrashes = { new DateTime(2026, 9, 29, 9, 55, 0) },
            LastCrash = new CrashRecord { Module = "coreclr.dll", ExceptionCode = "c0000005", Offset = "0x1d45dc" },
        };

        var back = Read(JsonSerializer.Serialize(state, JsonStore.Options));

        Assert.Equal(state.RunningSince, back.RunningSince);
        Assert.False(back.CleanExit);
        Assert.True(back.AutoRestartOff);
        Assert.Equal(state.RecentCrashes, back.RecentCrashes);
        Assert.Equal("coreclr.dll c0000005 at 0x1d45dc", back.LastCrash!.Summary());
        Assert.DoesNotContain("endedUnexpectedly", JsonSerializer.Serialize(state, JsonStore.Options));
    }
}
