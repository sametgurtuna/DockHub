using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using ManagedShell;
using ManagedShell.Common.Enums;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.UWPInterop;
using ManagedShell.WindowsTray;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Shell;

/// <summary>
/// Hosts shell services (ManagedShell): running windows, system tray, full screen detection,
/// hiding Explorer taskbar, and invoking system interfaces like Start/Search/Notification center.
/// </summary>
public sealed class ShellHost : IDisposable
{
    private readonly AppVisibilityHelper _appVisibility;
    private readonly DispatcherTimer _launcherPoller;
    private readonly EventHandler _launcherTickHandler;
    private bool _launcherVisible;

    public ShellHost(bool replaceTaskbar, IEnumerable<string>? pinnedTrayIcons)
    {
        IsReplacingTaskbar = replaceTaskbar;
        EnvironmentHelper.IsAppRunningAsShell = GetShellWindow() == IntPtr.Zero;
        ShellLogger.Severity = LogSeverity.Warning;
        ShellLogger.Attach(new LogBridge());

        // TasksService redirects Win key messages (SC_TASKLIST) to its own window.
        // We restore the old "taskman" window so Explorer can open the Start menu.
        var originalTaskman = GetTaskmanWindow();

        var config = ShellManager.DefaultShellConfig;
        config.EnableTrayService = replaceTaskbar;
        config.AutoStartTrayService = replaceTaskbar;
        config.AutoStartTasksService = false;
        config.TaskIconSize = IconSize.Large;
        config.PinnedNotifyIcons = pinnedTrayIcons?.ToArray() ?? NotificationArea.DEFAULT_PINNED;

        Manager = new ShellManager(config);
        Manager.Tasks.Initialize(new AppCategoryProvider(), true);

        if (originalTaskman != IntPtr.Zero && IsWindow(originalTaskman) && GetTaskmanWindow() != originalTaskman)
        {
            SetTaskmanWindow(originalTaskman);
            Log.Info("Taskman window returned to Explorer.");
        }

        _appVisibility = new AppVisibilityHelper(false);
        Taskbar = new TaskbarController(() => Manager.NotificationArea?.Handle ?? IntPtr.Zero, () => _launcherVisible);
        RunningApps = new RunningAppsService(Manager);
        VisibilityWatcher = new WindowVisibilityWatcher(Manager);

        _launcherTickHandler = (_, _) => PollLauncher();
        _launcherPoller = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _launcherPoller.Tick += _launcherTickHandler;
        _launcherPoller.Start();
    }

    public ShellManager Manager { get; }

    public bool IsReplacingTaskbar { get; }

    public TaskbarController Taskbar { get; }

    public RunningAppsService RunningApps { get; }

    public WindowVisibilityWatcher VisibilityWatcher { get; }

    public NotificationArea? Tray => Manager.NotificationArea;

    /// <summary>Is Start menu or Search open?</summary>
    public bool IsLauncherVisible => _launcherVisible;

    public event Action<bool>? LauncherVisibilityChanged;

    private void PollLauncher()
    {
        bool visible;
        try
        {
            visible = _appVisibility.IsLauncherVisible();
        }
        catch
        {
            visible = false;
        }

        if (visible == _launcherVisible) return;
        _launcherVisible = visible;
        LauncherVisibilityChanged?.Invoke(visible);
    }

    // ------------------------------------------------------------------ System interfaces

    public void ShowStartMenu(IntPtr anchorWindow) => StartMenuLauncher.Show(anchorWindow);

    public void ShowStartContextMenu() => InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_X);

    public void ShowSearch() => InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_S);

    public void ShowTaskView() => InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_TAB);

    /// <summary>Notification center + calendar (clock click).</summary>
    public void ShowNotificationCenter()
    {
        try
        {
            ImmersiveShellHelper.ShowActionCenter();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open notification center");
            InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_N);
        }
    }

    /// <summary>Quick settings (network, volume, battery).</summary>
    public void ShowQuickSettings()
    {
        try
        {
            ImmersiveShellHelper.ShowControlCenter();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open quick settings");
            InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_A);
        }
    }

    public void ToggleDesktop() => Taskbar.ToggleDesktop();

    /// <summary>
    /// Start menu, quick settings, and notifications align assuming this rectangle is the "taskbar".
    /// </summary>
    public void SetTrayHost(RECT rect, DockEdge edge)
    {
        Tray?.SetTrayHostSizeData(new TrayHostSizeData
        {
            edge = edge switch
            {
                DockEdge.Top => ManagedShell.Interop.NativeMethods.ABEdge.ABE_TOP,
                DockEdge.Left => ManagedShell.Interop.NativeMethods.ABEdge.ABE_LEFT,
                DockEdge.Right => ManagedShell.Interop.NativeMethods.ABEdge.ABE_RIGHT,
                _ => ManagedShell.Interop.NativeMethods.ABEdge.ABE_BOTTOM,
            },
            rc = new ManagedShell.Interop.NativeMethods.Rect { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom },
        });
    }

    public void Dispose()
    {
        _launcherPoller.Stop();
        _launcherPoller.Tick -= _launcherTickHandler;
        Taskbar.Dispose();
        VisibilityWatcher.Dispose();
        RunningApps.Dispose();
        try
        {
            Manager.AppBarManager.SignalGracefulShutdown();
            Manager.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to shut down ManagedShell");
        }
        _appVisibility.Dispose();
    }

    /// <summary>Redirects ManagedShell warning/error logs to application log.</summary>
    private sealed class LogBridge : ILog
    {
        public void Log(object sender, LogEventArgs e)
            => Core.Log.Write("SHELL", e.Exception is null ? e.Message : $"{e.Message}: {e.Exception.Message}");
    }
}
