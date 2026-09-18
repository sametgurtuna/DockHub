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
using Forms = System.Windows.Forms;

namespace CustomDock.Dock;

/// <summary>
/// Windows görev çubuğunun yerini alan dock.
/// Yerleşim: [Başlat · Ara · Görev görünümü] [öğeler + çalışan uygulamalar (kaydırılabilir)] [oklar · tepsi · saat · masaüstü]
/// </summary>
public partial class DockWindow : Window, IWidgetHost
{
    /// <summary>Orta boyuttaki çubuk kalınlığı (DIP). İçerik 46 DIP + 5 DIP kenar boşluğu.</summary>
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

    private IntPtr _hwnd;
    private SpaceReserver? _reserver;
    private (string Device, DockEdge Edge, double Thickness)? _reserverKey;
    private EdgeTriggerWindow? _trigger;
    private MonitorInfo _monitor;
    private RECT _shownRect;

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
        // Opened/Closed her zaman çift gelir: dock içindeki bağlam menüleri açıkken otomatik gizleme durur.
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

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); TryAutoHide(); };
        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _revealTimer.Tick += (_, _) => { _revealTimer.Stop(); Reveal(); };
        _topmostTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => ReassertTopmost();

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
            ((INotifyCollectionChanged)tray.UnpinnedIcons).CollectionChanged += (_, _) => UpdateTrayVisibility();
        }

        // macOS Dock'undaki gibi imlece yakın öğelerin hafifçe büyüdüğü "dalga" efekti.
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

    /// <summary>Metin girişi için dock'u geçici olarak etkinleştirilebilir yapar.</summary>
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

    /// <summary>Tüm bağlam menüleri (öğelerin kendi doldurma işleyicilerinden sonra) dock'un dışına yerleşir.</summary>
    private void OnAnyContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Tepsi simgesinin kendi ContextMenu'sü yok; bu yüzden varsayılan davranış olay ağacında yukarı
        // doğru ilk ContextMenu'ye sahip öğeyi (Root, yani dock menüsü) bulup onu açardı. Tepsi simgesine
        // sağ tıklanınca (gerçek sağ tık uygulamanın kendi işlemine iletilir) dock menüsünün açılmasını engelle.
        if (FindAncestor<TrayIconView>(e.OriginalSource as DependencyObject) is not null)
        {
            e.Handled = true;
            return;
        }
        if (e.Handled) return;
        if (PopupPlacement.FindMenuOwner(e.OriginalSource as DependencyObject) is { ContextMenu: { } menu } owner)
            PopupPlacement.PlaceMenu(menu, owner, _config.Edge);
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var d = source; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d)
                 : LogicalTreeHelper.GetParent(d))
        {
            if (d is T match) return match;
        }
        return null;
    }

    private void OnPreviewMouseLeftButtonDownAnywhere(object sender, MouseButtonEventArgs e)
    {
        if (_openMenus.Count > 0)
        {
            var menus = _openMenus.ToList();
            foreach (var menu in menus)
                menu.IsOpen = false;
        }
    }

    private void OnContextMenuStateChanged(ContextMenu menu, bool open)
    {
        if (open)
        {
            _openMenus.Add(menu);
            GlobalPopupDismissHook.RegisterMenu(menu);
            BeginInteraction();
        }
        else if (_openMenus.Remove(menu))
        {
            GlobalPopupDismissHook.UnregisterMenu(menu);
            EndInteraction();
        }
    }

    private void OnDraggingChanged(bool dragging)
    {
        if (dragging) BeginInteraction();
        else EndInteraction();
    }

    // ------------------------------------------------------------------ Başlatma / ayarlar

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
        if ((msg == WM_SETTINGCHANGE && wParam.ToInt32() == SPI_SETWORKAREA) || msg == WM_DISPLAYCHANGE)
            QueueReposition();
        return IntPtr.Zero;
    }

    private double Scale => _config.Size switch
    {
        DockSize.Small => 0.86,
        DockSize.Large => 1.18,
        _ => 1.0,
    };

    private double ThicknessDip => Math.Round(BaseThickness * Scale);

    private double MarginDip => _config.Layout == DockLayout.Floating ? _config.EdgeMargin : 0;

    /// <summary>Konfigürasyondaki tüm dock ayarlarını uygular.</summary>
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

    private void ApplyOrientation()
    {
        bool vertical = IsVertical;
        var orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        Zones.Orientation = orientation;
        StartZone.Orientation = orientation;
        EndZone.Orientation = orientation;
        ItemsPanel.Orientation = orientation;
        Scroller.HorizontalScrollBarVisibility = vertical ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        Scroller.VerticalScrollBarVisibility = vertical ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
        Scroller.PanningMode = vertical ? PanningMode.VerticalOnly : PanningMode.HorizontalOnly;
        StartSeparator.SetOrientation(vertical);
        EndSeparator.SetOrientation(vertical);
        _runningSeparator.SetOrientation(vertical);
        ScrollBackButton.Content = vertical ? "\uE70E" : "\uE76B";
        ScrollForwardButton.Content = vertical ? "\uE70D" : "\uE76C";
        ScrollBackButton.Width = ScrollForwardButton.Width = vertical ? 44 : 26;
        ScrollBackButton.Height = ScrollForwardButton.Height = vertical ? 26 : 46;

        bool center = _config.Alignment == DockAlignment.Center;
        ItemsPanel.HorizontalAlignment = vertical ? HorizontalAlignment.Center : center ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        ItemsPanel.VerticalAlignment = !vertical ? VerticalAlignment.Center : center ? VerticalAlignment.Center : VerticalAlignment.Top;

        ClockDate.Visibility = _config.ClockShowDate && !vertical ? Visibility.Visible : Visibility.Collapsed;
        ClockButton.Width = vertical ? 44 : double.NaN;
        ClockButton.Padding = vertical ? new Thickness(0) : new Thickness(9, 0, 9, 0);
        ClockTime.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Right;

        var showDesktop = ShowDesktopButton;
        showDesktop.Width = vertical ? 46 : 12;
        showDesktop.Height = vertical ? 12 : 46;
        showDesktop.LayoutTransform = vertical ? new RotateTransform(90) : Transform.Identity;

        EdgeHighlight.BorderThickness = _config.Edge switch
        {
            DockEdge.Top => new Thickness(0, 0, 0, 1),
            DockEdge.Left => new Thickness(0, 0, 1, 0),
            DockEdge.Right => new Thickness(1, 0, 0, 0),
            _ => new Thickness(0, 1, 0, 0),
        };
    }

    private void ApplyZoneVisibility()
    {
        StartButton.Visibility = _config.ShowStartButton ? Visibility.Visible : Visibility.Collapsed;
        SearchButton.Visibility = _config.ShowSearchButton ? Visibility.Visible : Visibility.Collapsed;
        TaskViewButton.Visibility = _config.ShowTaskViewButton ? Visibility.Visible : Visibility.Collapsed;
        StartSeparator.Visibility = _config.ShowStartButton || _config.ShowSearchButton || _config.ShowTaskViewButton
            ? Visibility.Visible : Visibility.Collapsed;

        bool tray = _config.ShowTray && _shell.Tray is not null;
        PinnedTray.Visibility = tray ? Visibility.Visible : Visibility.Collapsed;
        ClockButton.Visibility = _config.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        ShowDesktopButton.Visibility = _config.ShowDesktopButton ? Visibility.Visible : Visibility.Collapsed;
        UpdateTrayVisibility();
    }

    private void UpdateTrayVisibility()
    {
        bool tray = _config.ShowTray && _shell.Tray is not null;
        bool hasHidden = tray && _shell.Tray!.UnpinnedIcons is { IsEmpty: false };
        TrayOverflowButton.Visibility = hasHidden ? Visibility.Visible : Visibility.Collapsed;
        EndSeparator.Visibility = tray || _config.ShowClock || ScrollBackButton.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyBackdrop()
    {
        if (_hwnd == IntPtr.Zero) return;
        bool floating = _config.Layout == DockLayout.Floating;
        var tint = (TryFindResource("DockTintBrush") as SolidColorBrush)?.Color ?? Colors.Black;

        if (_config.Backdrop == BackdropKind.Solid)
        {
            TintLayer.SetResourceReference(Border.BackgroundProperty, "DockSolidBrush");
            TintLayer.Opacity = 1;
        }
        else
        {
            TintLayer.SetResourceReference(Border.BackgroundProperty, "DockTintBrush");
            TintLayer.Opacity = _config.TintOpacity;
        }

        WindowEffects.ApplyDockBackdrop(_hwnd, _config.Backdrop, Color.FromArgb((byte)(_config.TintOpacity * 255), tint.R, tint.G, tint.B));
        WindowEffects.SetDarkMode(_hwnd, ThemeManager.IsDark);
        WindowEffects.SetCornerPreference(_hwnd, floating ? 2 : 1);
        WindowEffects.SetBorderColor(_hwnd, floating ? (TryFindResource("DockBorderColor") as Color?) : null);
        EdgeHighlight.Visibility = ThemeManager.IsDark ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnThemeChanged()
    {
        ApplyBackdrop();
        foreach (var view in _itemViews.Values.OfType<WidgetItemView>())
            view.HoverEnabled = _config.HoverEffect;
    }

    // ------------------------------------------------------------------ Öğeler

    private void OnItemsChanged(object? sender, EventArgs e) => RebuildItems();

    private void RebuildItems()
    {
        var items = _config.Items;
        bool vertical = IsVertical;
        var positionsBefore = CapturePositions();

        foreach (var id in _itemViews.Keys.Except(items.Select(i => i.Id)).ToList())
        {
            DisposeView(_itemViews[id]);
            _itemViews.Remove(id);
            _itemKeys.Remove(id);
        }

        ItemsPanel.Children.Clear();
        foreach (var item in items)
        {
            if (!_itemViews.TryGetValue(item.Id, out var view))
            {
                view = CreateView(item);
                if (view is null) continue;
                _itemViews[item.Id] = view;
            }

            switch (view)
            {
                case SeparatorView separator:
                    separator.SetOrientation(vertical);
                    break;
                case WidgetItemView widgetView:
                    widgetView.HoverEnabled = _config.HoverEffect;
                    widgetView.SetCompact(vertical);
                    widgetView.Margin = vertical ? new Thickness(0, 2, 0, 2) : new Thickness(3, 0, 3, 0);
                    widgetView.HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
                    widgetView.Widget.OnLayoutChanged();
                    break;
                case AppButton appButton:
                    appButton.Margin = vertical ? new Thickness(0, 1, 0, 1) : new Thickness(1, 0, 1, 0);
                    break;
            }
            ItemsPanel.Children.Add(view);
        }

        _runningSeparator.SetOrientation(vertical);
        RefreshRunningApps();
        AnimateShifts(positionsBefore);
    }

    /// <summary>Yeniden sıralama/ekleme/kaldırmadan önce mevcut öğelerin ItemsPanel içindeki konumunu kaydeder.</summary>
    private Dictionary<FrameworkElement, Point> CapturePositions()
    {
        var positions = new Dictionary<FrameworkElement, Point>();
        foreach (FrameworkElement element in ItemsPanel.Children)
        {
            try { positions[element] = element.TranslatePoint(new Point(0, 0), ItemsPanel); }
            catch (InvalidOperationException) { /* henüz düzenlenmemiş */ }
        }
        return positions;
    }

    /// <summary>
    /// Konum değiştiren öğeleri (aynı örnek, farklı sıra) eskisinden yenisine doğru kaydırarak taşır;
    /// yeni eklenenler (Motion.Appear ile) zaten kendi giriş animasyonuna sahip, burada dokunulmaz.
    /// </summary>
    private void AnimateShifts(Dictionary<FrameworkElement, Point> before)
    {
        if (before.Count == 0 || _closing) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_closing) return;
            foreach (FrameworkElement element in ItemsPanel.Children)
            {
                if (!before.TryGetValue(element, out var oldPos)) continue;
                Point newPos;
                try { newPos = element.TranslatePoint(new Point(0, 0), ItemsPanel); }
                catch (InvalidOperationException) { continue; }

                double dx = oldPos.X - newPos.X, dy = oldPos.Y - newPos.Y;
                if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) continue;
                Motion.SlideFrom(element, new Vector(dx, dy));
            }
        });
    }

    private FrameworkElement? CreateView(DockItem item)
    {
        try
        {
            switch (item.Kind)
            {
                case DockItemKind.App when !string.IsNullOrWhiteSpace(item.Path):
                    var app = new AppButton(item, null);
                    DockDragHelper.Attach(app, () => new DataObject(DockDragHelper.ItemFormat, item.Id));
                    return app;
                case DockItemKind.Widget when WidgetRegistry.Find(item.Widget) is { } descriptor:
                    var widget = descriptor.Create(item);
                    var view = new WidgetItemView(item, widget, this);
                    view.SetCompact(IsVertical);
                    widget.Attach(this);
                    return view;
                case DockItemKind.Separator:
                    return new SeparatorView(item, IsVertical);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Dock öğesi oluşturulamadı: {item.Kind} {item.Widget ?? item.Path}");
        }
        return null;
    }

    private static void DisposeView(FrameworkElement view)
    {
        switch (view)
        {
            case WidgetItemView widgetView:
                widgetView.Widget.Detach();
                widgetView.Detach();
                break;
            case AppButton app:
                app.Detach();
                break;
        }
    }

    private string KeyFor(DockItem item)
    {
        string cacheKey = item.Id;
        if (!_itemKeys.TryGetValue(cacheKey, out var key))
            _itemKeys[cacheKey] = key = AppKeys.ForItem(item);
        return key;
    }

    /// <summary>Sabitlenmiş düğmelere pencere gruplarını bağlar; sabitlenmemiş çalışan uygulamaları sona ekler.</summary>
    private void RefreshRunningApps()
    {
        if (_closing) return;
        var pinnedKeys = new HashSet<string>();
        foreach (var item in _config.Items.Where(i => i.Kind == DockItemKind.App))
        {
            var key = KeyFor(item);
            pinnedKeys.Add(key);
            if (_itemViews.TryGetValue(item.Id, out var view) && view is AppButton button)
                button.Group = _shell.RunningApps.Find(key);
        }

        var unpinned = _config.ShowRunningApps
            ? _shell.RunningApps.Groups.Where(g => !pinnedKeys.Contains(g.Key)).OrderBy(g => g.Order).ToList()
            : new List<AppGroup>();

        foreach (var key in _runningViews.Keys.Except(unpinned.Select(g => g.Key)).ToList())
        {
            _runningViews[key].Detach();
            _runningViews.Remove(key);
        }

        // Kuyruk kısmını (ayraç + çalışan uygulamalar) yeniden kur
        int itemCount = _config.Items.Count(i => _itemViews.ContainsKey(i.Id));
        while (ItemsPanel.Children.Count > itemCount)
            ItemsPanel.Children.RemoveAt(ItemsPanel.Children.Count - 1);

        if (unpinned.Count == 0) return;
        if (itemCount > 0) ItemsPanel.Children.Add(_runningSeparator);

        bool vertical = IsVertical;
        foreach (var group in unpinned)
        {
            if (!_runningViews.TryGetValue(group.Key, out var button))
            {
                var g = group;
                button = new AppButton(null, group);
                DockDragHelper.Attach(button, () => new DataObject(DockDragHelper.RunningAppFormat, g.Key));
                _runningViews[group.Key] = button;
            }
            button.Margin = vertical ? new Thickness(0, 1, 0, 1) : new Thickness(1, 0, 1, 0);
            button.Refresh();
            ItemsPanel.Children.Add(button);
        }
    }

    // ------------------------------------------------------------------ Sürükle-bırak

    private int PinnedViewCount => _config.Items.Count(i => _itemViews.ContainsKey(i.Id));

    private int DropIndexAt(Point panelPoint, out double caret)
    {
        int count = PinnedViewCount;
        bool vertical = IsVertical;
        int index = 0;
        caret = 0;
        double lastEnd = 0;

        for (int i = 0; i < count; i++)
        {
            var child = (FrameworkElement)ItemsPanel.Children[i];
            if (child.Visibility != Visibility.Visible) continue;
            var topLeft = child.TranslatePoint(new Point(0, 0), ItemsPanel);
            double start = vertical ? topLeft.Y : topLeft.X;
            double length = vertical ? child.ActualHeight : child.ActualWidth;
            double pointer = vertical ? panelPoint.Y : panelPoint.X;
            lastEnd = start + length;

            if (pointer < start + length / 2)
            {
                caret = start;
                return index;
            }
            index = i + 1;
        }
        caret = lastEnd;
        return index;
    }

    private static bool HasDockData(IDataObject data) =>
        data.GetDataPresent(DockDragHelper.ItemFormat) ||
        data.GetDataPresent(DockDragHelper.RunningAppFormat) ||
        data.GetDataPresent(DataFormats.FileDrop);

    private void OnItemsDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!HasDockData(e.Data))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        if (!_shown) Reveal();
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.Move;
        DropIndexAt(e.GetPosition(ItemsPanel), out double caret);

        var origin = ItemsPanel.TranslatePoint(new Point(0, 0), CaretLayer);
        bool vertical = IsVertical;
        DropCaret.Width = vertical ? 32 : 3;
        DropCaret.Height = vertical ? 3 : 32;
        Canvas.SetLeft(DropCaret, vertical ? (CaretLayer.ActualWidth - 32) / 2 : origin.X + caret - 1.5);
        Canvas.SetTop(DropCaret, vertical ? origin.Y + caret - 1.5 : (CaretLayer.ActualHeight - 32) / 2);
        DropCaret.Visibility = Visibility.Visible;
    }

    private void OnItemsDragLeave(object sender, DragEventArgs e) => DropCaret.Visibility = Visibility.Collapsed;

    private void OnItemsDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        DropCaret.Visibility = Visibility.Collapsed;
        int index = DropIndexAt(e.GetPosition(ItemsPanel), out _);
        var config = AppServices.ConfigService;

        if (e.Data.GetData(DockDragHelper.ItemFormat) is string itemId)
        {
            config.MoveItem(itemId, index);
        }
        else if (e.Data.GetData(DockDragHelper.RunningAppFormat) is string key &&
                 _shell.RunningApps.Find(key) is { } group &&
                 AppLauncher.PinnablePath(group) is { } path)
        {
            config.AddItem(DockItem.App(path, group.Title), index);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files.Where(f => !string.IsNullOrWhiteSpace(f)))
                config.AddItem(DockItem.App(file), index++);
        }
    }

    // ------------------------------------------------------------------ Kaydırma

    private double ScrollOffset => IsVertical ? Scroller.VerticalOffset : Scroller.HorizontalOffset;

    private double ScrollableLength => IsVertical ? Scroller.ScrollableHeight : Scroller.ScrollableWidth;

    private double ViewportLength => IsVertical ? Scroller.ViewportHeight : Scroller.ViewportWidth;

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Menü veya açılır pencere (mikser, takvim vb.) açıkken dock'u yatay kaydırma
        if (GlobalPopupDismissHook.HasActivePopupsOrMenus) return;

        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is TextBox { IsKeyboardFocusWithin: true } box && box.ExtentHeight > box.ViewportHeight) return;
            // Ses widget'ı fare tekerleğini kendisi kullanır (ses seviyesi ayarı)
            if (d is Widgets.AudioWidget) return;
            if (ReferenceEquals(d, Scroller)) break;
        }

        if (ScrollableLength <= 0) return;
        e.Handled = true;
        SmoothScrollBy(-e.Delta * 0.9);
    }

    private void SmoothScrollBy(double delta)
    {
        double from = double.IsNaN(_scrollTarget) ? ScrollOffset : _scrollTarget;
        _scrollTarget = Math.Clamp(from + delta, 0, ScrollableLength);
        if (_scrollAnimating) return;
        _scrollAnimating = true;
        _lastScrollFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnScrollFrame;
    }

    private void OnScrollFrame(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        double dt = _lastScrollFrame == TimeSpan.Zero ? 1 / 60.0 : (time - _lastScrollFrame).TotalSeconds;
        if (dt <= 0) return; // aynı kare için ikinci çağrı
        _lastScrollFrame = time;
        dt = Math.Min(dt, 0.05);

        double current = ScrollOffset;
        double diff = _scrollTarget - current;
        // Kritik sönümlü yaklaşım: ~120 ms'de hedefe yerleşir
        double next = Math.Abs(diff) < 0.5 ? _scrollTarget : current + diff * (1 - Math.Exp(-dt * 18));

        if (IsVertical) Scroller.ScrollToVerticalOffset(next);
        else Scroller.ScrollToHorizontalOffset(next);

        if (next == _scrollTarget)
        {
            CompositionTarget.Rendering -= OnScrollFrame;
            _scrollAnimating = false;
            _scrollTarget = double.NaN;
        }
    }

    private void OnScrollBackClick(object sender, RoutedEventArgs e) => SmoothScrollBy(-ViewportLength * 0.6);

    private void OnScrollForwardClick(object sender, RoutedEventArgs e) => SmoothScrollBy(ViewportLength * 0.6);

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        bool scrollable = ScrollableLength > 0.5;
        var visibility = scrollable ? Visibility.Visible : Visibility.Collapsed;
        if (ScrollBackButton.Visibility != visibility)
        {
            ScrollBackButton.Visibility = ScrollForwardButton.Visibility = visibility;
            UpdateTrayVisibility();
        }
        ScrollBackButton.IsEnabled = ScrollOffset > 0.5;
        ScrollForwardButton.IsEnabled = ScrollOffset < ScrollableLength - 0.5;
        UpdateFadeMask();
    }

    /// <summary>Kaydırılabilir içerikte kenarları yumuşakça soldurur.</summary>
    private void UpdateFadeMask()
    {
        double length = IsVertical ? Scroller.ActualHeight : Scroller.ActualWidth;
        if (ScrollableLength <= 0.5 || length <= 0)
        {
            Scroller.OpacityMask = null;
            return;
        }

        double fade = Math.Min(0.25, 28 / length);
        bool startFade = ScrollOffset > 0.5;
        bool endFade = ScrollOffset < ScrollableLength - 0.5;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = IsVertical ? new Point(0, 1) : new Point(1, 0),
        };
        brush.GradientStops.Add(new GradientStop(startFade ? Colors.Transparent : Colors.Black, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Black, fade));
        brush.GradientStops.Add(new GradientStop(Colors.Black, 1 - fade));
        brush.GradientStops.Add(new GradientStop(endFade ? Colors.Transparent : Colors.Black, 1));
        brush.Freeze();
        Scroller.OpacityMask = brush;
    }

    // ------------------------------------------------------------------ Konumlandırma

    private void UpdateReserver()
    {
        bool needed = !_config.AutoHide && !_closing;
        double thickness = ThicknessDip + 2 * MarginDip;
        var key = (_monitor.DeviceName, _config.Edge, thickness);

        if (needed && _reserver is not null && _reserverKey == key) return;

        if (_reserver is not null)
        {
            _reserver.RectChanged -= QueueReposition;
            _reserver.CloseReserver();
            _reserver = null;
            _reserverKey = null;
        }

        if (!needed) return;

        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, _monitor.DeviceName, StringComparison.OrdinalIgnoreCase))
                     ?? Forms.Screen.PrimaryScreen!;
        var edge = _config.Edge switch
        {
            DockEdge.Top => AppBarEdge.Top,
            DockEdge.Left => AppBarEdge.Left,
            DockEdge.Right => AppBarEdge.Right,
            _ => AppBarEdge.Bottom,
        };

        try
        {
            _reserver = new SpaceReserver(_shell.Manager, AppBarScreen.FromScreen(screen), edge, thickness);
            _reserver.RectChanged += QueueReposition;
            _reserver.Show();
            _reserverKey = key;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ekran alanı ayrılamadı");
            _reserver = null;
        }
    }

    private void QueueReposition()
    {
        if (_repositionQueued || _closing) return;
        _repositionQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _repositionQueued = false;
            Reposition();
        });
    }

    private double DesiredLengthPx(double dpi)
    {
        var zonesMargin = Zones.Margin;
        Zones.Measure(IsVertical
            ? new Size(Zones.ActualWidth > 0 ? Zones.ActualWidth : 46, double.PositiveInfinity)
            : new Size(double.PositiveInfinity, Zones.ActualHeight > 0 ? Zones.ActualHeight : 46));
        double length = IsVertical
            ? Zones.DesiredSize.Height + zonesMargin.Top + zonesMargin.Bottom
            : Zones.DesiredSize.Width + zonesMargin.Left + zonesMargin.Right;
        return Math.Ceiling(length * dpi);
    }

    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero || _closing) return;

        _monitor = MonitorHelper.GetPreferred(_config.MonitorDevice);
        double dpi = _monitor.DpiScale;
        int barPx = (int)Math.Round(ThicknessDip * dpi);
        int marginPx = (int)Math.Round(MarginDip * dpi);

        RECT area;
        if (_reserver is { Handle: not 0 } reserver && reserver.Rect.Width > 0 && reserver.Rect.Height > 0)
            area = reserver.Rect;
        else
            area = _shell.IsReplacingTaskbar ? _monitor.Bounds : _monitor.WorkArea;

        bool vertical = IsVertical;
        int maxLength = (vertical ? area.Height : area.Width) - 2 * marginPx;
        int length = _config.WidthMode == DockWidthMode.Full
            ? maxLength
            : (int)Math.Min(maxLength, DesiredLengthPx(dpi));

        _shownRect = _config.Edge switch
        {
            DockEdge.Top => Rect(area.Left + (area.Width - length) / 2, area.Top + marginPx, length, barPx),
            DockEdge.Left => Rect(area.Left + marginPx, area.Top + (area.Height - length) / 2, barPx, length),
            DockEdge.Right => Rect(area.Right - marginPx - barPx, area.Top + (area.Height - length) / 2, barPx, length),
            _ => Rect(area.Left + (area.Width - length) / 2, area.Bottom - marginPx - barPx, length, barPx),
        };

        if (!_animating && _shown)
            MoveTo(_shownRect);

        UpdateTrigger();
        UpdateTrayHost();
        UpdateFadeMask();
    }

    private static RECT Rect(int x, int y, int w, int h) => new(x, y, x + w, y + h);

    private void MoveTo(RECT r)
        => SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);

    private RECT HiddenRect()
    {
        var b = _monitor.Bounds;
        var r = _shownRect;
        return _config.Edge switch
        {
            DockEdge.Top => new RECT(r.Left, b.Top - r.Height - 2, r.Right, b.Top - 2),
            DockEdge.Left => new RECT(b.Left - r.Width - 2, r.Top, b.Left - 2, r.Bottom),
            DockEdge.Right => new RECT(b.Right + 2, r.Top, b.Right + 2 + r.Width, r.Bottom),
            _ => new RECT(r.Left, b.Bottom + 2, r.Right, b.Bottom + 2 + r.Height),
        };
    }

    /// <summary>Başlat menüsü, hızlı ayarlar ve bildirimler bu dikdörtgene göre konumlanır.</summary>
    private void UpdateTrayHost()
    {
        if (_hwnd == IntPtr.Zero || _shownRect.Width <= 0) return;
        _shell.SetTrayHost(_shownRect, _config.Edge);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            _monitor = MonitorHelper.GetPreferred(_config.MonitorDevice);
            UpdateReserver();
            QueueReposition();
        });

    private static readonly HashSet<string> MenuWindowClasses = new(StringComparer.Ordinal)
    {
        "#32768", "tooltips_class32", "SysShadow", "Xaml_WindowedPopupClass", "DropDown", "Chrome_WidgetWin_2",
    };

    private void ReassertTopmost()
    {
        if (_closing || _fullscreen) return;
        // Menü/panel açıkken dock'u öne almak onların altında kalmasına yol açar.
        if (_openMenus.Count > 0 || _interactionCount > 0) return;
        const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER;
        if (_shown && IsVisible && IsCoveredByForeignWindow())
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, flags);
        if (_trigger is { IsVisible: true })
            SetWindowPos(_trigger.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
    }

    /// <summary>
    /// Dock'un üstündeki pencereleri tarar: bizim ya da bir menü/araç ipucu penceresi varsa dokunma;
    /// yalnızca başka bir uygulamanın penceresi dock'u örtüyorsa true.
    /// </summary>
    private bool IsCoveredByForeignWindow()
    {
        if (!GetWindowRect(_hwnd, out var dock)) return false;
        int guard = 0;
        for (IntPtr h = NativeMethods.GetWindow(_hwnd, GW_HWNDPREV); h != IntPtr.Zero && guard++ < 256; h = NativeMethods.GetWindow(h, GW_HWNDPREV))
        {
            if (!IsWindowVisible(h) || IsCloaked(h)) continue;
            if (!GetWindowRect(h, out var r)) continue;
            if (r.Right <= dock.Left || r.Left >= dock.Right || r.Bottom <= dock.Top || r.Top >= dock.Bottom) continue;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == _processId) return false;
            if (MenuWindowClasses.Contains(GetClassName(h))) return false;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ Tam ekran / görünürlük

    private void OnFullScreenAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        bool fullscreen = _shell.Manager.FullScreenHelper.FullScreenApps.Any(app =>
            app.screen.IsVirtualScreen || string.Equals(app.screen.DeviceName, _monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
        if (fullscreen == _fullscreen) return;
        _fullscreen = fullscreen;
        UpdateVisibility(animate: false);
        UpdateTrigger();
    }

    private void OnLauncherVisibilityChanged(bool visible)
    {
        if (!_config.AutoHide) return;
        if (visible) Reveal();
        else ScheduleAutoHide();
    }

    private bool IsFullscreenBlocked => _config.HideOnFullscreen && _fullscreen;

    private void UpdateVisibility(bool animate)
    {
        bool shouldShow = !IsFullscreenBlocked && (!_config.AutoHide || _revealed);
        if (shouldShow == _shown && (_animating || IsVisible == shouldShow)) return;
        _shown = shouldShow;
        _ = AnimateAsync(shouldShow, animate);
        UpdateTrigger();
    }

    private async Task AnimateAsync(bool show, bool animate)
    {
        int version = ++_animationVersion;
        RECT from, to;

        if (show)
        {
            if (!IsVisible)
            {
                if (_shownRect.Width <= 0) Reposition();
                MoveTo(animate ? HiddenRect() : _shownRect);
                Show();
            }
            if (!animate)
            {
                MoveTo(_shownRect);
                return;
            }
            GetWindowRect(_hwnd, out from);
            to = _shownRect;
        }
        else
        {
            if (!IsVisible) return;
            if (!animate)
            {
                Hide();
                return;
            }
            GetWindowRect(_hwnd, out from);
            to = HiddenRect();
        }

        _animating = true;
        try
        {
            var duration = show ? ShowDuration : HideDuration;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < duration)
            {
                double t = sw.Elapsed.TotalMilliseconds / duration.TotalMilliseconds;
                // Açılış: uzun yavaşlama (ease-out quint); kapanış: hızlanarak çıkış
                double eased = show ? 1 - Math.Pow(1 - t, 5) : t * t * t;
                int x = (int)Math.Round(from.Left + (to.Left - from.Left) * eased);
                int y = (int)Math.Round(from.Top + (to.Top - from.Top) * eased);
                SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                await NextFrame();
                if (version != _animationVersion || _closing) return;
            }

            MoveTo(to);
            if (!show) Hide();
        }
        finally
        {
            if (version == _animationVersion) _animating = false;
        }
    }

    /// <summary>Bir sonraki ekran karesini bekler (animasyonu ekran yenilemesiyle eşzamanlar).</summary>
    private static Task NextFrame()
    {
        var tcs = new TaskCompletionSource();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            CompositionTarget.Rendering -= handler;
            tcs.TrySetResult();
        };
        CompositionTarget.Rendering += handler;
        return tcs.Task;
    }

    public void Reveal()
    {
        _revealed = true;
        _hideTimer.Stop();
        UpdateVisibility(animate: true);
        ScheduleAutoHide();
    }

    private void ScheduleAutoHide()
    {
        if (!_config.AutoHide || _interactionCount > 0 || _closing) return;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void TryAutoHide()
    {
        if (!_config.AutoHide || _interactionCount > 0 || !_shown) return;
        if (_shell.IsLauncherVisible || IsCursorOverDock()) return;
        if (_inputMode && IsActive) return;

        _revealed = false;
        UpdateVisibility(animate: true);
    }

    private bool IsCursorOverDock()
    {
        if (!GetCursorPos(out var p) || !GetWindowRect(_hwnd, out var r)) return false;
        const int slack = 2;
        return p.X >= r.Left - slack && p.X <= r.Right + slack && p.Y >= r.Top - slack && p.Y <= r.Bottom + slack;
    }

    private void UpdateTrigger()
    {
        bool active = _config.AutoHide && !IsFullscreenBlocked && !_shown && !_closing;
        if (!active)
        {
            _trigger?.SetActive(false);
            return;
        }

        if (_trigger is null)
        {
            _trigger = new EdgeTriggerWindow();
            _trigger.Entered += () => _revealTimer.Start();
            _trigger.Exited += () => _revealTimer.Stop();
            _trigger.DragEntered += Reveal;
        }

        var b = _monitor.Bounds;
        var dock = _shownRect;
        RECT rect = _config.Edge switch
        {
            DockEdge.Top => new RECT(dock.Left, b.Top, dock.Right, b.Top + TriggerThickness),
            DockEdge.Left => new RECT(b.Left, dock.Top, b.Left + TriggerThickness, dock.Bottom),
            DockEdge.Right => new RECT(b.Right - TriggerThickness, dock.Top, b.Right, dock.Bottom),
            _ => new RECT(dock.Left, b.Bottom - TriggerThickness, dock.Right, b.Bottom),
        };

        _trigger.SetActive(true);
        _trigger.Place(rect);
    }

    // ------------------------------------------------------------------ Saat

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

    // ------------------------------------------------------------------ Düğmeler

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
        menu.Items.Add(DockMenu.Item("Bildirim merkezi", "\uE91C", _shell.ShowNotificationCenter));
        menu.Items.Add(DockMenu.Item("Hızlı ayarlar", "\uE9E9", _shell.ShowQuickSettings));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("Tarih ve saati ayarla", "\uE787",
            () => Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true })));
        menu.Items.Add(DockMenu.Check("Saniyeleri göster", _config.ClockShowSeconds, () => _config.ClockShowSeconds = !_config.ClockShowSeconds));
        menu.Items.Add(DockMenu.Check("Tarihi göster", _config.ClockShowDate, () => _config.ClockShowDate = !_config.ClockShowDate));
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
        TrayOverflowPopup.IsOpen = true;
    }

    private void OnTrayOverflowClosed(object? sender, EventArgs e)
    {
        TrayOverflowButton.IsChecked = false;
        EndInteraction();
    }

    // ------------------------------------------------------------------ Dock menüsü

    private void OnDockMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Root'un kendi ContextMenu'sü var; tepsi simgesi gibi ContextMenu'sü olmayan bir alt öğeye
        // sağ tıklanınca varsayılan davranış bu olayı en yakın sahip (Root) üzerinden başlatır. O yüzden
        // gerçek tıklama noktasının tepsi simgesi olup olmadığını burada da kontrol etmemiz gerekir.
        if (FindAncestor<TrayIconView>(e.OriginalSource as DependencyObject) is not null)
        {
            e.Handled = true;
            return;
        }
        var menu = Root.ContextMenu;
        menu.Items.Clear();
        menu.Items.Add(DockMenu.Item("Widget ekle…", "\uE710", () => App.Instance.ShowSettings("gallery")));
        menu.Items.Add(DockMenu.Item("Uygulama sabitle…", "\uE718", () => App.Instance.ShowAppPicker()));
        menu.Items.Add(DockMenu.Item("Ayraç ekle", "\uE76F", () => AppServices.ConfigService.AddItem(DockItem.Separator())));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("Görev Yöneticisi", "\uE9D9", () => Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true })));
        menu.Items.Add(DockMenu.Item("Windows Ayarları", "", () => Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true })));
        menu.Items.Add(DockMenu.Item("Hızlı ayarlar", "\uE9E9", () => { UpdateTrayHost(); _shell.ShowQuickSettings(); }));
        menu.Items.Add(DockMenu.Check("Otomatik gizle", _config.AutoHide, () => _config.AutoHide = !_config.AutoHide));
        menu.Items.Add(DockMenu.Check("Windows görev çubuğunu gizle", _config.TaskbarMode == TaskbarMode.Replace,
            () => _config.TaskbarMode = _config.TaskbarMode == TaskbarMode.Replace ? TaskbarMode.ShowBoth : TaskbarMode.Replace));
        menu.Items.Add(DockMenu.Submenu("Konum", "\uE8A0", new[]
        {
            DockMenu.Check("Alt", _config.Edge == DockEdge.Bottom, () => _config.Edge = DockEdge.Bottom),
            DockMenu.Check("Üst", _config.Edge == DockEdge.Top, () => _config.Edge = DockEdge.Top),
            DockMenu.Check("Sol", _config.Edge == DockEdge.Left, () => _config.Edge = DockEdge.Left),
            DockMenu.Check("Sağ", _config.Edge == DockEdge.Right, () => _config.Edge = DockEdge.Right),
        }));
        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("DockHub ayarları…", "\uE713", () => App.Instance.ShowSettings()));
        menu.Items.Add(DockMenu.Item("Çıkış", "\uE7E8", () => App.Instance.ExitApplication()));
    }

    // ------------------------------------------------------------------ Kapanış

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

        if (_reserver is not null)
        {
            _reserver.RectChanged -= QueueReposition;
            _reserver.CloseReserver();
            _reserver = null;
        }
        _trigger?.Close();
        _magnifier?.Dispose();
        Close();
    }
}
