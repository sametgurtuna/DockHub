using System.Text.Json.Serialization;
using CustomDock.Core;

namespace CustomDock.Shell;

/// <summary>
/// What has to survive a crash (session.json): the taskbar state to restore, whether the last session ended cleanly,
/// recent crashes for the restart loop guard and the last crash Windows recorded.
/// </summary>
public sealed class SessionState
{
    private static readonly object Gate = new();

    public bool TaskbarHidden { get; set; }

    public int OriginalTaskbarState { get; set; }

    /// <summary>When the running DockHub started; null when none is running.</summary>
    public DateTime? RunningSince { get; set; }

    /// <summary>False while DockHub runs. Files written before 0.10 don't have it and count as a clean exit.</summary>
    public bool CleanExit { get; set; } = true;

    /// <summary>Times Windows restarted DockHub after a crash, inside the loop guard's window.</summary>
    public List<DateTime> RecentCrashes
    {
        get => _recentCrashes;
        set => _recentCrashes = value ?? new(); // "recentCrashes": null in a hand-edited file
    }

    private List<DateTime> _recentCrashes = new();

    /// <summary>Set by the loop guard; Windows no longer restarts DockHub after a crash until the user turns it back on.</summary>
    public bool AutoRestartOff { get; set; }

    public CrashRecord? LastCrash { get; set; }

    /// <summary>The session this file describes started and never reached a clean exit.</summary>
    [JsonIgnore]
    public bool EndedUnexpectedly => !CleanExit && RunningSince is not null;

    public static SessionState Load()
    {
        lock (Gate) return JsonStore.Load<SessionState>(AppPaths.SessionFile);
    }

    /// <summary>Reads, changes and writes the file in one step, so other fields written meanwhile are kept.</summary>
    public static SessionState Update(Action<SessionState> change)
    {
        lock (Gate)
        {
            var state = JsonStore.Load<SessionState>(AppPaths.SessionFile);
            change(state);
            JsonStore.Save(AppPaths.SessionFile, state);
            return state;
        }
    }

    /// <summary>The taskbar is visible again; the crash fields stay.</summary>
    public static void ClearTaskbar() => Update(s =>
    {
        s.TaskbarHidden = false;
        s.OriginalTaskbarState = 0;
    });
}
