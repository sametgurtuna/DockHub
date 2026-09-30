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
using ManagedShell.WindowsTasks;

namespace CustomDock.Dock;

public partial class DockWindow
{
    private void OnItemsChanged(object? sender, EventArgs e)
    {
        // Keys are cached per item id; a repaired or edited path must be matched again.
        _itemKeys.Clear();
        RebuildItems();
    }

    /// <summary>Widgets pinned to the fixed right edge render in <see cref="EndItemsPanel"/> instead of the scrollable center list.</summary>
    private static bool IsPinnedEnd(DockItem item) => item.Kind == DockItemKind.Widget && item.PinnedEnd;

    /// <summary>Displays whose own dock failed to open; their widgets stay on the main dock.</summary>
    private static readonly HashSet<string> s_failedDisplays = new(StringComparer.OrdinalIgnoreCase);

    public static void MarkDisplayFailed(string device, bool failed)
    {
        if (failed) s_failedDisplays.Add(device);
        else s_failedDisplays.Remove(device);
    }

    /// <summary>
    /// Displays that have, or are about to get, a dock of their own: the same set the app opens docks for (every
    /// connected display but the main one, while "Show on all displays" is on), minus docks that failed to open.
    /// Deciding from this set rather than from the docks already open means the main dock never builds, even
    /// briefly, a widget that belongs to a display whose dock is still starting.
    /// </summary>
    private IReadOnlyCollection<string> SecondaryDisplays()
    {
        if (!_config.ShowOnAllDisplays) return Array.Empty<string>();
        string main = MonitorHelper.GetPreferred(_config.MonitorDevice).DeviceName;
        return MonitorHelper.GetAll()
            .Select(m => m.DeviceName)
            .Where(d => !string.Equals(d, main, StringComparison.OrdinalIgnoreCase) && !s_failedDisplays.Contains(d))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyCollection<string> _secondaryDisplays = Array.Empty<string>();

    /// <summary>The top bar's window failed to open; its widgets stay on the main dock.</summary>
    private static bool s_barFailed;

    public static void MarkBarFailed(bool failed) => s_barFailed = failed;

    /// <summary>Whether the top bar's widgets are on the bar: it is on and its window didn't fail to open.</summary>
    public static bool BarShown => AppServices.Config.TopBar.Enabled && !s_barFailed;

    /// <summary>Whether this dock shows the item (see <see cref="WidgetPlacement"/>).</summary>
    private bool ShowsHere(DockItem item)
        => WidgetPlacement.ShowsOn(item.Kind, item.Surface, item.Display, _surface.Role, _secondaryDevice, _secondaryDisplays, _config.TopBar.Enabled && !s_barFailed);

    private void RebuildItems()
    {
        var items = _config.Items;
        bool vertical = IsVertical;
        ClearEditDecorations();
        var positionsBefore = CapturePositions();
        _secondaryDisplays = SecondaryDisplays();

        // Items that were removed, or widgets that moved to another display's dock.
        var kept = items.Where(ShowsHere).Select(i => i.Id).ToHashSet();
        foreach (var id in _itemViews.Keys.Where(id => !kept.Contains(id)).ToList())
        {
            DisposeView(_itemViews[id]);
            _itemViews.Remove(id);
            _itemKeys.Remove(id);
        }

        ItemsPanel.Children.Clear();
        EndItemsPanel.Children.Clear();
        foreach (var item in items)
        {
            if (!ShowsHere(item)) continue;
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
            (IsPinnedEnd(item) ? EndItemsPanel : ItemsPanel).Children.Add(view);
        }

        if (_editing)
        {
            // The "+" tile of edit mode follows the dock's own items, before the running apps.
            AddTile.Margin = vertical ? new Thickness(0, 3, 0, 3) : new Thickness(3, 0, 3, 0);
            ItemsPanel.Children.Add(AddTile);
        }

        _runningSeparator.SetOrientation(vertical);
        RefreshRunningApps();
        if (_editing) ApplyEditDecorations();
        UpdateWidgetDividers();
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
                    DockDragHelper.Attach(app, () => DockDragHelper.StringData(DockDragHelper.ItemFormat, item.Id));
                    return app;
                // Widgets keep their own state (timers, notes, alarms); a second copy would diverge, so each lives on one dock.
                case DockItemKind.Widget when !ShowsHere(item):
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
                    DockDragHelper.Attach(groupView, () => DockDragHelper.StringData(DockDragHelper.ItemFormat, item.Id));
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

    /// <summary>Whether every window has a button of its own (buttons not combined). The top bar has no apps.</summary>
    private bool SplitWindows => _config.CombineButtons == CombineButtons.Never && !IsBar;

    /// <summary>
    /// The dock's app buttons (see <see cref="TaskbarButtons"/>): the pinned apps with their windows, then the running
    /// apps that aren't pinned (with "Show unpinned running apps"; with docks on several displays, those on this one).
    /// </summary>
    private List<TaskbarButton<ApplicationWindow>> ButtonLayout()
    {
        var pins = _config.Items.Where(i => i.Kind == DockItemKind.App).Select(i => (i.Id, KeyFor(i))).ToList();
        var folderKeys = _config.Items.Where(i => i.Kind == DockItemKind.Group)
            .SelectMany(g => g.Children ?? new()).Where(c => c.Kind == DockItemKind.App).Select(KeyFor).ToHashSet();
        var mode = SplitWindows ? CombineButtons.Never : CombineButtons.Always;
        bool byDisplay = FilterRunningByDisplay;
        // Not combined, each window has its button on the dock of its own display.
        var apps = _shell.RunningApps.Groups.Select(g => new RunningApp<ApplicationWindow>(g.Key, g.Order,
            mode == CombineButtons.Never && byDisplay ? g.Windows.Where(IsWindowOnThisDisplay).ToList() : g.Windows.ToList())).ToList();
        bool tail = _config.ShowRunningApps && !IsBar;
        return TaskbarButtons.Layout(pins, folderKeys, apps, mode)
            .Where(b => b.Slot != TaskbarSlot.Running
                        || (tail && (mode == CombineButtons.Never || !byDisplay || IsOnThisDisplay(_shell.RunningApps.Find(b.Key)))))
            .ToList();
    }

    /// <summary>
    /// Binds the running apps to the pinned buttons, puts a pinned app's other windows right after it (buttons not
    /// combined) and the other running apps at the end.
    /// </summary>
    private void RefreshRunningApps()
    {
        if (_closing) return;
        var layout = ButtonLayout();

        // Apps inside folders count as pinned too; the folder shows that they are running.
        foreach (var folder in _config.Items.Where(i => i.Kind == DockItemKind.Group))
        {
            bool running = (folder.Children ?? new()).Where(c => c.Kind == DockItemKind.App)
                .Any(child => _shell.RunningApps.Find(KeyFor(child)) is { WindowCount: > 0 });
            if (_itemViews.TryGetValue(folder.Id, out var view) && view is GroupItemView groupView)
                groupView.SetRunning(running);
        }

        bool vertical = IsVertical;
        foreach (var b in layout.Where(b => b.Slot == TaskbarSlot.Pinned))
        {
            if (!_itemViews.TryGetValue(b.PinId!, out var view) || view is not AppButton pinned) continue;
            pinned.Group = _shell.RunningApps.Find(b.Key);
            pinned.Window = b.Window;
            pinned.TitleAllowed = !vertical;
        }

        // A pinned app's other windows stay out of edit mode, which is about the dock's own items.
        var extras = layout.Where(b => b.Slot == TaskbarSlot.PinnedWindow && !_editing).ToList();
        var tail = layout.Where(b => b.Slot == TaskbarSlot.Running).ToList();
        _runningSignature = Signature(layout);

        var wanted = extras.Concat(tail).Select(ViewKey).ToHashSet();
        foreach (var key in _runningViews.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _runningViews[key].Detach();
            _runningViews.Remove(key);
        }

        // Everything but the dock's own items and edit mode's "+" tile is placed again.
        var itemViews = _itemViews.Values.ToHashSet();
        for (int i = ItemsPanel.Children.Count - 1; i >= 0; i--)
        {
            var child = ItemsPanel.Children[i];
            if (!ReferenceEquals(child, _addTile) && !(child is FrameworkElement element && itemViews.Contains(element)))
                ItemsPanel.Children.RemoveAt(i);
        }

        string? previousPin = null;
        UIElement? previous = null;
        foreach (var b in extras)
        {
            if (b.PinId != previousPin)
            {
                previousPin = b.PinId;
                previous = _itemViews.GetValueOrDefault(b.PinId!);
            }
            int at = previous is null ? -1 : ItemsPanel.Children.IndexOf(previous);
            if (at < 0) continue;
            var button = RunningView(b, vertical, out _);
            ItemsPanel.Children.Insert(at + 1, button);
            previous = button;
        }

        if (tail.Count == 0) return;
        if (ItemsPanel.Children.Count > 0) ItemsPanel.Children.Add(_runningSeparator);
        foreach (var b in tail)
        {
            var button = RunningView(b, vertical, out bool isNew);
            ItemsPanel.Children.Add(button);
            if (isNew && DateTime.UtcNow > _startedAt + TimeSpan.FromSeconds(5)) HintIfOutOfView(button);
        }
    }

    /// <summary>The button of a running app, one of its windows, or one more window of a pinned app (kept while it lasts).</summary>
    private AppButton RunningView(TaskbarButton<ApplicationWindow> model, bool vertical, out bool isNew)
    {
        string key = ViewKey(model);
        var group = _shell.RunningApps.Find(model.Key);
        isNew = !_runningViews.TryGetValue(key, out var button);
        if (button is null)
        {
            button = new AppButton(null, group) { Window = model.Window };
            if (model.Slot == TaskbarSlot.Running)
            {
                // Dragged onto the dock, a running app is pinned (a pinned app's window isn't: the app already is).
                string appKey = model.Key;
                DockDragHelper.Attach(button, () => DockDragHelper.StringData(DockDragHelper.RunningAppFormat, appKey));
            }
            else
            {
                button.PinnedBy = _config.Items.FirstOrDefault(i => i.Id == model.PinId);
            }
            _runningViews[key] = button;
        }
        button.Group = group;
        button.TitleAllowed = !vertical;
        button.Margin = vertical ? new Thickness(0, 1, 0, 1) : new Thickness(1, 0, 1, 0);
        button.Refresh();
        return button;
    }

    private static string ViewKey(TaskbarButton<ApplicationWindow> model) => model switch
    {
        { Slot: TaskbarSlot.PinnedWindow, Window: { } window } => $"pin:{model.PinId}#{window.Handle}",
        { Window: { } window } => $"{model.Key}#{window.Handle}",
        _ => model.Key,
    };

    /// <summary>With docks on several displays each one only lists the apps whose windows are on its display.</summary>
    private bool FilterRunningByDisplay => _config.ShowOnAllDisplays && _config.RunningAppsOnOwnDisplay && s_docks.Count(d => !d.IsBar) > 1;

    private bool IsOnThisDisplay(AppGroup? group)
    {
        if (group is null) return false;
        if (group.Windows.Count == 0) return IsMain;
        return group.Windows.Any(IsWindowOnThisDisplay);
    }

    private bool IsWindowOnThisDisplay(ApplicationWindow window)
    {
        // Minimized windows report the display they were restored on.
        var monitor = NativeMethods.MonitorFromWindow(window.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == _monitor.Handle) return true;
        // A display handle may be stale after a display change; fall back to the device name.
        return monitor != IntPtr.Zero && MonitorHelper.TryGet(monitor) is { } info &&
               string.Equals(info.DeviceName, _monitor.DeviceName, StringComparison.OrdinalIgnoreCase);
    }

    private static string Signature(IEnumerable<TaskbarButton<ApplicationWindow>> layout)
        => string.Join("|", layout.Select(b => b.Slot == TaskbarSlot.Pinned ? $"{b.PinId}={b.Window?.Handle}" : ViewKey(b)));

    /// <summary>Rebuilds the app buttons when a window moved to or from this display.</summary>
    private void RefreshRunningAppsIfMoved()
    {
        if (_closing || !FilterRunningByDisplay) return;
        if (Signature(ButtonLayout()) != _runningSignature)
            RefreshRunningApps();
    }

    /// <summary>A window opened or closed in a running app: only buttons that aren't combined change.</summary>
    private void OnRunningWindowsChanged()
    {
        if (SplitWindows) RefreshRunningApps();
    }

    private bool _dividersQueued;

    /// <summary>
    /// Seamless widget style: a thin line between two widgets that stand next to each other (in either strip). Widgets
    /// that hide or show themselves (nothing playing) move the lines along.
    /// </summary>
    private void UpdateWidgetDividers()
    {
        bool seamless = _config.WidgetStyle == WidgetStyle.Seamless;
        foreach (var panel in new[] { ItemsPanel, EndItemsPanel })
        {
            // Along the panel: the right-edge panel stays a row even on a side dock.
            var divider = panel.Orientation == Orientation.Vertical ? CardDivider.Top : CardDivider.Left;
            FrameworkElement? previous = null;
            foreach (var child in panel.Children.OfType<FrameworkElement>())
            {
                if (child.Visibility != Visibility.Visible) continue;
                if (child is WidgetItemView widget)
                {
                    widget.IsVisibleChanged -= OnWidgetVisibilityChanged;
                    widget.IsVisibleChanged += OnWidgetVisibilityChanged;
                    widget.Divider = seamless && previous is WidgetItemView ? divider : CardDivider.None;
                }
                previous = child;
            }
            // Hidden widgets keep no line of their own.
            foreach (var hidden in panel.Children.OfType<WidgetItemView>().Where(v => v.Visibility != Visibility.Visible))
            {
                hidden.IsVisibleChanged -= OnWidgetVisibilityChanged;
                hidden.IsVisibleChanged += OnWidgetVisibilityChanged;
                hidden.Divider = CardDivider.None;
            }
        }
    }

    private void OnWidgetVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_dividersQueued || _closing) return;
        _dividersQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _dividersQueued = false;
            UpdateWidgetDividers();
        });
    }

    private int PinnedViewCount => _config.Items.Count(i => !IsPinnedEnd(i) && _itemViews.ContainsKey(i.Id));

    /// <summary>
    /// Insertion index in <see cref="AppConfig.Items"/> for a pointer position. Views can be missing for some items
    /// (widgets on secondary docks, items that failed to load), so panel positions are mapped back to config indices.
    /// </summary>
    private int DropIndexAt(Point panelPoint, out double caret)
    {
        bool vertical = IsVertical;
        int index = 0;
        caret = 0;
        double lastEnd = 0;
        var itemViews = _itemViews.Values.ToHashSet();

        int position = 0;
        foreach (FrameworkElement child in ItemsPanel.Children)
        {
            // The dock's own items come first; edit mode's "+" tile and the running apps follow them.
            if (ReferenceEquals(child, _addTile) || ReferenceEquals(child, _runningSeparator)) break;
            bool isItem = itemViews.Contains(child);
            // A pinned app's other windows (buttons not combined) go with it; running apps are no drop places.
            if (!isItem && child is not AppButton { PinnedBy: not null }) continue;
            int fallback = isItem ? position++ : position;
            if (child.Visibility != Visibility.Visible) continue;
            var topLeft = child.TranslatePoint(new Point(0, 0), ItemsPanel);
            double start = vertical ? topLeft.Y : topLeft.X;
            double length = vertical ? child.ActualHeight : child.ActualWidth;
            double pointer = vertical ? panelPoint.Y : panelPoint.X;
            lastEnd = start + length;
            if (!isItem) continue;
            int configIndex = ConfigIndexOf(child, fallback);

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
        data.GetDataPresent(DockDragHelper.NewWidgetFormat) ||
        data.GetDataPresent(DataFormats.FileDrop);

    /// <summary>A new widget dragged from the gallery, for this dock (on another display's dock it belongs to that display).</summary>
    private DockItem? NewWidgetFrom(IDataObject data)
    {
        if (DockDragHelper.NewWidgetItem(data) is not { } item) return null;
        PlaceHere(item);
        return item;
    }

    /// <summary>A widget added on this dock belongs to it: the top bar, or this display's dock.</summary>
    private void PlaceHere(DockItem widget)
    {
        widget.Surface = IsBar ? DockItem.BarSurface : null;
        widget.Display = IsBar ? null : _secondaryDevice;
    }

    /// <summary>The top bar takes widgets only (moved here, or new from the gallery).</summary>
    private static bool IsWidgetData(IDataObject data) =>
        data.GetDataPresent(DockDragHelper.NewWidgetFormat)
        || (DockDragHelper.ReadString(data, DockDragHelper.ItemFormat) is { } id && AppServices.ConfigService.FindItem(id) is { Kind: DockItemKind.Widget });

    private void OnItemsDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!HasDockData(e.Data) || (IsBar && !IsWidgetData(e.Data)))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        if (!_shown) Reveal();
        UpdateEdgeScroll(e.GetPosition(Scroller)); // dragging to a faded edge scrolls toward the hidden items
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

    private void OnItemsDragLeave(object sender, DragEventArgs e)
    {
        DropCaret.Visibility = Visibility.Collapsed;
        StopEdgeScroll();
    }

    private void OnItemsDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        DropCaret.Visibility = Visibility.Collapsed;
        StopEdgeScroll();
        if (IsBar && !IsWidgetData(e.Data)) return; // apps and files belong on the dock
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
                     AppLauncher.PinItem(runGroup) is { } runItem)
            {
                config.AddToGroup(targetGroup.Item.Id, runItem);
                targetGroup.RefreshAppearance();
                return;
            }
            else if (NewWidgetFrom(e.Data) is { } newWidget)
            {
                config.AddToGroup(targetGroup.Item.Id, newWidget);
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
            var dragged = config.FindItem(itemId);
            // Dropping a right-pinned widget back into the scrollable list unpins it.
            bool unpin = dragged is { PinnedEnd: true };
            // A new place (this dock, the bar or this display) and unpinning are one undo step with the move.
            using (dragged is not null && (unpin || Adopts(itemId)) ? config.History.Batch(_config, L.T("Moved {0}", ConfigService.Describe(dragged))) : null)
            {
                bool changed = AdoptWidget(itemId);
                if (unpin)
                {
                    dragged!.PinnedEnd = false;
                    changed = true;
                }
                config.MoveItem(itemId, index);
                // The move alone raises nothing when the position stays the same.
                if (changed) _config.NotifyItemsChanged();
            }
        }
        else if (e.Data.GetData(DockDragHelper.RunningAppFormat) is string key &&
                 _shell.RunningApps.Find(key) is { } group &&
                 AppLauncher.PinItem(group) is { } pinItem)
        {
            config.AddItem(pinItem, index);
        }
        else if (NewWidgetFrom(e.Data) is { } widget)
        {
            config.AddItem(widget, index);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files.Where(f => !string.IsNullOrWhiteSpace(f)))
                config.AddItem(DockItem.App(file), index++);
        }
    }

    private static bool HasPinnableWidget(IDataObject data) =>
        data.GetDataPresent(DockDragHelper.NewWidgetFormat) ||
        (data.GetDataPresent(DockDragHelper.ItemFormat) &&
         data.GetData(DockDragHelper.ItemFormat) is string id &&
         AppServices.ConfigService.FindItem(id) is { Kind: DockItemKind.Widget });

    private void OnEndItemsDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!_shown) Reveal();
        e.Effects = HasPinnableWidget(e.Data) ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void OnEndItemsDragLeave(object sender, DragEventArgs e) => e.Handled = true;

    /// <summary>Dropping a widget anywhere in the end zone (tray/clock area) pins it to the fixed right edge.</summary>
    private void OnEndItemsDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (NewWidgetFrom(e.Data) is { } newWidget)
        {
            newWidget.PinnedEnd = true;
            AppServices.ConfigService.AddItem(newWidget);
            return;
        }
        if (e.Data.GetData(DockDragHelper.ItemFormat) is not string itemId) return;
        if (AppServices.ConfigService.FindItem(itemId) is not { Kind: DockItemKind.Widget } item) return;
        if (item.PinnedEnd && !Adopts(itemId)) return;

        using (AppServices.ConfigService.History.Batch(_config, L.T("Moved {0}", ConfigService.Describe(item))))
        {
            AdoptWidget(itemId);
            item.PinnedEnd = true;
        }
        _config.NotifyItemsChanged();
    }

    /// <summary>
    /// A widget dragged here from another display's dock moves to this dock's display. A widget already shown here
    /// keeps its display (it may be waiting here for a disconnected one).
    /// </summary>
    private bool AdoptWidget(string itemId)
    {
        if (!Adopts(itemId)) return false;
        PlaceHere(AppServices.ConfigService.FindItem(itemId)!);
        AppServices.ConfigService.ScheduleSave();
        return true;
    }

    /// <summary>Whether a widget dropped here comes from elsewhere: the bar, the dock, or another display's dock.</summary>
    private bool Adopts(string itemId)
    {
        if (_itemViews.ContainsKey(itemId)) return false;
        if (AppServices.ConfigService.FindItem(itemId) is not { Kind: DockItemKind.Widget } widget) return false;
        bool onBar = WidgetDisplays.IsOnBar(widget);
        return onBar != IsBar || (!IsBar && !string.Equals(widget.Display, _secondaryDevice, StringComparison.OrdinalIgnoreCase));
    }
}
