using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Threading;
using CustomDock.Core;
using ManagedShell;
using ManagedShell.WindowsTasks;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Shell;

/// <summary>
/// Keeps ManagedShell's task list in sync with window visibility.
/// <para>
/// ManagedShell evaluates a window's <c>ShowInTaskbar</c> once and caches it; only a few shell hook messages
/// re-evaluate it. Apps that create their main window hidden and show it later (Electron apps like Discord,
/// apps restored from the tray) can stay cached as "not in taskbar" and never appear on the dock.
/// This watcher listens for top-level show/hide/uncloak events and re-evaluates (or adds) those windows.
/// </para>
/// </summary>
public sealed class WindowVisibilityWatcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    private readonly ShellManager _manager;
    private readonly Dispatcher _dispatcher;
    private readonly WinEventProc _callback;
    private readonly List<IntPtr> _hooks = new();
    private readonly HashSet<IntPtr> _pending = new();
    private readonly DispatcherTimer _flushTimer;
    private readonly MethodInfo? _addWindow;
    private readonly object? _windowsLock;

    public WindowVisibilityWatcher(ShellManager manager)
    {
        _manager = manager;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _callback = OnWinEvent; // kept in a field so the delegate isn't collected while hooked
        _flushTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = Debounce };
        _flushTimer.Tick += (_, _) => Flush();

        // ManagedShell 0.0.372 has no public "add window" API; its private addWindow also sends TaskbarButtonCreated,
        // which apps need for progress bars and overlay badges. Fall back to adding the window ourselves.
        var tasksServiceType = typeof(TasksService);
        _addWindow = tasksServiceType.GetMethod("addWindow", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(IntPtr), typeof(ApplicationWindow.WindowState), typeof(bool) }, null);
        _windowsLock = tasksServiceType.GetField("_windowsLock", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(manager.TasksService);
        if (_addWindow is null) Log.Warn("ManagedShell addWindow not found; using the fallback for late windows.");

        Hook(EVENT_OBJECT_SHOW, EVENT_OBJECT_HIDE);
        Hook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED);
    }

    /// <summary>ManagedShell's unfiltered window list (GroupedWindows only shows windows with ShowInTaskbar).</summary>
    public ObservableCollection<ApplicationWindow>? AllWindows =>
        (_manager.Tasks.GroupedWindows as CollectionView)?.SourceCollection as ObservableCollection<ApplicationWindow>;

    private void Hook(uint min, uint max)
    {
        var hook = SetWinEventHook(min, max, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (hook != IntPtr.Zero) _hooks.Add(hook);
        else Log.Warn($"SetWinEventHook failed for 0x{min:X}-0x{max:X}.");
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != 0) return;
        if (GetAncestor(hwnd, GA_ROOT) != hwnd) return; // top-level windows only

        // Out-of-context hooks are delivered on the thread that installed them (the UI thread).
        _pending.Add(hwnd);
        if (!_flushTimer.IsEnabled) _flushTimer.Start();
    }

    private void Flush()
    {
        _flushTimer.Stop();
        if (_pending.Count == 0) return;
        var handles = _pending.ToList();
        _pending.Clear();

        var windows = AllWindows;
        if (windows is null) return;

        foreach (var hwnd in handles)
        {
            try
            {
                var existing = windows.FirstOrDefault(w => w.Handle == hwnd);
                if (existing is not null)
                {
                    bool before = existing.ShowInTaskbar;
                    existing.SetShowInTaskbar();
                    if (before != existing.ShowInTaskbar)
                        Log.Debug($"Taskbar visibility re-evaluated: {existing.Title} ({hwnd}) -> {existing.ShowInTaskbar}");
                }
                else if (IsWindow(hwnd) && IsWindowVisible(hwnd) && TaskbarEligibility.IsEligible(hwnd))
                {
                    AddWindow(windows, hwnd);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to update window {hwnd}");
            }
        }
    }

    private void AddWindow(ObservableCollection<ApplicationWindow> windows, IntPtr hwnd)
    {
        if (_addWindow is not null)
        {
            lock (_windowsLock ?? windows)
            {
                if (windows.Any(w => w.Handle == hwnd)) return;
                _addWindow.Invoke(_manager.TasksService, new object[] { hwnd, ApplicationWindow.WindowState.Inactive, true });
            }
        }
        else
        {
            var window = new ApplicationWindow(_manager.TasksService, hwnd);
            if (!window.CanAddToTaskbar) return;
            windows.Add(window);
        }
        Log.Debug($"Late window added to task list: {hwnd}");
    }

    public void Dispose()
    {
        _flushTimer.Stop();
        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
    }
}

/// <summary>DockHub's copy of ManagedShell's taskbar eligibility rule (ApplicationWindow.CanAddToTaskbar).</summary>
public static class TaskbarEligibility
{
    public static bool IsEligible(IntPtr hwnd)
    {
        long exStyle = GetExStyle(hwnd);
        bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
        bool isAppWindow = (exStyle & WS_EX_APPWINDOW) != 0;
        bool isNoActivate = (exStyle & WS_EX_NOACTIVATE) != 0;
        bool hasOwner = GetWindow(hwnd, GW_OWNER) != IntPtr.Zero;
        bool deleted = GetProp(hwnd, "ITaskList_Deleted") != IntPtr.Zero;
        return IsWindowVisible(hwnd) && !IsCloaked(hwnd) && (!hasOwner || isAppWindow) &&
               (!isNoActivate || isAppWindow) && !isToolWindow && !deleted;
    }

    /// <summary>Human-readable flags for diagnostics.</summary>
    public static string Describe(IntPtr hwnd)
    {
        long exStyle = GetExStyle(hwnd);
        return $"visible={IsWindowVisible(hwnd)} cloaked={IsCloaked(hwnd)} owner=0x{GetWindow(hwnd, GW_OWNER).ToInt64():X} " +
               $"exStyle=0x{exStyle:X8} tool={(exStyle & WS_EX_TOOLWINDOW) != 0} app={(exStyle & WS_EX_APPWINDOW) != 0} " +
               $"noActivate={(exStyle & WS_EX_NOACTIVATE) != 0} taskListDeleted={GetProp(hwnd, "ITaskList_Deleted") != IntPtr.Zero}";
    }
}
