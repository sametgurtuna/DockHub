using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Services;
using CustomDock.Shell;
using CustomDock.Widgets;
using ManagedShell.AppBar;
using Microsoft.Win32;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

/// <summary>
/// Dock that replaces the Windows taskbar.
/// Layout: [Start · Search · Task view] [items + running apps (scrollable)] [arrows · tray · clock · desktop]
/// </summary>
public partial class DockWindow : Window, IWidgetHost
{
    /// <summary>Base bar thickness in DIP (content 46 DIP + 5 DIP margins).</summary>
    private const double BaseThickness = 56;
    private const int TriggerThickness = 2;
    private static readonly TimeSpan ShowDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(200);

    private static DockWindow? s_current;

    private readonly AppConfig _config;
    private readonly ShellHost _shell;
    private readonly Dictionary<string, FrameworkElement> _itemViews = new();
    private readonly Dictionary<string, AppButton> _runningViews = new();
    private readonly Dictionary<string, string> _itemKeys = new();
    private readonly SeparatorView _runningSeparator = new(false);
    private readonly HashSet<ContextMenu> _openMenus = new();
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _revealTimer;
    private readonly DispatcherTimer _topmostTimer;
    private readonly uint _processId = (uint)Environment.ProcessId;
    private readonly NotifyCollectionChangedEventHandler? _unpinnedIconsChangedHandler;

    private IntPtr _hwnd;
    private SpaceReserver? _reserver;
    private (string Device, DockEdge Edge, double Thickness)? _reserverKey;
    private EdgeTriggerWindow? _trigger;
    private MonitorInfo _monitor;
    private RECT _shownRect;

    private const int HOTKEY_DOCK_TOGGLE = 0xD0C4;
    private bool _hotkeyRegistered;
    private int _interactionCount;
    private bool _revealed = true;
    private bool _shown;
    private bool _fullscreen;
    private bool _inputMode;
    private bool _closing;
    private bool _repositionQueued;
    private bool _animating;
    private int _animationVersion;
    private double _scrollTarget = double.NaN;
    private bool _scrollAnimating;
    private TimeSpan _lastScrollFrame;
    private DockMagnifier? _magnifier;

