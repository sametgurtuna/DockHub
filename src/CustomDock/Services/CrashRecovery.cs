using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Shell;

namespace CustomDock.Services;

/// <summary>
/// Keeps the dock from disappearing for good: Windows restarts DockHub after a crash (Windows Error Reporting, only
/// for a process that ran at least 60 seconds), a loop guard stops that after repeated crashes, and the next start
/// tells the user what happened and offers a report.
/// </summary>
public static class CrashRecovery
{
    /// <summary>Command line Windows uses when it restarts DockHub after a crash.</summary>
    public const string RestartedArgument = "--restarted-after-crash";

    /// <summary>Tries out the crash path: with debugLogging on, DockHub crashes on purpose 65 s after starting.</summary>
    public const string CrashTestArgument = "--crash-test";

    public static readonly TimeSpan CrashTestDelay = TimeSpan.FromSeconds(65);

    /// <summary>Crash reports are looked up this long after start-up, when the dock is up.</summary>
    public static readonly TimeSpan NoticeDelay = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan EventLogTimeout = TimeSpan.FromSeconds(10);

    private static DateTime? s_previousStart;
    private static bool s_crashTest;

    /// <summary>The previous session never reached a clean exit (crash, Task Manager, power loss).</summary>
    public static bool PreviousSessionEndedUnexpectedly { get; private set; }

    /// <summary>
    /// Called once DockHub knows it is the running instance: reads how the last session ended, marks this one as running
    /// and registers the restart. Returns false when Windows restarted DockHub once too often (the loop guard): the
    /// caller shows <see cref="NotifyLoop"/> and exits.
    /// </summary>
    public static bool Begin(string[] args)
    {
        var now = DateTime.Now;
        s_crashTest = args.Any(a => a.Equals(CrashTestArgument, StringComparison.OrdinalIgnoreCase));
        bool restartedByWindows = args.Any(a => a.Equals(RestartedArgument, StringComparison.OrdinalIgnoreCase));
        var previous = SessionState.Load();
        PreviousSessionEndedUnexpectedly = previous.EndedUnexpectedly;
        s_previousStart = previous.RunningSince;

        var (crashes, loopDetected) = CrashLoopGuard.OnStart(previous.RecentCrashes, PreviousSessionEndedUnexpectedly,
            restartedByWindows, previous.AutoRestartOff, now);
        bool restartOff = previous.AutoRestartOff || loopDetected;

        SessionState.Update(s =>
        {
            s.RunningSince = now;
            s.CleanExit = false;
            s.RecentCrashes = crashes;
            if (loopDetected) s.AutoRestartOff = true;
        });

        if (PreviousSessionEndedUnexpectedly)
            Log.Warn($"The previous session (started {previous.RunningSince:g}) did not exit cleanly" +
                     (restartedByWindows ? $"; Windows restarted DockHub, {crashes.Count} time(s) in the last {CrashLoopGuard.Window.TotalMinutes:0} minutes." : "."));

        if (restartOff) Unregister();
        else Register();

        if (loopDetected)
        {
            Log.Warn("DockHub closed repeatedly; automatic restart after a crash is off.");
            return false;
        }
        return true;
    }

    /// <summary>Clean exit: nothing to report next time and no restart from Windows.</summary>
    public static void End()
    {
        try
        {
            SessionState.Update(s =>
            {
                s.CleanExit = true;
                s.RunningSince = null;
            });
            Unregister();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to record a clean exit");
        }
    }

    /// <summary>Settings › Backup and troubleshooting › Automatic restart: turns it back on after the loop guard.</summary>
    public static void EnableAutoRestart()
    {
        SessionState.Update(s =>
        {
            s.AutoRestartOff = false;
            s.RecentCrashes.Clear();
        });
        Register();
        Log.Info("Automatic restart after a crash turned back on.");
    }

