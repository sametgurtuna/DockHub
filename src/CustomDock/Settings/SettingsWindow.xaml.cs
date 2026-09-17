using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Native;
using CustomDock.Shell;
using CustomDock.Widgets;
using TrayIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace CustomDock.Settings;

/// <summary>Dock öğeleri listesindeki bir satır.</summary>
public sealed class ItemRow
{
    public ItemRow(DockItem item)
    {
        Item = item;
        switch (item.Kind)
        {
            case DockItemKind.App:
                Icon = item.Path is null ? null : ShellIcons.GetIcon(item.Path, 48);
                Title = !string.IsNullOrWhiteSpace(item.Name) ? item.Name! : AppDisplayName(item.Path ?? "");
                Subtitle = "Uygulama";
                break;
            case DockItemKind.Widget:
                var descriptor = WidgetRegistry.Find(item.Widget);
                Glyph = descriptor?.Icon;
                GlyphBrush = descriptor is null ? null : Application.Current.TryFindResource(descriptor.AccentKey) as Brush;
                Title = descriptor?.Name ?? item.Widget ?? "Widget";
                Subtitle = descriptor is null ? "" : $"Widget · {descriptor.VariantName(item.Variant)}";
                break;
            default:
                Glyph = Geometry.Parse("M12,3 V21");
                GlyphBrush = Application.Current.TryFindResource("TextSecondaryBrush") as Brush;
                Title = "Ayraç";
                Subtitle = "Öğeleri gruplar";
                break;
        }
    }

    public DockItem Item { get; }
    public ImageSource? Icon { get; }
    public Geometry? Glyph { get; }
    public Brush? GlyphBrush { get; }
    public string Title { get; }
    public string Subtitle { get; }

    public static string AppDisplayName(string path)
    {
        if (path.StartsWith(AppKeys.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
            return path[AppKeys.AppsFolderPrefix.Length..].Split('!')[0].Split('_')[0];
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            try
            {
                var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            }
            catch
            {
                // yoksay
            }
        }
        return Path.GetFileNameWithoutExtension(path);
    }
}

public sealed record MonitorOption(string Label, string? Device);

/// <summary>Galeri önizlemeleri için sahte dock ortamı.</summary>
internal sealed class PreviewHost : IWidgetHost
{
    public PreviewHost(Window window) => Window = window;
    public DockEdge Edge => DockEdge.Bottom;
    public bool IsVertical => false;
    public Window Window { get; }
    public bool IsPreview => true;
    public void BeginInteraction() { }
    public void EndInteraction() { }
    public void ActivateForInput() { }
}

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
        VersionText.Text = $"Sürüm {version}";
        AboutVersion.Text = $"Sürüm {version} · .NET {Environment.Version.ToString(2)} · WPF";
        ConfigFolderRow.Description = AppPaths.Root;

        LoadMonitors();
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

    // ------------------------------------------------------------------ Navigasyon

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

    // ------------------------------------------------------------------ Genel

    private void OnRestoreTaskbarClick(object sender, RoutedEventArgs e)
    {
        TaskbarController.ForceShow();
        _config.TaskbarMode = TaskbarMode.ShowBoth;
    }

    private void OnRestartClick(object sender, RoutedEventArgs e) => App.Instance.RestartApplication();

