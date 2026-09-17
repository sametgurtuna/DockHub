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
/// Kabuk servislerini (ManagedShell) barındırır: çalışan pencereler, sistem tepsisi, tam ekran algılama,
/// Explorer görev çubuğunu gizleme ve Başlat/Arama/Bildirim merkezi gibi sistem arayüzlerini açma.
/// </summary>
public sealed class ShellHost : IDisposable
{
    private readonly AppVisibilityHelper _appVisibility;
    private readonly DispatcherTimer _launcherPoller;
    private bool _launcherVisible;

    public ShellHost(bool replaceTaskbar, IEnumerable<string>? pinnedTrayIcons)
    {
        IsReplacingTaskbar = replaceTaskbar;
        EnvironmentHelper.IsAppRunningAsShell = GetShellWindow() == IntPtr.Zero;
        ShellLogger.Severity = LogSeverity.Warning;
        ShellLogger.Attach(new LogBridge());

        // TasksService, Win tuşu mesajlarını (SC_TASKLIST) kendi penceresine yönlendirir.
        // Explorer'ın Başlat menüsünü açabilmesi için eski "taskman" penceresini geri vereceğiz.
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
            Log.Info("Taskman penceresi Explorer'a geri verildi.");
        }

        _appVisibility = new AppVisibilityHelper(false);
        Taskbar = new TaskbarController(() => Manager.NotificationArea?.Handle ?? IntPtr.Zero, () => _launcherVisible);
        RunningApps = new RunningAppsService(Manager);

        _launcherPoller = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _launcherPoller.Tick += (_, _) => PollLauncher();
        _launcherPoller.Start();
    }

    public ShellManager Manager { get; }

    public bool IsReplacingTaskbar { get; }

    public TaskbarController Taskbar { get; }

    public RunningAppsService RunningApps { get; }

    public NotificationArea? Tray => Manager.NotificationArea;

    /// <summary>Başlat menüsü veya Arama açık mı?</summary>
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

    // ------------------------------------------------------------------ Sistem arayüzleri

    public void ShowStartMenu(IntPtr anchorWindow) => StartMenuLauncher.Show(anchorWindow);

    public void ShowStartContextMenu() => InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_X);

    public void ShowSearch() => InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_S);

    public void ShowTaskView() => InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_TAB);

    /// <summary>Bildirim merkezi + takvim (saat tıklaması).</summary>
    public void ShowNotificationCenter()
    {
        try
        {
            ImmersiveShellHelper.ShowActionCenter();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bildirim merkezi açılamadı");
            InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_N);
        }
    }

    /// <summary>Hızlı ayarlar (ağ, ses, pil).</summary>
    public void ShowQuickSettings()
    {
        try
        {
            ImmersiveShellHelper.ShowControlCenter();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Hızlı ayarlar açılamadı");
            InputHelper.SendKeyCombo(InputHelper.VK_LWIN, InputHelper.VK_A);
        }
    }

    public void ToggleDesktop() => Taskbar.ToggleDesktop();

    /// <summary>
    /// Başlat menüsü, hızlı ayarlar ve bildirimler bu dikdörtgeni "görev çubuğu" kabul ederek konumlanır.
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
        Taskbar.Restore();
        RunningApps.Dispose();
        try
        {
            Manager.AppBarManager.SignalGracefulShutdown();
            Manager.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ManagedShell kapatılamadı");
        }
        _appVisibility.Dispose();
    }

    /// <summary>ManagedShell uyarı/hata günlüklerini uygulama günlüğüne yönlendirir.</summary>
    private sealed class LogBridge : ILog
    {
        public void Log(object sender, LogEventArgs e)
            => Core.Log.Write("SHELL", e.Exception is null ? e.Message : $"{e.Message}: {e.Exception.Message}");
    }
}
