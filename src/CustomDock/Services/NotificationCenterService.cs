using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using Microsoft.Win32;

namespace CustomDock.Services;

/// <summary>
/// What the Windows 11 taskbar shows next to its clock: how many notifications wait in the notification center and
/// whether Do Not Disturb is on. The count comes from Windows' own notification database (read-only, through the
/// SQLite that ships with Windows); Do Not Disturb from the notification settings. Started by the first subscriber.
/// </summary>
public sealed class NotificationCenterService
{
    private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

    private readonly DispatcherTimer _debounce;
    private FileSystemWatcher? _watcher;
    private RegistryWatcher? _settingsWatcher;
    private bool _started;

    public NotificationCenterService()
    {
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };
    }

    /// <summary>Notifications in the notification center (0 when unknown).</summary>
    public int Count { get; private set; }

    public bool DoNotDisturb { get; private set; }

    /// <summary>Raised on the UI thread when the count or Do Not Disturb changes.</summary>
    public event Action? Changed;

    private static string DatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Notifications\wpndatabase.db");

    public void EnsureStarted()
    {
        if (_started) return;
        _started = true;

        try
        {
            var folder = Path.GetDirectoryName(DatabasePath)!;
            if (Directory.Exists(folder))
            {
                // The database is written through its WAL file; any write means the list may have changed.
                _watcher = new FileSystemWatcher(folder, "wpndatabase.db*") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
                _watcher.Changed += (_, _) => QueueRefresh();
                _watcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Notification database is not watched: {ex.Message}");
        }

        _settingsWatcher = new RegistryWatcher(RegistryHive.CurrentUser, SettingsKey, subtree: false);
        _settingsWatcher.Changed += QueueRefresh;
        _settingsWatcher.Start();

        // Focus sessions and automatic rules turn Do Not Disturb on without touching the settings key.
        AppServices.Clock.MinuteTick += (_, _) => QueueRefresh();
        Refresh();
    }

    public void QueueRefresh() => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        _debounce.Stop();
        _debounce.Start();
    });

    private void Refresh()
    {
        // A failed read (database busy) keeps the last known count instead of flashing zero.
        int count = CountNotifications() ?? Count;
        bool dnd = ReadDoNotDisturb();
        if (count == Count && dnd == DoNotDisturb) return;
        Count = count;
        DoNotDisturb = dnd;
        Changed?.Invoke();
    }

    private static bool ReadDoNotDisturb()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
            if (key?.GetValue("NOC_GLOBAL_SETTING_DND") is int dnd && dnd != 0) return true;
            if (key?.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED") is int enabled && enabled == 0) return true;
        }
        catch { /* ignore */ }

        try
        {
            // Focus assist / quiet hours profile: 0 off, 1 priority only, 2 alarms only.
            ulong state = NativeMethods.WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED;
            uint size = sizeof(int);
            if (NativeMethods.NtQueryWnfStateData(ref state, IntPtr.Zero, IntPtr.Zero, out _, out int profile, ref size) == 0 && size >= sizeof(int))
                return profile != 0;
        }
        catch { /* not available */ }
        return false;
    }

    private static int? CountNotifications()
    {
        if (!File.Exists(DatabasePath)) return 0;
        IntPtr db = IntPtr.Zero;
        IntPtr statement = IntPtr.Zero;
        try
        {
            // Read-only, never creating or locking anything for the notification service.
            if (Sqlite.sqlite3_open_v2(DatabasePath, out db, Sqlite.SQLITE_OPEN_READONLY, IntPtr.Zero) != Sqlite.SQLITE_OK) return null;
            Sqlite.sqlite3_busy_timeout(db, 200);
            const string sql = "SELECT COUNT(*) FROM Notification WHERE Type = 'toast' AND (ExpiryTime = 0 OR ExpiryTime > ?1)";
            if (Sqlite.sqlite3_prepare16_v2(db, sql, -1, out statement, IntPtr.Zero) != Sqlite.SQLITE_OK) return null;
            Sqlite.sqlite3_bind_int64(statement, 1, DateTime.UtcNow.ToFileTimeUtc());
            return Sqlite.sqlite3_step(statement) == Sqlite.SQLITE_ROW ? Sqlite.sqlite3_column_int(statement, 0) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Log.Debug($"Notification count could not be read: {ex.Message}");
            return null;
        }
        finally
        {
            if (statement != IntPtr.Zero) Sqlite.sqlite3_finalize(statement);
            if (db != IntPtr.Zero) Sqlite.sqlite3_close(db);
        }
    }

    /// <summary>The SQLite library that ships with Windows 10 and 11 (no extra files).</summary>
    private static class Sqlite
    {
        private const string Library = "winsqlite3.dll";
        public const int SQLITE_OK = 0;
        public const int SQLITE_ROW = 100;
        public const int SQLITE_OPEN_READONLY = 0x1;

        [DllImport(Library, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);

        [DllImport(Library, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int bytes, out IntPtr statement, IntPtr tail);

        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_step(IntPtr statement);

        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_column_int(IntPtr statement, int column);

        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_finalize(IntPtr statement);

        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_close(IntPtr db);
    }
}