    private void OnOpenConfigFolderClick(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Root}\"") { UseShellExecute = true });

    private void OnExitClick(object sender, RoutedEventArgs e) => App.Instance.ExitApplication();

    private void OnOpenLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url })
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    // ------------------------------------------------------------------ Görev çubuğu

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

    // ------------------------------------------------------------------ Monitör

    private void LoadMonitors()
    {
        var options = new List<MonitorOption> { new("Birincil ekran (otomatik)", null) };
        options.AddRange(MonitorHelper.GetAll().Select(m => new MonitorOption(m.DisplayName, m.DeviceName)));
        if (_config.MonitorDevice is { } saved && options.All(o => o.Device != saved))
            options.Add(new MonitorOption($"{saved} (bağlı değil)", saved));

        MonitorCombo.ItemsSource = options;
        MonitorCombo.SelectedItem = options.FirstOrDefault(o => o.Device == _config.MonitorDevice) ?? options[0];
    }

    private void OnMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorCombo.SelectedItem is MonitorOption option && option.Device != _config.MonitorDevice)
            _config.MonitorDevice = option.Device;
    }

    // ------------------------------------------------------------------ Dock öğeleri

    private void OnConfigItemsChanged(object? sender, EventArgs e) => LoadItems();

    private void LoadItems()
    {
        var selectedId = (ItemList.SelectedItem as ItemRow)?.Item.Id;
        _rows.Clear();
        foreach (var item in _config.Items)
            _rows.Add(new ItemRow(item));
        ItemList.SelectedItem = _rows.FirstOrDefault(r => r.Item.Id == selectedId);
        if (ItemList.SelectedItem is null) ShowDetail(null);
    }

    private void OnItemSelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetail(ItemList.SelectedItem as ItemRow);

    private void ShowDetail(ItemRow? row)
    {
        _detailPreview?.Detach();
        _detailPreview = null;
        if (_detailSource is not null) _detailSource.PropertyChanged -= OnDetailSourceChanged;
        _detailSource = null;
        _detailPreviewItem = null;
        DetailPreviewHost.Child = null;
        DetailPreviewHost.Visibility = Visibility.Collapsed;

        EmptyDetail.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        if (row is null) return;

        var item = row.Item;
        DetailTitle.Text = row.Title;
        DetailImage.Source = row.Icon;
        DetailGlyph.Data = row.Glyph;
        DetailGlyph.Stroke = row.GlyphBrush;
        AppDetail.Visibility = item.Kind == DockItemKind.App ? Visibility.Visible : Visibility.Collapsed;
        WidgetDetail.Visibility = item.Kind == DockItemKind.Widget ? Visibility.Visible : Visibility.Collapsed;

        switch (item.Kind)
        {
            case DockItemKind.App:
                DetailSubtitle.Text = item.Path ?? "";
                AppNameBox.Text = item.Name ?? "";
                AppNameBox.Tag = ItemRow.AppDisplayName(item.Path ?? "");
                AppArgsBox.Text = item.Arguments ?? "";
                AppPathRow.Description = item.Path;
                break;

            case DockItemKind.Widget when WidgetRegistry.Find(item.Widget) is { } descriptor:
                DetailSubtitle.Text = descriptor.Description;
                _suppressVariant = true;
                VariantCombo.ItemsSource = descriptor.Variants;
                VariantCombo.SelectedItem = descriptor.Variants.FirstOrDefault(v => v.Id == item.Variant) ?? descriptor.Variants[0];
                _suppressVariant = false;

                if (descriptor.SettingsType is null)
                {
                    WidgetSettingsHost.Content = new TextBlock
                    {
                        Text = "Bu widget'ın ek ayarı yok. Kullanım için widget'a dock üzerinde tıklayın veya sağ tıklayın.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)FindResource("TextSecondaryBrush"),
                        Margin = new Thickness(2, 6, 0, 0),
                    };
                }
                else
                {
                    var settings = AppServices.ConfigService.GetItemSettings(item, descriptor.SettingsType);
                    WidgetSettingsHost.Content = descriptor.SettingsViewFactory?.Invoke(settings)
                                                 ?? new ContentControl { Content = settings, Focusable = false };
                }

                ShowDetailPreview(descriptor, item);
                break;

            default:
                DetailSubtitle.Text = "Dock'taki öğeleri görsel olarak gruplar.";
                break;
        }
    }

    /// <summary>Seçili widget'ın canlı önizlemesi (aynı ayarları paylaşır).</summary>
    private void ShowDetailPreview(WidgetDescriptor descriptor, DockItem item)
    {
        try
        {
            // Aynı ayar nesnesini kullanan, ancak kalıcı veri yazmayan kopya öğe
            var previewItem = new DockItem { Id = item.Id, Kind = DockItemKind.Widget, Widget = item.Widget, Variant = item.Variant, Settings = item.Settings };
            _detailSource = item;
            _detailPreviewItem = previewItem;
            item.PropertyChanged += OnDetailSourceChanged;
            var widget = descriptor.Create(previewItem);
            DetailPreviewHost.Child = CreatePreviewCard(widget);
            widget.Attach(_previewHost);
            _detailPreview = widget;
            DetailPreviewHost.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Önizleme oluşturulamadı");
        }
    }

    private void OnDetailSourceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DockItem.Variant) && _detailPreviewItem is not null && _detailSource is not null)
            _detailPreviewItem.Variant = _detailSource.Variant;
    }

    private void OnVariantChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressVariant || ItemList.SelectedItem is not ItemRow row || VariantCombo.SelectedItem is not WidgetVariant variant) return;
        if (row.Item.Variant == variant.Id) return;
        row.Item.Variant = variant.Id;
        AppServices.ConfigService.ScheduleSave();
    }

    private void OnAppNameChanged(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ItemRow row) return;
        var name = AppNameBox.Text.Trim();
        var value = string.IsNullOrEmpty(name) ? null : name;
        if (row.Item.Name == value) return;
        row.Item.Name = value;
        AppServices.ConfigService.ReplaceItems(_config.Items);
    }

    private void OnAppNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnAppNameChanged(sender, e);
    }

    private void OnAppArgsChanged(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ItemRow row) return;
        var args = AppArgsBox.Text.Trim();
        row.Item.Arguments = string.IsNullOrEmpty(args) ? null : args;
        AppServices.ConfigService.ScheduleSave();
    }

    private void OnOpenAppLocationClick(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is ItemRow { Item.Path: { } path })
            Services.AppLauncher.OpenLocation(path);
    }

    private void OnAddAppClick(object sender, RoutedEventArgs e) => App.Instance.ShowAppPicker();

    private void OnOpenGalleryClick(object sender, RoutedEventArgs e) => NavigateTo("gallery");

    private void OnAddSeparatorClick(object sender, RoutedEventArgs e)
        => AppServices.ConfigService.AddItem(DockItem.Separator(), DockItemsIndex.EndOfApps());

    private void OnImportPinsClick(object sender, RoutedEventArgs e)
    {
        var existing = _config.Items.Where(i => i.Kind == DockItemKind.App).Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int index = DockItemsIndex.EndOfApps();
        int added = 0;
        foreach (var item in DefaultItems.ImportTaskbarPins().Where(i => !existing.Contains(i.Path)))
        {
            _config.Items.Insert(index++, item);
            added++;
        }
        if (added > 0) _config.NotifyItemsChanged();
        MessageBox.Show(this, added > 0 ? $"{added} uygulama eklendi." : "Eklenecek yeni sabitleme bulunamadı.", "Custom Dock",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnRemoveItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ItemRow row)
            AppServices.ConfigService.RemoveItem(row.Item.Id);
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => MoveRow(sender, -1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => MoveRow(sender, +1);

    private void MoveRow(object sender, int delta)
    {
        if ((sender as FrameworkElement)?.DataContext is not ItemRow row) return;
        int from = _config.Items.IndexOf(row.Item);
        int to = Math.Clamp(from + delta, 0, _config.Items.Count - 1);
        if (from < 0 || from == to) return;
        _config.Items.RemoveAt(from);
        _config.Items.Insert(to, row.Item);
        _config.NotifyItemsChanged();
        ItemList.SelectedItem = _rows.FirstOrDefault(r => r.Item == row.Item);
    }

    private void OnItemListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        if (e.OriginalSource is FrameworkElement { Tag: "drag-handle", DataContext: ItemRow row })
        {
            _dragCandidate = row;
            _dragStart = e.GetPosition(ItemList);
        }
    }

    private void OnItemListMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;
        if (Math.Abs((e.GetPosition(ItemList) - _dragStart).Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = _dragCandidate;
        _dragCandidate = null;
        DragDrop.DoDragDrop(ItemList, new DataObject(DragFormat, row), DragDropEffects.Move);
    }

    private void OnItemListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnItemListDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not ItemRow dragged) return;
        var hit = VisualTreeHelper.HitTest(ItemList, e.GetPosition(ItemList))?.VisualHit;
        while (hit is not null and not ListBoxItem) hit = VisualTreeHelper.GetParent(hit);
        var target = (hit as ListBoxItem)?.DataContext as ItemRow;
        int to = target is null ? _config.Items.Count : _config.Items.IndexOf(target.Item);
        if (target is not null && _config.Items.IndexOf(dragged.Item) < to) to++;
        AppServices.ConfigService.MoveItem(dragged.Item.Id, to);
        ItemList.SelectedItem = _rows.FirstOrDefault(r => r.Item == dragged.Item);
    }

    // ------------------------------------------------------------------ Galeri

    private FrameworkElement CreatePreviewCard(WidgetBase widget)
    {
        var card = new WidgetCard { Content = widget, HoverEnabled = false, Margin = new Thickness(0) };
        void Apply()
        {
            if (widget.CardBackground is { } background) card.Background = background;
            else card.SetResourceReference(BackgroundProperty, "CardBrush");
            card.Padding = widget.CardPadding;
        }
        widget.CardAppearanceChanged += Apply;
        Apply();
        return card;
    }

    private void BuildGallery()
    {
        foreach (var category in WidgetCategories.Ordered)
        {
            var descriptors = WidgetRegistry.All.Where(d => d.Category == category).ToList();
            if (descriptors.Count == 0) continue;

            GalleryPanel.Children.Add(new TextBlock
            {
                Text = category,
                FontFamily = (FontFamily)FindResource("DisplayFont"),
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 18, 0, 10),
            });

            var wrap = new WrapPanel();
            foreach (var descriptor in descriptors)
            {
                foreach (var variant in descriptor.Variants)
                    wrap.Children.Add(CreateGalleryTile(descriptor, variant));
            }
            GalleryPanel.Children.Add(wrap);
        }
    }

    private FrameworkElement CreateGalleryTile(WidgetDescriptor descriptor, WidgetVariant variant)
    {
        var previewItem = new DockItem { Id = "preview-" + descriptor.Id + "-" + variant.Id, Kind = DockItemKind.Widget, Widget = descriptor.Id, Variant = variant.Id };
        FrameworkElement preview;
        try
        {
            var widget = descriptor.Create(previewItem);
            preview = CreatePreviewCard(widget);
            widget.Attach(_previewHost);
            _previews.Add(widget);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Galeri önizlemesi oluşturulamadı: {descriptor.Id}");
            preview = new TextBlock { Text = descriptor.Name };
        }

        var stage = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(8),
            Child = preview,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            ToolTip = descriptor.Description,
        };
        stage.SetResourceReference(Border.BackgroundProperty, "GalleryBackdropBrush");

        var name = new TextBlock
        {
            Text = descriptor.Variants.Count > 1 ? $"{descriptor.Name} · {variant.Name}" : descriptor.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12.5,
        };

        var add = new Button
        {
            Content = "\uE710",
            Style = (Style)FindResource("IconButton"),
            ToolTip = "Dock'a ekle",
            Width = 28,
            Height = 28,
        };
        add.Click += (_, _) =>
        {
            var item = DockItem.ForWidget(descriptor.Id, variant.Id);
            AppServices.ConfigService.AddItem(item);
            add.Content = "\uE73E";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            timer.Tick += (_, _) => { timer.Stop(); add.Content = "\uE710"; };
            timer.Start();
        };

        var footer = new DockPanel { Margin = new Thickness(4, 6, 0, 0) };
        DockPanel.SetDock(add, System.Windows.Controls.Dock.Right);
        footer.Children.Add(add);
        footer.Children.Add(name);

        var tile = new StackPanel { Margin = new Thickness(0, 0, 18, 18), MinWidth = 150 };
        tile.Children.Add(stage);
        tile.Children.Add(footer);
        // Alt satır önizleme genişliğini izlesin
        footer.SetBinding(WidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = stage });
        return tile;
    }
}
