using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Shell;
using CustomDock.Widgets;
using TrayIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace CustomDock.Settings;

public sealed record MonitorOption(string Label, string? Device);

public partial class SettingsWindow : Window
{
    private const string DragFormat = "CustomDock.SettingsItemRow";

    private readonly AppConfig _config = AppServices.Config;
    private readonly ObservableCollection<ItemRow> _rows = new();
    private readonly Dictionary<string, FrameworkElement> _pages;
    private readonly List<WidgetBase> _previews = new();
    private readonly PreviewHost _previewHost;
    private WidgetBase? _detailPreview;
    private DockItem? _detailSource;
    private DockItem? _detailPreviewItem;
    private ItemRow? _dragCandidate;
    private Point _dragStart;
    private bool _galleryBuilt;
    private bool _suppressVariant;
    private bool _suppressGroupAccent;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = _config;
        _previewHost = new PreviewHost(this);

        _pages = new Dictionary<string, FrameworkElement>
        {
            ["general"] = GeneralPage,
            ["taskbar"] = TaskbarPage,
            ["appearance"] = AppearancePage,
            ["items"] = ItemsPage,
            ["gallery"] = GalleryPage,
            ["about"] = AboutPage,
        };

        var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = $"Version {version}";
        AboutVersion.Text = $"Version {version} · .NET {Environment.Version.ToString(2)} · WPF";
        ConfigFolderRow.Description = AppPaths.Root;

        LoadMonitors();
        LoadDisplaySizes();
        _config.PropertyChanged += OnDisplayConfigChanged;
        LoadTray();
        ItemList.ItemsSource = _rows;
        LoadItems();
        NavList.SelectedIndex = 0;

        _config.ItemsChanged += OnConfigItemsChanged;
        SourceInitialized += (_, _) => ApplyWindowTheme();
        StateChanged += (_, _) => RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _config.ItemsChanged -= OnConfigItemsChanged;
        _config.PropertyChanged -= OnDisplayConfigChanged;
        foreach (var preview in _previews) preview.Detach();
        ShowDetail(null);
    }

    private void OnThemeChanged()
    {
        ApplyWindowTheme();
        LoadItems();
    }

    private void ApplyWindowTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        WindowEffects.SetDarkMode(hwnd, ThemeManager.IsDark);
        if (WindowEffects.TryApplyMica(hwnd))
        {
            Background = Brushes.Transparent;
            WindowEffects.ExtendGlass(hwnd);
        }
        else
        {
            SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
        }
    }

    // ------------------------------------------------------------------ Navigation

    public void NavigateTo(string page, string? itemId = null)
    {
        foreach (ListBoxItem item in NavList.Items)
        {
            if (item.Tag as string != page) continue;
            NavList.SelectedItem = item;
            break;
        }

        if (itemId is not null)
            ItemList.SelectedItem = _rows.FirstOrDefault(r => r.Item.Id == itemId);
    }

    private void OnNavigationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ListBoxItem { Tag: string tag }) return;
        foreach (var (key, page) in _pages)
            page.Visibility = key == tag ? Visibility.Visible : Visibility.Collapsed;
        PageScroller.ScrollToTop();

        if (tag == "gallery" && !_galleryBuilt)
        {
            _galleryBuilt = true;
            BuildGallery();
        }
        if (tag == "taskbar") LoadTray();
    }

    // ------------------------------------------------------------------ General

    private void OnRestoreTaskbarClick(object sender, RoutedEventArgs e)
    {
        TaskbarController.ForceShow();
        _config.TaskbarMode = TaskbarMode.ShowBoth;
    }

    private void OnRestartClick(object sender, RoutedEventArgs e) => App.Instance.RestartApplication();

    private void OnOpenConfigFolderClick(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true });

    private void OnExitClick(object sender, RoutedEventArgs e) => App.Instance.ExitApplication();

    private void OnCopyDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        if (App.Instance.Shell is not { } shell) return;
        try
        {
            string report = WindowDiagnostics.Dump(shell, AppServices.Config);
            Log.Info("Diagnostics report:" + Environment.NewLine + report);
            Clipboard.SetText(report);
            DiagnosticsButton.Content = "Copied";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create diagnostics report");
            DiagnosticsButton.Content = "Failed";
        }
    }

    private void OnOpenLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url })
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    // ------------------------------------------------------------------ Taskbar

    private void LoadTray()
    {
        var tray = AppServices.Shell?.Tray;
        TrayRow.IsEnabled = tray is not null;
        TrayIconsCard.Visibility = tray is null ? Visibility.Collapsed : Visibility.Visible;
        if (tray is null) return;
        TrayIconList.ItemsSource = null;
        TrayIconList.ItemsSource = tray.TrayIcons
            .Where(i => !string.IsNullOrWhiteSpace(i.Title) || !string.IsNullOrWhiteSpace(i.Path))
            .OrderBy(i => i.IsPinned ? 0 : 1).ThenBy(i => i.Title)
            .ToList();
    }

    private void OnTrayPinClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: TrayIcon icon } box || AppServices.Shell?.Tray is not { } tray) return;
        if (icon.IsPinned) icon.Unpin();
        else icon.Pin();
        box.IsChecked = icon.IsPinned;
        TrayPreferences.Save(tray);
    }

    // ------------------------------------------------------------------ Monitor

    private void LoadMonitors()
    {
        var options = new List<MonitorOption> { new("Primary display (automatic)", null) };
        options.AddRange(MonitorHelper.GetAll().Select(m => new MonitorOption(m.DisplayName, m.DeviceName)));
        if (_config.MonitorDevice is { } saved && options.All(o => o.Device != saved))
            options.Add(new MonitorOption($"{saved} (disconnected)", saved));

        MonitorCombo.ItemsSource = options;
        MonitorCombo.SelectedItem = options.FirstOrDefault(o => o.Device == _config.MonitorDevice) ?? options[0];
    }

    private void OnDisplayConfigChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppConfig.MonitorDevice) or nameof(AppConfig.ShowOnAllDisplays))
            LoadDisplaySizes();
    }

    private sealed record SizeOption(string Label, DockSize? Size);

    /// <summary>Size picker for each display other than the main one ("Same as main" follows the general size).</summary>
    private void LoadDisplaySizes()
    {
        DisplaySizesPanel.Children.Clear();
        if (!_config.ShowOnAllDisplays) return;

        string mainDevice = MonitorHelper.GetPreferred(_config.MonitorDevice).DeviceName;
        foreach (var monitor in MonitorHelper.GetAll()
                     .Where(m => !string.Equals(m.DeviceName, mainDevice, StringComparison.OrdinalIgnoreCase)))
        {
            var options = new List<SizeOption>
            {
                new("Same as main dock", null),
                new("Small", DockSize.Small),
                new("Medium", DockSize.Medium),
                new("Large", DockSize.Large),
            };
            var combo = new ComboBox { Width = 260, DisplayMemberPath = nameof(SizeOption.Label), ItemsSource = options };
            var current = _config.DisplaySizeOf(monitor.DeviceName);
            combo.SelectedItem = options.First(o => o.Size == current);
            string device = monitor.DeviceName;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is SizeOption option)
                    _config.SetDisplaySize(device, option.Size);
            };

            DisplaySizesPanel.Children.Add(new Controls.SettingRow
            {
                Glyph = "",
                Header = $"Size on {monitor.DisplayName}",
                Description = "Dock size on this display.",
                Content = combo,
            });
        }
    }

    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorCombo.SelectedItem is MonitorOption option && option.Device != _config.MonitorDevice)
            _config.MonitorDevice = option.Device;
    }
}
