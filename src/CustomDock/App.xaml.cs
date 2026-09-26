using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;
using CustomDock.Settings;
using CustomDock.Shell;
using Microsoft.Win32;

namespace CustomDock;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private bool _running;
    private ShellHost? _shell;
    private DockWindow? _dock;
    /// <summary>Docks on the other displays (when "Show on all displays" is on), keyed by device name.</summary>
    private readonly Dictionary<string, DockWindow> _secondaryDocks = new(StringComparer.OrdinalIgnoreCase);
    private TrayIconManager? _tray;
    private SettingsWindow? _settings;
    private AppPickerWindow? _appPicker;
    private bool _cleanedUp;
    private static bool _taskbarTouched;
    private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _trayIconsChangedHandler;

    public static App Instance => (App)Current;
    public ShellHost? Shell => _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Date and number formats in XAML bindings should follow system culture (defaults to en-US).
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        // WPF animations are capped at 60 FPS by default; keep smooth on high refresh rate displays.
        System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(
            typeof(System.Windows.Media.Animation.Timeline),
            new FrameworkPropertyMetadata { DefaultValue = Math.Clamp(Native.NativeMethods.GetRefreshRate(), 60, 240) });

        base.OnStartup(e);
        AppPaths.EnsureCreated();

        // Emergency recovery: "DockHub.exe --restore-taskbar"
        if (e.Args.Any(a => a.Equals("--restore-taskbar", StringComparison.OrdinalIgnoreCase)))
        {
            TaskbarController.ForceShow();
            Shutdown();
            return;
        }

        // "DockHub.exe --pin <file>": Explorer right-click pin integration
        if (PinArgumentPath(e.Args) is { } pinPath)
            ExplorerPinMenu.Enqueue(pinPath);

        _singleInstance = new SingleInstance();
        bool exitRequested = e.Args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase));
        if (exitRequested)
        {
            // "DockHub.exe --exit": cleanly closes the running instance.
            if (!_singleInstance.TryAcquire())
                SingleInstance.SignalExit();
            Shutdown();
            return;
        }

        if (!_singleInstance.TryAcquire())
        {
            // Second instance triggered by toast click closes silently; otherwise show settings of running instance.
            if (PinArgumentPath(e.Args) is not null)
                SingleInstance.SignalPin();
            else if (!e.Args.Any(a => a.Contains("ToastActivated", StringComparison.OrdinalIgnoreCase)))
                SingleInstance.SignalExisting();
            Shutdown();
            return;
        }

        _running = true;
        RegisterCrashHandlers();

        // If the previous session crashed while taskbar was hidden, restore it before ManagedShell starts.
        TaskbarController.RecoverFromPreviousSession();

        System.Windows.Forms.Application.EnableVisualStyles();

        AppServices.ConfigService.Load();
        var config = AppServices.Config;
        if (config.DebugLogging) Log.DebugEnabled = true;
        L.Initialize(config.Language);
        ApplyMotionLevel();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Accessibility) ApplyMotionLevel();
        };
        ThemeManager.Apply(config.Theme);

        if (config.StartWithWindows != StartupManager.IsEnabled())
            StartupManager.Set(config.StartWithWindows); // Also updates path if exe was moved
        ExplorerPinMenu.Set(config.ExplorerPinMenu);

        StartShell();

        _tray = new TrayIconManager();
        AppServices.Notifications.Fallback = (title, body) => _tray?.ShowBalloon(title, body);
        AppServices.Notifications.Initialize();
        if (AppServices.ConfigService.RecoveredFromBackup is { } backupDate)
            AppServices.Notifications.Show("Settings restored from backup", $"config.json was damaged, so DockHub loaded your backup from {backupDate:g}.");
        AppServices.Reminders.Start();
        ItemDataStore.PurgeOld();
        AppServices.Updates.UpdateAvailable += release => AppServices.Notifications.Show(
            $"DockHub {release.Version} is available", "Open DockHub settings to see what's new and install it.", "update",
            new ToastAction("Details", NotificationService.ActionOpenUpdate),
            new ToastAction("Skip this version", NotificationService.ActionSkipUpdate));
        AppServices.Updates.Start();

        CreateDock();
        ApplyTaskbarMode();
        StartKeyboardShortcuts();
        UndoToast.Attach(AppServices.ConfigService.History);

        config.PropertyChanged += OnConfigChanged;
        WidgetItemView.SettingsRequested += item => ShowSettings("items", item.Id);

        _singleInstance.Listen(Dispatcher, () => ShowSettings(), ExitApplication, ProcessPinRequests);
        ProcessPinRequests();
        SystemEvents.SessionEnding += (_, _) => Cleanup();

        if (config.IsFirstRun && !config.WelcomeShown)
            ShowWelcome();

        Log.Info($"DockHub started (v{typeof(App).Assembly.GetName().Version}, mode: {config.TaskbarMode}).");

        if (Log.DebugEnabled)
        {
            // Once windows and tray icons have settled, record how DockHub sees them.
            var diagnosticsTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            diagnosticsTimer.Tick += (_, _) =>
            {
                diagnosticsTimer.Stop();
                if (_shell is not null) Log.Debug("Startup diagnostics:" + Environment.NewLine + WindowDiagnostics.Dump(_shell, AppServices.Config));
            };
            diagnosticsTimer.Start();
        }
    }

    private void StartShell()
    {
        var config = AppServices.Config;
        _shell = new ShellHost(config.TaskbarMode == TaskbarMode.Replace, config.PinnedTrayIcons);
        AppServices.Shell = _shell;
        _taskbarTouched = true;

        if (_shell.Tray is { } tray)
        {
            if (config.PinnedTrayIcons is null)
                TrayPreferences.ImportWindowsPromotedIcons(tray);
            _trayIconsChangedHandler = (_, _) => TrayPreferences.ApplyNewIcons(tray);
            tray.TrayIcons.CollectionChanged += _trayIconsChangedHandler;
        }
    }

    private void RegisterCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true; // Prevent single widget error from taking down the entire dock.
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log.Error(ex, "Fatal exception");
            SafeRestoreTaskbar();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeRestoreTaskbar();

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    /// <summary>Ensures the user is never left without a taskbar: called on all exit paths.</summary>
    private static void SafeRestoreTaskbar()
    {
        if (!_taskbarTouched) return;
        try
        {
            AppServices.Shell?.Taskbar.Restore();
        }
        catch
        {
            // Last resort: will be restored via session.json on next launch.
        }
    }

    private void CreateDock()
    {
        _dock = new DockWindow(AppServices.Config, _shell!);
        _dock.Start();
        SyncSecondaryDocks();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
        {
            if (_cleanedUp) return;
            SyncSecondaryDocks();
            AppServices.Profiles.ApplyDisplayRule(Native.MonitorHelper.GetAll().Count());
        });

    /// <summary>Opens a dock on every display other than the main one, or closes them all.</summary>
    private void SyncSecondaryDocks()
    {
        if (_shell is null || _dock is null) return;
        var config = AppServices.Config;
        string mainDevice = Native.MonitorHelper.GetPreferred(config.MonitorDevice).DeviceName;
        var wanted = config.ShowOnAllDisplays
            ? Native.MonitorHelper.GetAll()
                .Select(m => m.DeviceName)
                .Where(d => !string.Equals(d, mainDevice, StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool changed = false;
        foreach (var device in _secondaryDocks.Keys.Where(d => !wanted.Contains(d)).ToList())
        {
            _secondaryDocks[device].CloseDock();
            _secondaryDocks.Remove(device);
            changed = true;
        }

        foreach (var device in wanted.Where(d => !_secondaryDocks.ContainsKey(d)))
        {
            try
            {
                var dock = new DockWindow(config, _shell, device);
                _secondaryDocks[device] = dock;
                dock.Start();
                changed = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to create dock on {device}");
            }
        }

        // Running apps are split between displays only while several docks exist.
        if (changed)
            foreach (var dock in DockWindow.All.ToList())
                dock.ApplySettings();
    }

    private HotkeyService? _hotkeys;
    private WinNumberHotkeys? _winNumbers;

    /// <summary>Configurable global shortcuts (null before startup finishes).</summary>
    public HotkeyService? Hotkeys => _hotkeys;

    /// <summary>Actions available to global shortcuts; later features add theirs here.</summary>
    private readonly Dictionary<string, Action> _hotkeyHandlers = new();

    public void RegisterHotkeyHandler(string actionId, Action handler) => _hotkeyHandlers[actionId] = handler;

    public bool HasHotkeyHandler(string actionId) => _hotkeyHandlers.ContainsKey(actionId);

    private void StartKeyboardShortcuts()
    {
        var config = AppServices.Config;
        RegisterHotkeyHandler(HotkeyActions.ToggleDock, DockWindow.ToggleAllDocks);
        RegisterHotkeyHandler(HotkeyActions.FocusDock, DockWindow.FocusMainDock);
        RegisterHotkeyHandler(HotkeyActions.NextProfile, AppServices.Profiles.SwitchToNext);
        RegisterHotkeyHandler(HotkeyActions.ClipboardHistory, Widgets.ClipboardWidget.RequestOpen);
        RegisterHotkeyHandler(HotkeyActions.OpenSettings, () => ShowSettings());
        RegisterHotkeyHandler(HotkeyActions.PinApp, () => ShowAppPicker());
        RegisterHotkeyHandler(HotkeyActions.ToggleAutoHide, () => config.AutoHide = !config.AutoHide);
        RegisterHotkeyHandler(HotkeyActions.ToggleMute, AppServices.Audio.ToggleMute);
        RegisterHotkeyHandler(HotkeyActions.VolumeUp, () => AppServices.Audio.StepVolume(0.05f));
        RegisterHotkeyHandler(HotkeyActions.VolumeDown, () => AppServices.Audio.StepVolume(-0.05f));
        _hotkeys = new HotkeyService(config, _hotkeyHandlers);

        _winNumbers = new WinNumberHotkeys(DockWindow.InvokeAppShortcut, DockWindow.ShowShortcutNumbers);
        UpdateWinNumberHotkeys();
    }

    /// <summary>Win+number is only taken over when DockHub replaces the taskbar.</summary>
    private void UpdateWinNumberHotkeys()
    {
        var config = AppServices.Config;
        if (config.TaskbarMode == TaskbarMode.Replace && config.WinNumberHotkeys) _winNumbers?.Enable();
        else _winNumbers?.Disable();
    }

    private void ApplyTaskbarMode()
    {
        if (_shell is null) return;
        if (AppServices.Config.TaskbarMode == TaskbarMode.Replace)
            _shell.Taskbar.Hide();
        else
            _shell.Taskbar.Restore();
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        var config = AppServices.Config;
        switch (e.PropertyName)
        {
            case nameof(AppConfig.Theme):
                ThemeManager.Apply(config.Theme);
                break;
            case nameof(AppConfig.TaskbarMode):
                if (_revertingTaskbarMode) break;
                if (!ConfirmTaskbarModeRestart(config.TaskbarMode))
                {
                    // Cancelled: put the previous mode back without asking again.
                    _revertingTaskbarMode = true;
                    config.TaskbarMode = config.TaskbarMode == TaskbarMode.Replace ? TaskbarMode.ShowBoth : TaskbarMode.Replace;
                    _revertingTaskbarMode = false;
                    break;
                }
                // System tray is only taken over when replacing taskbar; reinitialize shell services.
                AppServices.ConfigService.SaveNow();
                RestartApplication();
                break;
            case nameof(AppConfig.StartWithWindows):
                StartupManager.Set(config.StartWithWindows);
                break;
            case nameof(AppConfig.ExplorerPinMenu):
                ExplorerPinMenu.Set(config.ExplorerPinMenu);
                break;
            case nameof(AppConfig.ShowVolumeIcon):
            case nameof(AppConfig.ShowNetworkIcon):
            case nameof(AppConfig.ShowBatteryIcon):
                TrayIconView.NotifySystemIconsChanged();
                foreach (var dock in DockWindow.All.ToList()) dock.ApplySettings();
                break;
            case nameof(AppConfig.Motion):
                ApplyMotionLevel();
                break;
            case nameof(AppConfig.Language):
                if (ConfirmDialog.Show(L.T("Restart DockHub?"), L.T("The new language is used after a restart."), "", _settings,
                        new DialogButton("later", L.T("Later"), IsCancel: true),
                        new DialogButton("restart", L.T("Restart now"), DialogButtonKind.Primary)) == "restart")
                {
                    AppServices.ConfigService.SaveNow();
                    RestartApplication();
                }
                break;
            case nameof(AppConfig.CheckForUpdates):
                if (config.CheckForUpdates) AppServices.Updates.Start(); else AppServices.Updates.Stop();
                break;
            case nameof(AppConfig.IncludePrereleases):
                break;
            case nameof(AppConfig.WinNumberHotkeys):
                UpdateWinNumberHotkeys();
                break;
            case nameof(AppConfig.Hotkeys):
                break;
            case nameof(AppConfig.PinnedTrayIcons):
                break;
            case nameof(AppConfig.ShowOnAllDisplays):
            case nameof(AppConfig.MonitorDevice):
                // Close a secondary dock on the new main display before the main dock moves there.
                SyncSecondaryDocks();
                foreach (var dock in DockWindow.All.ToList()) dock.ApplySettings();
                break;
            default:
                foreach (var dock in DockWindow.All.ToList()) dock.ApplySettings();
                break;
        }
    }

    /// <summary>Maps the animation setting (or Windows' "Animation effects") to the level all animations use.</summary>
    private static void ApplyMotionLevel()
    {
        Controls.Motion.Level = AppServices.Config.Motion switch
        {
            MotionPreference.Full => Controls.MotionLevel.Full,
            MotionPreference.Reduced => Controls.MotionLevel.Reduced,
            MotionPreference.Off => Controls.MotionLevel.Off,
            _ => SystemParameters.ClientAreaAnimation ? Controls.MotionLevel.Full : Controls.MotionLevel.Reduced,
        };
    }

    private bool _revertingTaskbarMode;

    /// <summary>Switching the taskbar mode restarts DockHub; make sure that's what the user wants.</summary>
    private bool ConfirmTaskbarModeRestart(TaskbarMode newMode)
    {
        string message = newMode == TaskbarMode.Replace
            ? "DockHub will restart and hide the Windows taskbar. It comes back whenever DockHub exits."
            : "DockHub will restart and show the Windows taskbar next to the dock.";
        return ConfirmDialog.Show("Restart DockHub?", message, "", _settings,
            new DialogButton("cancel", "Cancel", IsCancel: true),
            new DialogButton("restart", "Restart", DialogButtonKind.Primary)) == "restart";
    }

    private static string? PinArgumentPath(string[] args)
    {
        int index = Array.FindIndex(args, a => a.Equals(ExplorerPinMenu.PinArgument, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && !string.IsNullOrWhiteSpace(args[index + 1]) ? args[index + 1] : null;
    }

    /// <summary>Appends "pin" requests from Explorer to the end of pinned applications.</summary>
    private void ProcessPinRequests()
    {
        var requests = ExplorerPinMenu.Dequeue();
        if (requests.Count == 0) return;

        var service = AppServices.ConfigService;
        var pinnedKeys = AppServices.Config.Items
            .Where(i => i.Kind == DockItemKind.App)
            .Select(AppKeys.ForItem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (var path in requests)
        {
            if (!File.Exists(path)) continue;
            var item = DockItem.App(path);
            if (!pinnedKeys.Add(AppKeys.ForItem(item))) continue; // already pinned
            service.AddItem(item, DockItemsIndex.EndOfApps());
            added++;
        }

        _dock?.Reveal();
        if (added == 0)
            _tray?.ShowBalloon(AppInfo.Name, "This application is already pinned.");
        Log.Info($"{added} application(s) pinned from Explorer.");
    }

    public void ShowSettings(string? page = null, string? itemId = null)
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }

        if (page is not null)
            _settings.NavigateTo(page, itemId);

        if (_settings.WindowState == WindowState.Minimized)
            _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    /// <param name="targetGroupId">Folder to add the picked apps to; null pins them to the dock.</param>
    public void ShowWelcome() => new WelcomeWindow().Show();

    public void ShowAppPicker(string? targetGroupId = null)
    {
        if (_appPicker is null)
        {
            _appPicker = new AppPickerWindow();
            _appPicker.Closed += (_, _) => _appPicker = null;
            _appPicker.Show();
        }
        _appPicker.SetTarget(targetGroupId);
        _appPicker.Activate();
    }

    public void RevealDock() => _dock?.Reveal();

    public void RestartApplication()
    {
        Log.Info("Restarting application.");
        Cleanup();
        _singleInstance?.Dispose();
        _singleInstance = null;
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restart");
        }
        Shutdown();
    }

    public void ExitApplication()
    {
        Cleanup();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    private void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;
        if (!_running)
        {
            // Second instance / --exit / --restore-taskbar: nothing to clean up.
            _singleInstance?.Dispose();
            return;
        }

        SafeRestoreTaskbar();
        try
        {
            _settings?.Close();
            _appPicker?.Close();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            foreach (var dock in _secondaryDocks.Values) dock.CloseDock();
            _secondaryDocks.Clear();
            _dock?.CloseDock();
            AppServices.ConfigService.SaveNow();
            if (_shell?.Tray is { } shellTray && _trayIconsChangedHandler is not null)
            {
                shellTray.TrayIcons.CollectionChanged -= _trayIconsChangedHandler;
                _trayIconsChangedHandler = null;
            }
            _shell?.Dispose();
            _winNumbers?.Dispose();
            _hotkeys?.Dispose();
            AppServices.Reminders.Dispose();
            AppServices.Audio.Dispose();
            AppServices.Clock.Dispose();
            NotificationService.Cleanup();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during shutdown");
        }

        _tray?.Dispose();
        _singleInstance?.Dispose();
        Log.Info("DockHub shut down.");
    }
}
