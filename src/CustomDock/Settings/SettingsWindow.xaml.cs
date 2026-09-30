using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Services;
using CustomDock.Shell;
using CustomDock.Widgets;
using TrayIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace CustomDock.Settings;

public sealed record MonitorOption(string Label, string? Device);

public partial class SettingsWindow : Window
{
    private const string DragFormat = "DockHub.SettingsItemId";

    private readonly AppConfig _config = AppServices.Config;
    private readonly ObservableCollection<ItemRow> _rows = new();
    private readonly Dictionary<string, FrameworkElement> _pages;
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
            [SettingsPages.Overview] = OverviewPage,
            ["general"] = GeneralPage,
            ["taskbar"] = TaskbarPage,
            ["appearance"] = AppearancePage,
            ["items"] = ItemsPage,
            ["gallery"] = GalleryPage,
            ["profiles"] = ProfilesPage,
            ["keyboard"] = KeyboardPage,
            ["backup"] = BackupPage,
            ["about"] = AboutPage,
        };

        var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText.Text = L.T("Version {0}", version);
        AboutVersion.Text = L.T("Version {0}", version) + $" · .NET {Environment.Version.ToString(2)} · WPF";
        ConfigFolderRow.Description = AppPaths.Root;

        LoadMonitors();
        LoadDisplaySizes();
        _config.PropertyChanged += OnDisplayConfigChanged;
        LoadTray();
        LoadHotkeys();
        LoadPresets();
        LoadProfiles();
        LoadUpdates();
        LoadTextScale();
        LoadTopBar();
        LoadLanguages();
        LoadCrashInfo();
        DockPreview.Bind(_config);
        OverviewPreview.Bind(_config);
        PreviewKeyDown += OnUndoKey;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                SettingsSearchBox.Focus();
                SettingsSearchBox.SelectAll();
                e.Handled = true;
            }
        };
        ItemList.ItemsSource = _rows;
        LoadItems();
        BuildNavigation();
        NavigateTo(SettingsPages.Default.Tag);

        _config.ItemsChanged += OnConfigItemsChanged;
        SourceInitialized += (_, _) => ApplyWindowTheme();
        StateChanged += (_, _) =>
        {
            RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            QueueGalleryPreviews(); // none while minimized
        };
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ThemeManager.ThemeChanged -= OnThemeChanged;
        _config.ItemsChanged -= OnConfigItemsChanged;
        _config.PropertyChanged -= OnDisplayConfigChanged;
        ReleaseGalleryPreviews();
        if (App.Instance.Hotkeys is { } hotkeys) hotkeys.RegistrationChanged -= RefreshHotkeyStatus;
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

    /// <summary>Opens a page by its tag (an unknown tag opens the Overview), optionally selecting a dock item.</summary>
    public void NavigateTo(string page, string? itemId = null)
    {
        string tag = SettingsPages.Resolve(page).Tag;
        foreach (ListBoxItem item in NavList.Items)
        {
            if (item.Tag as string != tag) continue;
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
            BuildFeaturedWidgets();
        }
        // Previews run only while the gallery is shown.
        QueueGalleryPreviews();
        if (tag == SettingsPages.Overview) LoadOverview();
        if (tag == "taskbar") LoadTray();
        if (tag == "appearance") LoadTopBar();
        if (tag is "about" or "backup") LoadCrashInfo();
    }

    // ------------------------------------------------------------------ General

    private void OnRestoreTaskbarClick(object sender, RoutedEventArgs e)
    {
        TaskbarController.ForceShow();
        _config.TaskbarMode = TaskbarMode.ShowBoth;
    }

    private void OnRestartClick(object sender, RoutedEventArgs e) => App.Instance.RestartApplication();

    private bool _loadingLanguages;

    private void LoadLanguages()
    {
        _loadingLanguages = true;
        LanguageCombo.Items.Clear();
        LanguageCombo.Items.Add(new ComboBoxItem { Content = L.T("System"), Tag = UiLanguage.System });
        foreach (var language in L.Languages)
            LanguageCombo.Items.Add(new ComboBoxItem { Content = language.NativeName, Tag = language.Language });
        LanguageCombo.SelectedItem = LanguageCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, _config.Language))
            ?? LanguageCombo.Items[0];
        _loadingLanguages = false;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingLanguages || LanguageCombo.SelectedItem is not ComboBoxItem { Tag: UiLanguage language }) return;
        _config.Language = language;
    }

    private void OnOpenConfigFolderClick(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true });

    private void OnExitClick(object sender, RoutedEventArgs e) => App.Instance.ExitApplication();

    private void OnCopyDiagnosticsClick(object sender, RoutedEventArgs e) => CopyDiagnostics();

    /// <summary>Puts the diagnostics report on the clipboard (and in log.txt); false when it couldn't.</summary>
    private bool CopyDiagnostics()
    {
        if (App.Instance.Shell is not { } shell) return false;
        try
        {
            string report = WindowDiagnostics.Dump(shell, AppServices.Config);
            Log.Info("Diagnostics report:" + Environment.NewLine + report);
            Clipboard.SetText(report);
            DiagnosticsButton.Content = L.T("Copied");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create diagnostics report");
            DiagnosticsButton.Content = L.T("Failed");
            return false;
        }
    }

    private void OnReportProblemClick(object sender, RoutedEventArgs e)
    {
        ProblemReport.Open();

        // The full report stays off the web page: it has window titles and app paths, so the user decides what to paste.
        if (CopyDiagnostics())
            ReportProblemRow.Description = L.T("The diagnostics report is on the clipboard. It lists the titles of open windows and the paths of pinned apps: paste it into the report only if it helps, and remove anything private.");
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
        else if (e.PropertyName == nameof(AppConfig.Language) && !Equals((LanguageCombo.SelectedItem as ComboBoxItem)?.Tag, _config.Language))
            LoadLanguages(); // changed by undo or a restored backup
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
                Header = L.T("Size on {0}", monitor.DisplayName),
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
