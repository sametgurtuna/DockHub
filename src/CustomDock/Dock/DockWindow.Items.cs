using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Services;
using CustomDock.Shell;
using CustomDock.Widgets;

namespace CustomDock.Dock;

public partial class DockWindow
{
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
                case GroupItemView groupView:
                    groupView.Margin = vertical ? new Thickness(0, 1, 0, 1) : new Thickness(1, 0, 1, 0);
                    groupView.RefreshIcons();
                    break;
            }
            ItemsPanel.Children.Add(view);
        }

        _runningSeparator.SetOrientation(vertical);
        RefreshRunningApps();
        AnimateShifts(positionsBefore);
    }

    /// <summary>Records positions of existing items within ItemsPanel prior to reordering/adding/removing.</summary>
    private Dictionary<FrameworkElement, Point> CapturePositions()
    {
        var positions = new Dictionary<FrameworkElement, Point>();
        foreach (FrameworkElement element in ItemsPanel.Children)
        {
            try { positions[element] = element.TranslatePoint(new Point(0, 0), ItemsPanel); }
            catch (InvalidOperationException) { /* not yet laid out */ }
        }
        return positions;
    }

    /// <summary>
    /// Slides items that changed positions (same instance, different order) from old to new position;
    /// newly added items (via Motion.Appear) already have their own entrance animation and are skipped here.
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
                // Widgets keep their own state (timers, notes, alarms); a second copy would diverge, so they stay on the main dock.
                case DockItemKind.Widget when !IsMain:
                    return null;
                case DockItemKind.Widget when WidgetRegistry.Find(item.Widget) is { } descriptor:
                    var widget = descriptor.Create(item);
                    var view = new WidgetItemView(item, widget, this);
                    view.SetCompact(IsVertical);
                    widget.Attach(this);
                    return view;
                case DockItemKind.Separator:
                    return new SeparatorView(item, IsVertical);
                case DockItemKind.Group:
                    var groupView = new GroupItemView(item, this);
                    DockDragHelper.Attach(groupView, () => new DataObject(DockDragHelper.ItemFormat, item.Id));
                    return groupView;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to create dock item: {item.Kind} {item.Widget ?? item.Path}");
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
            case GroupItemView group:
                group.Detach();
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

    /// <summary>Binds window groups to pinned buttons; appends unpinned running apps to the end.</summary>
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

        var unpinned = UnpinnedRunningGroups(pinnedKeys);
        _runningSignature = Signature(unpinned);

        foreach (var key in _runningViews.Keys.Except(unpinned.Select(g => g.Key)).ToList())
        {
            _runningViews[key].Detach();
            _runningViews.Remove(key);
        }

        // Rebuild the tail section (separator + running apps)
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

    /// <summary>With docks on several displays each one only lists the apps whose windows are on its display.</summary>
    private bool FilterRunningByDisplay => _config.ShowOnAllDisplays && _config.RunningAppsOnOwnDisplay && s_docks.Count > 1;

    private List<AppGroup> UnpinnedRunningGroups(HashSet<string> pinnedKeys)
    {
        if (!_config.ShowRunningApps) return new List<AppGroup>();
        var groups = _shell.RunningApps.Groups.Where(g => !pinnedKeys.Contains(g.Key));
        if (FilterRunningByDisplay) groups = groups.Where(IsOnThisDisplay);
        return groups.OrderBy(g => g.Order).ToList();
    }

    private bool IsOnThisDisplay(AppGroup group)
    {
        if (group.Windows.Count == 0) return IsMain;
        foreach (var window in group.Windows)
        {
            // Minimized windows report the display they were restored on.
            var monitor = NativeMethods.MonitorFromWindow(window.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == _monitor.Handle) return true;
            // A display handle may be stale after a display change; fall back to the device name.
            if (monitor != IntPtr.Zero && MonitorHelper.TryGet(monitor) is { } info &&
                string.Equals(info.DeviceName, _monitor.DeviceName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string Signature(List<AppGroup> groups) => string.Join("|", groups.Select(g => g.Key));

    /// <summary>Rebuilds running apps when a window moved to or from this display.</summary>
    private void RefreshRunningAppsIfMoved()
    {
        if (_closing || !FilterRunningByDisplay) return;
        var pinnedKeys = _config.Items.Where(i => i.Kind == DockItemKind.App).Select(KeyFor).ToHashSet();
        if (Signature(UnpinnedRunningGroups(pinnedKeys)) != _runningSignature)
            RefreshRunningApps();
    }

    private int PinnedViewCount => _config.Items.Count(i => _itemViews.ContainsKey(i.Id));

    /// <summary>
    /// Insertion index in <see cref="AppConfig.Items"/> for a pointer position. Views can be missing for some items
    /// (widgets on secondary docks, items that failed to load), so panel positions are mapped back to config indices.
    /// </summary>
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
            int configIndex = ConfigIndexOf(child, i);

            if (pointer < start + length / 2)
            {
                caret = start;
                return configIndex;
            }
            index = configIndex + 1;
        }
        caret = lastEnd;
        return index;
    }

    private int ConfigIndexOf(FrameworkElement view, int fallback)
    {
        foreach (var (id, candidate) in _itemViews)
        {
            if (!ReferenceEquals(candidate, view)) continue;
            int index = _config.Items.FindIndex(i => i.Id == id);
            return index >= 0 ? index : fallback;
        }
        return fallback;
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
        var config = AppServices.ConfigService;

        // Check if dropped directly onto a group folder
        var hit = VisualTreeHelper.HitTest(ItemsPanel, e.GetPosition(ItemsPanel))?.VisualHit;
        var targetGroup = FindAncestor<GroupItemView>(hit);
        if (targetGroup is not null)
        {
            if (e.Data.GetData(DockDragHelper.ItemFormat) is string dropId)
            {
                if (dropId != targetGroup.Item.Id)
                {
                    var existing = config.FindItem(dropId);
                    if (existing is not null && existing.Kind != DockItemKind.Group)
                    {
                        config.RemoveItem(dropId);
                        config.AddToGroup(targetGroup.Item.Id, existing);
                        targetGroup.RefreshAppearance();
                        return;
                    }
                }
            }
            else if (e.Data.GetData(DockDragHelper.RunningAppFormat) is string runKey &&
                     _shell.RunningApps.Find(runKey) is { } runGroup &&
                     AppLauncher.PinnablePath(runGroup) is { } runPath)
            {
                config.AddToGroup(targetGroup.Item.Id, DockItem.App(runPath, runGroup.Title));
                targetGroup.RefreshAppearance();
                return;
            }
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] dropFiles)
            {
                foreach (var file in dropFiles.Where(f => !string.IsNullOrWhiteSpace(f)))
                    config.AddToGroup(targetGroup.Item.Id, DockItem.App(file));
                targetGroup.RefreshAppearance();
                return;
            }
        }

        int index = DropIndexAt(e.GetPosition(ItemsPanel), out _);

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
}