    static DockWindow()
    {
        // Opened/Closed always arrive in pairs: auto-hide pauses while dock context menus are open.
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
            new RoutedEventHandler((s, _) => s_current?.OnContextMenuStateChanged((ContextMenu)s, open: true)));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent,
            new RoutedEventHandler((s, _) => s_current?.OnContextMenuStateChanged((ContextMenu)s, open: false)));
    }

    public DockWindow(AppConfig config, ShellHost shell)
    {
        s_current = this;
        _config = config;
        _shell = shell;
        _monitor = MonitorHelper.GetPreferred(config.MonitorDevice);
        InitializeComponent();

        AllowDrop = true;
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); TryAutoHide(); };
        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _revealTimer.Tick += (_, _) => { _revealTimer.Stop(); Reveal(); };
        _topmostTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => ReassertTopmost();

        DragEnter += (_, _) => { _hideTimer.Stop(); BeginInteraction(); };
        DragLeave += (_, _) => { EndInteraction(); ScheduleAutoHide(); };
        Drop += (_, _) => { EndInteraction(); ScheduleAutoHide(); };

        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => QueueReposition();
        ItemsPanel.SizeChanged += (_, _) => { if (_config.WidthMode == DockWidthMode.Fit) QueueReposition(); };
        MouseEnter += (_, _) => _hideTimer.Stop();
        MouseLeave += (_, _) => ScheduleAutoHide();
        Deactivated += OnDeactivated;
        PreviewMouseWheel += OnPreviewMouseWheel;

        AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPreviewMouseLeftButtonDownAnywhere), handledEventsToo: true);
        AddHandler(ContextMenuOpeningEvent, new ContextMenuEventHandler(OnAnyContextMenuOpening), handledEventsToo: true);
        Root.ContextMenu = new ContextMenu();
        Root.ContextMenuOpening += OnDockMenuOpening;
        StartButton.ContextMenuOpening += (_, e) => e.Handled = true;
        ClockButton.ContextMenu = new ContextMenu();
        ClockButton.ContextMenuOpening += OnClockMenuOpening;

        ThemeManager.ThemeChanged += OnThemeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _config.ItemsChanged += OnItemsChanged;
        _shell.RunningApps.GroupsChanged += RefreshRunningApps;
        _shell.Manager.FullScreenHelper.FullScreenApps.CollectionChanged += OnFullScreenAppsChanged;
        _shell.LauncherVisibilityChanged += OnLauncherVisibilityChanged;
        TrayIconView.Interacting += UpdateTrayHost;
        DockDragHelper.DraggingChanged += OnDraggingChanged;

        if (_shell.Tray is { } tray)
        {
            PinnedTray.ItemsSource = tray.PinnedIcons;
            OverflowTray.ItemsSource = tray.UnpinnedIcons;
            _unpinnedIconsChangedHandler = (_, _) => UpdateTrayVisibility();
            ((INotifyCollectionChanged)tray.UnpinnedIcons).CollectionChanged += _unpinnedIconsChangedHandler;
        }

        // Wave effect where items near the cursor scale up slightly, similar to macOS Dock.
        _magnifier = new DockMagnifier(ItemsPanel, () => IsVertical);
    }

    // ------------------------------------------------------------------ IWidgetHost

    public DockEdge Edge => _config.Edge;

    public bool IsVertical => _config.Edge is DockEdge.Left or DockEdge.Right;

    Window IWidgetHost.Window => this;

    public bool IsPreview => false;

    public void BeginInteraction()
    {
        _interactionCount++;
        _hideTimer.Stop();
    }

    public void EndInteraction()
    {
        _interactionCount = Math.Max(0, _interactionCount - 1);
        ScheduleAutoHide();
    }

    /// <summary>Temporarily makes the dock activatable for text input.</summary>
    public void ActivateForInput()
    {
        if (_hwnd == IntPtr.Zero) return;
        _inputMode = true;
        WindowEffects.SetNoActivate(_hwnd, false);
        Activate();
        SetForegroundWindow(_hwnd);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_inputMode)
        {
            _inputMode = false;
            WindowEffects.SetNoActivate(_hwnd, true);
        }
        ScheduleAutoHide();
    }

    // ------------------------------------------------------------------ Initialization / settings

    public void Start()
    {
        new WindowInteropHelper(this).EnsureHandle();
        ApplySettings();
        _topmostTimer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
        WindowEffects.MakeToolWindow(_hwnd, noActivate: true);
        WindowEffects.ExtendGlass(_hwnd);
        ApplyBackdrop();
        RegisterGlobalHotkey();
    }

    private void RegisterGlobalHotkey()
    {
        if (_hwnd == IntPtr.Zero) return;
        // Win + Alt + D
        bool success = RegisterHotKey(_hwnd, HOTKEY_DOCK_TOGGLE, MOD_WIN | MOD_ALT | MOD_NOREPEAT, 0x44 /* D */);
        if (!success)
        {
            // Fallback: Ctrl + Alt + D
            success = RegisterHotKey(_hwnd, HOTKEY_DOCK_TOGGLE, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x44 /* D */);
            if (success)
                Log.Info("DockHub shortcut registered: Ctrl+Alt+D");
            else
                Log.Warn("Failed to register DockHub global shortcut.");
        }
        else
        {
            Log.Info("DockHub shortcut registered: Win+Alt+D");
        }
        _hotkeyRegistered = success;
    }

    public void ToggleDock()
    {
        if (_closing) return;
        if (_config.AutoHide)
        {
            if (_shown && _revealed)
            {
                _revealed = false;
                UpdateVisibility(animate: true);
            }
            else
            {
                Reveal();
            }
        }
        else
        {
            ReassertTopmost();
            if (_hwnd != IntPtr.Zero)
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEHWHEEL = 0x020E;
        if (msg == WM_MOUSEHWHEEL && !IsVertical && ScrollableLength > 0)
        {
            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            SmoothScrollBy(delta * 0.9);
            handled = true;
            return new IntPtr(1);
        }
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_DOCK_TOGGLE)
        {
            ToggleDock();
            handled = true;
            return IntPtr.Zero;
        }
        if (msg == WM_DPICHANGED)
        {
            _monitor = MonitorHelper.GetPreferred(_config.MonitorDevice);
            UpdateReserver();
            QueueReposition();
            handled = true;
            return IntPtr.Zero;
        }
        if ((msg == WM_SETTINGCHANGE && wParam.ToInt32() == SPI_SETWORKAREA) || msg == WM_DISPLAYCHANGE)
            QueueReposition();
        return IntPtr.Zero;
    }

    /// <summary>Applies all dock settings from configuration.</summary>
    public void ApplySettings()
    {
        if (_hwnd == IntPtr.Zero || _closing) return;

        _monitor = MonitorHelper.GetPreferred(_config.MonitorDevice);
        ContentScale.ScaleX = ContentScale.ScaleY = Scale;
        ApplyOrientation();
        ApplyZoneVisibility();
        ApplyBackdrop();
        RebuildItems();
        UpdateClock(DateTime.Now);
        SubscribeClock();
        UpdateReserver();

        if (_config.AutoHide) ScheduleAutoHide();
        else _revealed = true;

        UpdateVisibility(animate: false);
        UpdateTrigger();
        QueueReposition();
    }

    // ------------------------------------------------------------------ Clock

    private void SubscribeClock()
    {
        AppServices.Clock.SecondTick -= OnClockTick;
        AppServices.Clock.MinuteTick -= OnClockTick;
        if (!_config.ShowClock) return;
        if (_config.ClockShowSeconds) AppServices.Clock.SecondTick += OnClockTick;
        else AppServices.Clock.MinuteTick += OnClockTick;
    }

    private void OnClockTick(object? sender, DateTime now) => UpdateClock(now);

    private void UpdateClock(DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        ClockTime.Text = now.ToString(_config.ClockShowSeconds ? "HH:mm:ss" : "HH:mm", culture);
        ClockDate.Text = now.ToString(culture.DateTimeFormat.ShortDatePattern, culture);
        ClockButton.ToolTip = now.ToString("D", culture);
    }

    // ------------------------------------------------------------------ Buttons

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        UpdateTrayHost();
        _shell.ShowStartMenu(_hwnd);
    }

    private void OnStartRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        UpdateTrayHost();
        _shell.ShowStartContextMenu();
    }

    private void OnSearchClick(object sender, RoutedEventArgs e)
    {
        UpdateTrayHost();
        _shell.ShowSearch();
    }

    private void OnTaskViewClick(object sender, RoutedEventArgs e) => _shell.ShowTaskView();

    private void OnClockClick(object sender, RoutedEventArgs e)
    {
        UpdateTrayHost();
        _shell.ShowNotificationCenter();
    }

    private void OnShowDesktopClick(object sender, RoutedEventArgs e) => _shell.ToggleDesktop();

    private void OnClockMenuOpening(object sender, ContextMenuEventArgs e)
    {
        UpdateTrayHost();
        var menu = ClockButton.ContextMenu;
        menu.Items.Clear();
        menu.Items.Add(DockMenu.Item("Notification center", "\uE91C", _shell.ShowNotificationCenter));
        menu.Items.Add(DockMenu.Item("Quick settings", "\uE9E9", _shell.ShowQuickSettings));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("Adjust date and time", "\uE787",
            () => Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true })));
        menu.Items.Add(DockMenu.Check("Show seconds", _config.ClockShowSeconds, () => _config.ClockShowSeconds = !_config.ClockShowSeconds));
        menu.Items.Add(DockMenu.Check("Show date", _config.ClockShowDate, () => _config.ClockShowDate = !_config.ClockShowDate));
    }

    private void OnTrayOverflowChecked(object sender, RoutedEventArgs e)
    {
        UpdateTrayHost();
        (TrayOverflowPopup.Placement, TrayOverflowPopup.HorizontalOffset, TrayOverflowPopup.VerticalOffset) = _config.Edge switch
        {
            DockEdge.Top => (PlacementMode.Bottom, -60.0, 10.0),
            DockEdge.Left => (PlacementMode.Right, 10.0, 0.0),
            DockEdge.Right => (PlacementMode.Left, -10.0, 0.0),
            _ => (PlacementMode.Top, -60.0, -10.0),
        };
        BeginInteraction();
        GlobalPopupDismissHook.RegisterPopup(TrayOverflowPopup);
        PopupAnimationHelper.AnimateOpen(TrayOverflowPopup, _config.Edge);
    }

    private void OnTrayOverflowUnchecked(object sender, RoutedEventArgs e)
    {
        if (TrayOverflowPopup.IsOpen && !PopupAnimationHelper.IsClosing(TrayOverflowPopup))
        {
            PopupAnimationHelper.ClosePopup(TrayOverflowPopup, _config.Edge);
        }
    }

    private void OnTrayOverflowClosed(object? sender, EventArgs e)
    {
        TrayOverflowButton.IsChecked = false;
        EndInteraction();
    }

    // ------------------------------------------------------------------ Dock menu

    private void OnDockMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Root has its own ContextMenu; when right-clicking a child element without a ContextMenu (like a tray icon),
        // default behavior raises this event on the nearest owner (Root). We must check if original source is a tray icon.
        if (FindAncestor<TrayIconView>(e.OriginalSource as DependencyObject) is not null)
        {
            e.Handled = true;
            return;
        }
        var menu = Root.ContextMenu;
        menu.Items.Clear();
        menu.Items.Add(DockMenu.Item("Add widget…", "\uE710", () => App.Instance.ShowSettings("gallery")));
        menu.Items.Add(DockMenu.Item("Pin application…", "\uE718", () => App.Instance.ShowAppPicker()));
        menu.Items.Add(DockMenu.Item("Add separator", "\uE76F", () => AppServices.ConfigService.AddItem(DockItem.Separator())));
        menu.Items.Add(DockMenu.Item("Create group", "\uE8B7", () => AppServices.ConfigService.AddItem(DockItem.Group("New group"))));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("Task Manager", "\uE9D9", () => Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true })));
        menu.Items.Add(DockMenu.Item("Windows Settings", "", () => Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true })));
        menu.Items.Add(DockMenu.Item("Quick settings", "\uE9E9", () => { UpdateTrayHost(); _shell.ShowQuickSettings(); }));
        menu.Items.Add(DockMenu.Check("Auto-hide", _config.AutoHide, () => _config.AutoHide = !_config.AutoHide));
        menu.Items.Add(DockMenu.Check("Hide Windows taskbar", _config.TaskbarMode == TaskbarMode.Replace,
            () => _config.TaskbarMode = _config.TaskbarMode == TaskbarMode.Replace ? TaskbarMode.ShowBoth : TaskbarMode.Replace));
        menu.Items.Add(DockMenu.Submenu("Position", "\uE8A0", new[]
        {
            DockMenu.Check("Bottom", _config.Edge == DockEdge.Bottom, () => _config.Edge = DockEdge.Bottom),
            DockMenu.Check("Top", _config.Edge == DockEdge.Top, () => _config.Edge = DockEdge.Top),
            DockMenu.Check("Left", _config.Edge == DockEdge.Left, () => _config.Edge = DockEdge.Left),
            DockMenu.Check("Right", _config.Edge == DockEdge.Right, () => _config.Edge = DockEdge.Right),
        }));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("DockHub settings…", "\uE713", () => App.Instance.ShowSettings()));
        menu.Items.Add(DockMenu.Item("Exit", "\uE7E8", () => App.Instance.ExitApplication()));
    }

    // ------------------------------------------------------------------ Shutdown

    public void CloseDock()
    {
        if (_closing) return;
        _closing = true;
        _animationVersion++;
        if (s_current == this) s_current = null;
        _hideTimer.Stop();
        _revealTimer.Stop();
        _topmostTimer.Stop();
        if (_scrollAnimating) CompositionTarget.Rendering -= OnScrollFrame;

        if (_hotkeyRegistered && _hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_DOCK_TOGGLE);
            _hotkeyRegistered = false;
        }

        foreach (var view in _itemViews.Values) DisposeView(view);
        foreach (var view in _runningViews.Values) view.Detach();
        _itemViews.Clear();
        _runningViews.Clear();

        AppServices.Clock.SecondTick -= OnClockTick;
        AppServices.Clock.MinuteTick -= OnClockTick;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _config.ItemsChanged -= OnItemsChanged;
        _shell.RunningApps.GroupsChanged -= RefreshRunningApps;
        _shell.Manager.FullScreenHelper.FullScreenApps.CollectionChanged -= OnFullScreenAppsChanged;
        _shell.LauncherVisibilityChanged -= OnLauncherVisibilityChanged;
        TrayIconView.Interacting -= UpdateTrayHost;
        DockDragHelper.DraggingChanged -= OnDraggingChanged;

        if (_shell.Tray is { } tray && _unpinnedIconsChangedHandler is not null)
        {
            ((INotifyCollectionChanged)tray.UnpinnedIcons).CollectionChanged -= _unpinnedIconsChangedHandler;
        }
        PinnedTray.ItemsSource = null;
        OverflowTray.ItemsSource = null;

        if (_reserver is not null)
        {
            _reserver.RectChanged -= QueueReposition;
            _reserver.CloseReserver();
            _reserver = null;
        }
        _trigger?.Close();
        _magnifier?.Dispose();
        try { WindowPreviewWindow.Instance.HidePreview(); } catch { }
        Close();
    }
}