    /// <summary>After start-up: the crash test, and the crash notice with what the event log says.</summary>
    public static void AfterStartup(Dispatcher dispatcher)
    {
        if (s_crashTest) StartCrashTest(dispatcher);
        if (!PreviousSessionEndedUnexpectedly || s_previousStart is not { } previousStart) return;

        var sessionEnd = DateTime.Now;
        _ = Task.Run(async () =>
        {
            await StartupPacing.WaitAsync(NoticeDelay);
            var lookup = Task.Run(() => CrashEventLog.Find(previousStart, sessionEnd));
            CrashRecord? record = null;
            try
            {
                if (await Task.WhenAny(lookup, Task.Delay(EventLogTimeout)) == lookup) record = await lookup;
                else Log.Warn("Reading the event log for crash reports took too long.");
            }
            catch (Exception ex)
            {
                Log.Warn($"Couldn't read crash reports from the event log: {ex.Message}");
            }
            // The PC restarted after DockHub started: a shutdown, power loss or update, not something to report.
            bool rebooted = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64) > previousStart;
            _ = dispatcher.BeginInvoke(() => ShowCrashNotice(record, rebooted));
        });
    }

    /// <summary>
    /// Shown as DockHub stops after the loop guard. No buttons: they would start DockHub again, which is what the guard
    /// just stopped (clicking the notification itself still starts it, as the user's choice).
    /// </summary>
    public static void NotifyLoop() => AppServices.Notifications.Show(
        L.T("DockHub closed repeatedly"),
        L.T("It closed {0} times within {1} minutes, so Windows no longer restarts it after a crash. Turn it back on in Settings › Backup and troubleshooting.",
            CrashLoopGuard.MaxCrashes, CrashLoopGuard.Window.TotalMinutes),
        "crash-loop");

    private static void ShowCrashNotice(CrashRecord? record, bool rebooted)
    {
        if (record is not null)
        {
            SessionState.Update(s => s.LastCrash = record);
            Log.Warn($"Crash of the previous session: {record.Summary()}");
        }
        else
        {
            Log.Info($"No crash report for the previous session in the event log{(rebooted ? "; the PC restarted since" : "")}.");
            if (rebooted) return;
        }

        AppServices.Notifications.Show(
            L.T("DockHub closed unexpectedly"),
            record is null
                ? L.T("It didn't shut down properly last time, for example because it was ended in Task Manager. If it crashed, reporting it helps fix the problem.")
                : L.T("It crashed last time and was started again. Reporting the problem helps fix it: the report includes where it crashed, nothing personal."),
            "crash",
            // "last": the report includes the crash just found; without one it has no crash field.
            new ToastAction(L.T("Report a problem"), NotificationService.ActionReportCrash, record is null ? null : "last"),
            new ToastAction(L.T("Open log"), NotificationService.ActionOpenLog));
    }

    private static void StartCrashTest(Dispatcher dispatcher)
    {
        if (!Log.DebugEnabled)
        {
            Log.Warn($"{CrashTestArgument} is ignored: it only works with \"debugLogging\": true in config.json.");
            return;
        }
        Log.Warn($"Crash test: DockHub crashes on purpose in {CrashTestDelay.TotalSeconds:0} s.");
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = CrashTestDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Environment.FailFast("DockHub crash test (--crash-test)");
        };
        timer.Start();
    }

    private static void Register()
    {
        // A crash test keeps crashing after each restart, so the loop guard can be tried out.
        string commandLine = s_crashTest ? $"{RestartedArgument} {CrashTestArgument}" : RestartedArgument;
        int result = NativeMethods.RegisterApplicationRestart(commandLine, NativeMethods.RESTART_NO_PATCH | NativeMethods.RESTART_NO_REBOOT);
        if (result != 0) Log.Warn($"RegisterApplicationRestart failed (0x{result:X8}).");
    }

    private static void Unregister()
    {
        try { NativeMethods.UnregisterApplicationRestart(); }
        catch (Exception ex) { Log.Warn($"UnregisterApplicationRestart failed: {ex.Message}"); }
    }
}
