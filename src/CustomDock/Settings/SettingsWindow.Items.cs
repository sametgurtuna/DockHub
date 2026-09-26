using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Native;
using CustomDock.Shell;
using CustomDock.Widgets;

namespace CustomDock.Settings;

/// <summary>A row in the dock items list.</summary>
public sealed class ItemRow
{
    public ItemRow(DockItem item)
    {
        Item = item;
        switch (item.Kind)
        {
            case DockItemKind.App:
                Icon = item.Path is null ? null : AppIcons.For(item, 48, out _);
                Title = !string.IsNullOrWhiteSpace(item.Name) ? item.Name! : AppDisplayName(item.Path ?? "");
                Subtitle = IsMissing(item.Path) ? "Application · file not found, check the Target below" : "Application";
                break;
            case DockItemKind.Widget:
                var descriptor = WidgetRegistry.Find(item.Widget);
                Glyph = descriptor?.Icon;
                GlyphBrush = descriptor is null ? null : Application.Current.TryFindResource(descriptor.AccentKey) as Brush;
                Title = descriptor?.Name ?? item.Widget ?? "Widget";
                Subtitle = descriptor is null ? "Not supported in this version of DockHub" : $"Widget · {descriptor.VariantName(item.Variant)}";
                break;
            case DockItemKind.Group:
                Glyph = Geometry.Parse("M3,7 H21 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 Z M3,7 L7,3 H13 L15,5");
                GlyphBrush = Application.Current.TryFindResource(item.GroupAccent ?? "AccentBlueBrush") as Brush;
                Title = item.GroupName ?? "Group";
                Subtitle = $"Group · {item.Children?.Count ?? 0} items";
                break;
            default:
                Glyph = Geometry.Parse("M12,3 V21");
                GlyphBrush = Application.Current.TryFindResource("TextSecondaryBrush") as Brush;
                Title = "Separator";
                Subtitle = "Groups items";
                break;
        }
    }

    public DockItem Item { get; }
    public ImageSource? Icon { get; }
    public Geometry? Glyph { get; }
    public Brush? GlyphBrush { get; }
    public string Title { get; }
    public string Subtitle { get; }

    /// <summary>A pinned file or program that no longer exists (shell: and URL targets can't be checked).</summary>
    private static bool IsMissing(string? path)
        => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && !File.Exists(path) && !Directory.Exists(path);

    /// <summary>Text the Dock items filter searches: title, subtitle and the names inside folders.</summary>
    public string SearchText => Item.Kind == DockItemKind.Group
        ? $"{Title} {Subtitle} " + string.Join(" ", (Item.Children ?? new()).Select(c => new ItemRow(c).Title))
        : $"{Title} {Subtitle} {Item.Path}";

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
                // ignore
            }
        }
        return Path.GetFileNameWithoutExtension(path);
    }
}

public partial class SettingsWindow
{
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

    private void OnItemFilterChanged(object sender, TextChangedEventArgs e)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_rows);
        string query = ItemFilterBox.Text.Trim();
        view.Filter = query.Length == 0
            ? null
            : o => o is ItemRow row && row.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

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
        WidgetDetail.Visibility = item.Kind == DockItemKind.Widget && WidgetRegistry.Find(item.Widget) is not null ? Visibility.Visible : Visibility.Collapsed;
        GroupDetail.Visibility = item.Kind == DockItemKind.Group ? Visibility.Visible : Visibility.Collapsed;

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
                        Text = "This widget has no additional settings. Click or right-click the widget on the dock to use it.",
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

            case DockItemKind.Group:
                DetailSubtitle.Text = $"Folder containing {item.Children?.Count ?? 0} item(s). Click on the dock to expand, or drag items onto it.";
                GroupNameBox.Text = item.GroupName ?? "Folder";
                _suppressGroupAccent = true;
                GroupAccentCombo.ItemsSource = new[]
                {
                    new { Name = "Blue", Key = "AccentBlueBrush" },
                    new { Name = "Green", Key = "AccentGreenBrush" },
                    new { Name = "Orange", Key = "AccentOrangeBrush" },
                    new { Name = "Red", Key = "AccentRedBrush" },
                    new { Name = "Purple", Key = "AccentPurpleBrush" },
                    new { Name = "Cyan", Key = "AccentCyanBrush" },
                    new { Name = "Pink", Key = "AccentPinkBrush" },
                    new { Name = "Yellow", Key = "AccentYellowBrush" },
                };
                GroupAccentCombo.DisplayMemberPath = "Name";
                GroupAccentCombo.SelectedValuePath = "Key";
                GroupAccentCombo.SelectedValue = item.GroupAccent ?? "AccentBlueBrush";
                _suppressGroupAccent = false;
                break;

            default:
                DetailSubtitle.Text = "Visually groups items on the dock.";
                break;
        }
    }

    /// <summary>Live preview of the selected widget (shares the same settings).</summary>
    private void ShowDetailPreview(WidgetDescriptor descriptor, DockItem item)
    {
        try
        {
            // Copy item sharing the same settings object without persisting data
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
            Log.Error(ex, "Failed to create preview");
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

    private void OnGroupNameChanged(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ItemRow row) return;
        var name = GroupNameBox.Text.Trim();
        var value = string.IsNullOrEmpty(name) ? "Folder" : name;
        if (row.Item.GroupName == value) return;
        row.Item.GroupName = value;
        AppServices.ConfigService.ReplaceItems(_config.Items);
    }

    private void OnGroupNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnGroupNameChanged(sender, e);
    }

    private void OnGroupAccentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressGroupAccent || ItemList.SelectedItem is not ItemRow row) return;
        if (GroupAccentCombo.SelectedValue is string accent)
        {
            if (row.Item.GroupAccent == accent) return;
            row.Item.GroupAccent = accent;
            AppServices.ConfigService.ReplaceItems(_config.Items);
        }
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
        var newPins = DefaultItems.ImportTaskbarPins().Where(i => !existing.Contains(i.Path)).ToList();
        if (newPins.Count > 0)
        {
            AppServices.ConfigService.History.Push(_config, "Imported taskbar pins");
            int index = DockItemsIndex.EndOfApps();
            foreach (var item in newPins) _config.Items.Insert(index++, item);
            _config.NotifyItemsChanged();
        }
        ConfirmDialog.Show("Import taskbar pins",
            newPins.Count switch { 0 => "No new pins found to import.", 1 => "1 application added.", _ => $"{newPins.Count} applications added." },
            "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
    }

    private void OnRemoveItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ItemRow row)
        {
            if (row.Item.Kind == DockItemKind.Group)
                Dock.GroupItemView.ConfirmRemoveFolder(row.Item, this);
            else
                AppServices.ConfigService.RemoveItem(row.Item.Id);
        }
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e) => MoveRow(sender, -1);

    private void OnMoveDownClick(object sender, RoutedEventArgs e) => MoveRow(sender, +1);

    private void MoveRow(object sender, int delta)
    {
        if ((sender as FrameworkElement)?.DataContext is not ItemRow row) return;
        int from = _config.Items.IndexOf(row.Item);
        int to = Math.Clamp(from + delta, 0, _config.Items.Count - 1);
        if (from < 0 || from == to) return;
        AppServices.ConfigService.History.Push(_config, $"Moved {ConfigService.Describe(row.Item)}");
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
}
