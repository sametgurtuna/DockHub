using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Widgets;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Dock;

/// <summary>
/// Edit mode (see <see cref="DockEditMode"/>): the dock's items wiggle and get a remove badge, widgets a handle that
/// switches them to a narrower or wider layout, a "+" tile adds widgets, and a bar next to the dock has the Done
/// button. Keys: arrows move the focus, Ctrl+arrows move the item, Delete removes it, + and - resize it, Esc or Enter
/// finish. Clicking outside DockHub, or on another dock, finishes too.
/// </summary>
public partial class DockWindow : IEditableDock
{
    private readonly List<(FrameworkElement View, AdornerLayer Layer, EditAdorner Adorner)> _editDecorations = new();
    private FrameworkElement? _addTile;
    private Popup? _editBar;
    private Popup? _addPicker;
    private IDisposable? _outsideClicks;
    private bool _editing;
    private DockItem? _editFocus;
    private IntPtr _windowBeforeEdit;
    private bool _editBarPlacementQueued;
    private DateTime _addPickerClosedAt;
    /// <summary>Per item: the layout it last had in each width rank, so resizing back returns to it.</summary>
    private readonly Dictionary<string, Dictionary<int, string>> _resizeMemory = new();

    /// <summary>True while this dock is in edit mode.</summary>
    public bool IsEditing => _editing;

    /// <summary>Whether <paramref name="element"/> is on a dock in edit mode, where a click picks an item up instead of opening it.</summary>
    internal static bool IsEditingAt(DependencyObject element) => Window.GetWindow(element) is DockWindow { IsEditing: true };

    /// <summary>Starts edit mode on this dock; from the keyboard the first item gets the focus ring.</summary>
    public void EnterEditMode(bool fromKeyboard = false)
    {
        if (_closing || _hwnd == IntPtr.Zero) return;
        DockEditMode.Enter(this);
        if (fromKeyboard && _editing && EditableItems() is { Count: > 0 } items) MoveEditFocus(items[0]);
    }

    /// <summary>Global shortcut and Settings › Dock items: edits the main dock (or finishes when it is being edited).</summary>
    public static void ToggleEditMainDock()
    {
        if (s_docks.FirstOrDefault(d => d.IsMain) is not { } dock) return;
        if (dock.IsEditing) DockEditMode.Exit();
        else dock.EnterEditMode(fromKeyboard: true);
    }

    /// <summary>Settings › Dock items › Edit on the dock.</summary>
    public static void EditMainDock() => s_docks.FirstOrDefault(d => d.IsMain)?.EnterEditMode();

    void IEditableDock.BeginEditing()
    {
        // Esc or Done gives the focus back to the window that had it (the one keyboard mode came from, if it was on).
        _windowBeforeEdit = IsKeyboardMode ? _windowBeforeKeyboard : GetForegroundWindow();
        ExitKeyboardMode(restoreWindow: false);
        WindowPreviewWindow.Instance.HidePreview();
        foreach (var menu in _openMenus.ToList()) menu.IsOpen = false;
        foreach (var group in _itemViews.Values.OfType<GroupItemView>()) group.CloseFan();

        _editing = true;
        _editFocus = null;
        _resizeMemory.Clear();
        Reveal();
        BeginInteraction();
        if (_magnifier is not null) _magnifier.Enabled = false;
        DimSystemItems(true);
        RebuildItems(); // adds the "+" tile and the decorations
        ShowEditBar();

        // Keys need the (normally non-activating) dock to take the focus.
        ActivateForInput();
        Focus();
        PreviewKeyDown -= OnEditModeKey;
        PreviewKeyDown += OnEditModeKey;
        PreviewMouseDown -= OnEditModeMouseDown;
        PreviewMouseDown += OnEditModeMouseDown;
        LocationChanged -= OnEditLayoutChanged;
        LocationChanged += OnEditLayoutChanged;
        Root.SizeChanged -= OnEditLayoutChanged;
        Root.SizeChanged += OnEditLayoutChanged;
        _outsideClicks = GlobalPopupDismissHook.WatchOutsideClicks(IsInsideEditing, () =>
        {
            if (_editing) DockEditMode.Exit();
        });
        Log.Info("Dock edit mode started.");
    }

    void IEditableDock.EndEditing()
    {
        _editing = false;
        _editFocus = null;
        _resizeMemory.Clear();
        PreviewKeyDown -= OnEditModeKey;
        PreviewMouseDown -= OnEditModeMouseDown;
        LocationChanged -= OnEditLayoutChanged;
        Root.SizeChanged -= OnEditLayoutChanged;
        _outsideClicks?.Dispose();
        _outsideClicks = null;
        CloseAddPicker();
        if (_editBar is not null) _editBar.IsOpen = false;
        HideFocusRing();
        foreach (var view in _itemViews.Values.OfType<WidgetItemView>()) view.SetEditing(false);
        DimSystemItems(false);
        if (_magnifier is not null) _magnifier.Enabled = true;
        if (_closing) ClearEditDecorations(); // stops the endless wiggle animations
        else RebuildItems(); // removes the tile and the decorations
        AppServices.ConfigService.ScheduleSave();
        EndInteraction();

        // Finished with Esc, Enter or Done (the dock still has the focus): the previous window gets it back. A click
        // outside has already given it to another window.
        if (!_closing && IsActive && _windowBeforeEdit != IntPtr.Zero && _windowBeforeEdit != _hwnd && IsWindow(_windowBeforeEdit))
            SetForegroundWindow(_windowBeforeEdit);
        _windowBeforeEdit = IntPtr.Zero;
        Log.Info("Dock edit mode ended.");
    }

    /// <summary>
    /// Clicks on this dock and on DockHub's dialogs, menus and settings keep edit mode; anything else, another dock
    /// included, ends it.
    /// </summary>
    private bool IsInsideEditing(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (hwnd == _hwnd) return true;
        if (s_docks.Any(d => d._hwnd == hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == _processId;
    }

    /// <summary>
    /// The dock gives the focus up when another window is clicked (settings, the app picker); a click back on it takes
    /// the focus again, so the edit keys keep working.
    /// </summary>
    private void OnEditModeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_editing && !IsActive) ActivateForInput();
    }

    /// <summary>The Done bar is a popup, which doesn't follow the dock when it moves or changes size.</summary>
    private void OnEditLayoutChanged(object? sender, EventArgs e)
    {
        if (!_editing || _editBarPlacementQueued) return;
        _editBarPlacementQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            _editBarPlacementQueued = false;
            PlaceEditBar();
        });
    }

    /// <summary>Start, search, tray and clock stay where they are; they fade while the items are edited.</summary>
    private void DimSystemItems(bool dim)
    {
        // The scroll arrows keep working: the "+" tile and later items may be out of view.
        var scrollArrows = new[] { ScrollBackButton, ScrollForwardButton.Parent as UIElement ?? ScrollForwardButton };
        foreach (var element in StartZone.Children.OfType<UIElement>()
                     .Concat(EndZone.Children.OfType<UIElement>().Where(e => !ReferenceEquals(e, EndItemsPanel)))
                     .Where(e => !scrollArrows.Contains(e)))
        {
            if (dim)
            {
                element.Opacity = 0.35;
                element.IsHitTestVisible = false;
            }
            else
            {
                element.ClearValue(OpacityProperty);
                element.ClearValue(IsHitTestVisibleProperty);
            }
        }
    }

    // ------------------------------------------------------------------ Items

    private static DockItem? ItemOf(FrameworkElement view) => view switch
    {
        WidgetItemView widget => widget.Item,
        AppButton app => app.Item,
        GroupItemView group => group.Item,
        SeparatorView separator => separator.Item,
        _ => null,
    };

    /// <summary>Pinned apps, folders, widgets and separators of this dock, in the order they are shown.</summary>
    private List<FrameworkElement> EditableItems()
    {
        var items = new List<FrameworkElement>();
        int pinned = Math.Min(PinnedViewCount, ItemsPanel.Children.Count);
        for (int i = 0; i < pinned; i++)
            if (ItemsPanel.Children[i] is FrameworkElement view && IsEditable(view)) items.Add(view);
        foreach (var view in EndItemsPanel.Children.OfType<FrameworkElement>())
            if (IsEditable(view)) items.Add(view);
        return items;
    }

    private static bool IsEditable(FrameworkElement view)
        => view.Visibility == Visibility.Visible && ItemOf(view) is not null;

    /// <summary>Called by <see cref="RebuildItems"/> (after it cleared the old ones) while editing: badges, handles and wiggle on every item.</summary>
    private void ApplyEditDecorations()
    {
        // First, so that widgets hiding themselves (nothing playing) show up as tiles and get decorations too.
        foreach (var widgetView in _itemViews.Values.OfType<WidgetItemView>()) widgetView.SetEditing(true);

        foreach (var view in EditableItems())
        {
            if (ItemOf(view) is not { } item) continue;
            Func<int, bool>? resize = null;
            // Tiles (on a side dock, or collapsed while idle) have one size only.
            if (view is WidgetItemView widget && !widget.Widget.IsCompact
                && WidgetResize.CanResize(widget.Widget.Descriptor.Variants, widget.Widget.Variant))
                resize = direction => ResizeEditItem(widget, direction);
            if (AdornerLayer.GetAdornerLayer(view) is not { } layer) continue;
            var adorner = new EditAdorner(view, ConfigService.Describe(item), () => RemoveEditItem(view), resize);
            layer.Add(adorner);
            _editDecorations.Add((view, layer, adorner));
            Motion.Wiggle(view, true);
            AutomationProperties.SetItemStatus(view, L.T("Editing"));
        }

        if (_editFocus is not null)
        {
            if (EditableItems().FirstOrDefault(v => ReferenceEquals(ItemOf(v), _editFocus)) is { } focused) MoveEditFocus(focused);
            else
            {
                _editFocus = null;
                HideFocusRing();
            }
        }
    }

    private void ClearEditDecorations()
    {
        foreach (var (view, layer, adorner) in _editDecorations)
        {
            layer.Remove(adorner);
            Motion.Wiggle(view, false);
            view.ClearValue(AutomationProperties.ItemStatusProperty);
        }
        _editDecorations.Clear();
    }

    private void RemoveEditItem(FrameworkElement view)
    {
        if (ItemOf(view) is not { } item) return;
        // The focus moves on to the neighbour of the removed item.
        var items = EditableItems();
        int index = items.IndexOf(view);
        if (ReferenceEquals(_editFocus, item))
            _editFocus = index + 1 < items.Count ? ItemOf(items[index + 1]) : index > 0 ? ItemOf(items[index - 1]) : null;

        if (view is GroupItemView)
        {
            // A folder with items asks what to do with them; the dialog takes the focus, so it comes back afterwards.
            GroupItemView.ConfirmRemoveFolder(item, this);
            if (_editing) ActivateForInput();
        }
        else
        {
            AppServices.ConfigService.RemoveItem(item.Id);
        }
    }

    private bool ResizeEditItem(WidgetItemView view, int direction)
    {
        if (!_resizeMemory.TryGetValue(view.Item.Id, out var memory)) _resizeMemory[view.Item.Id] = memory = new();
        var variants = view.Widget.Descriptor.Variants;
        string current = view.Widget.Variant;
        if (WidgetResize.Next(variants, current, direction, memory) is not { } next) return false;
        memory[WidgetResize.RankOf(variants, current)] = current;
        view.Item.Variant = next;
        AppServices.ConfigService.ScheduleSave();
        return true;
    }

    /// <summary>Moves the item one place along the dock (past the next item shown here).</summary>
    private void MoveEditItem(FrameworkElement view, int direction)
    {
        if (ItemOf(view) is not { } item) return;
        var items = EditableItems();
        int position = items.IndexOf(view) + direction;
        if (position < 0 || position >= items.Count || ItemOf(items[position]) is not { } neighbour) return;

        int neighbourIndex = _config.Items.IndexOf(neighbour);
        if (neighbourIndex < 0 || _config.Items.IndexOf(item) < 0) return;

        bool itemAtEnd = IsPinnedEnd(item), neighbourAtEnd = IsPinnedEnd(neighbour);
        if (itemAtEnd != neighbourAtEnd)
        {
            // Crossing between the scrolling items and the right edge (only widgets can live there): the item joins the
            // other side next to the border, before the first item at the edge or after the last scrolling one.
            if (item.Kind != DockItemKind.Widget) return;
            item.PinnedEnd = neighbourAtEnd;
            int target = direction > 0 ? neighbourIndex : neighbourIndex + 1;
            int index = _config.Items.IndexOf(item);
            if (target == index || target == index + 1) _config.NotifyItemsChanged(); // already next to the border
            else AppServices.ConfigService.MoveItem(item.Id, target);
            return;
        }

        AppServices.ConfigService.MoveItem(item.Id, direction > 0 ? neighbourIndex + 1 : neighbourIndex);
    }

    // ------------------------------------------------------------------ Keyboard

    private void MoveEditFocus(FrameworkElement view)
    {
        _editFocus = ItemOf(view);
        PlaceFocusRing(view, () => _editing && ReferenceEquals(ItemOf(view), _editFocus));
    }

    private void OnEditModeKey(object sender, KeyEventArgs e)
    {
        if (!_editing) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool back = IsVertical ? key == Key.Up : key == Key.Left;
        bool forward = IsVertical ? key == Key.Down : key == Key.Right;

        var items = EditableItems();
        int index = _editFocus is null ? -1 : items.FindIndex(v => ReferenceEquals(ItemOf(v), _editFocus));
        var focused = index >= 0 ? items[index] : null;

        if (key is Key.Escape or Key.Enter)
        {
            DockEditMode.Exit();
        }
        else if (back || forward)
        {
            if (ctrl && focused is not null) MoveEditItem(focused, forward ? 1 : -1);
            else if (items.Count > 0) MoveEditFocus(items[index < 0 ? 0 : Math.Clamp(index + (forward ? 1 : -1), 0, items.Count - 1)]);
        }
        else if (key == Key.Home && items.Count > 0) MoveEditFocus(items[0]);
        else if (key == Key.End && items.Count > 0) MoveEditFocus(items[^1]);
        else if (key is Key.Delete or Key.Back && focused is not null) RemoveEditItem(focused);
        else if (key is Key.OemPlus or Key.Add && focused is WidgetItemView wider) ResizeEditItem(wider, 1);
        else if (key is Key.OemMinus or Key.Subtract && focused is WidgetItemView narrower) ResizeEditItem(narrower, -1);
        else return;
        e.Handled = true;
    }

    // ------------------------------------------------------------------ "+" tile and its picker

    private FrameworkElement AddTile => _addTile ??= CreateAddTile();

    private FrameworkElement CreateAddTile()
    {
        var outline = new Rectangle
        {
            RadiusX = 10,
            RadiusY = 10,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 3, 2 },
        };
        outline.SetResourceReference(Shape.StrokeProperty, "TextTertiaryBrush");
        var glyph = new TextBlock { Text = "", FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var tile = new Grid
        {
            Width = 40,
            Height = 40,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = L.T("Add to the dock"),
            Children = { outline, glyph },
        };
        AutomationProperties.SetName(tile, L.T("Add to the dock"));
        tile.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ToggleAddPicker();
        };
        return tile;
    }

    private void ToggleAddPicker()
    {
        if (_addPicker is { } open)
        {
            // A click on the tile has already started closing it (the popup closes on any click outside it).
            if (!PopupAnimationHelper.IsClosing(open)) CloseAddPicker();
            return;
        }
        if (DateTime.UtcNow - _addPickerClosedAt < TimeSpan.FromMilliseconds(250)) return;
        AddTile.BringIntoView();
        UpdateLayout();

        var (popup, content) = WidgetUi.PopupShell(330);
        content.Children.Add(WidgetUi.PopupHeader(L.T("Add to the dock")));
        var list = new StackPanel();
        list.Children.Add(PickerRow(Glyph(""), L.T("Separator"), () => AddFromEditMode(DockItem.Separator())));
        list.Children.Add(PickerRow(Glyph(""), L.T("Pin application…"), () =>
        {
            CloseAddPicker();
            App.Instance.ShowAppPicker();
        }));
        foreach (var category in WidgetCategories.Ordered)
        {
            var descriptors = WidgetRegistry.All.Where(d => d.Category == category).ToList();
            if (descriptors.Count == 0) continue;
            var heading = WidgetUi.Text("CaptionText", L.T(category));
            heading.Margin = new Thickness(6, 10, 0, 4);
            list.Children.Add(heading);
            // The same cards as the gallery's, in their small form.
            foreach (var descriptor in descriptors)
                list.Children.Add(new Settings.GalleryCard(descriptor, null, (d, _) => AddFromEditMode(DockItem.ForWidget(d.Id))));
        }
        content.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });

        _addPicker = popup;
        popup.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_addPicker, popup)) return;
            _addPicker = null;
            _addPickerClosedAt = DateTime.UtcNow;
        };
        PopupPlacement.PlacePopup(popup, AddTile, _config.Edge, gap: 6);
        GlobalPopupDismissHook.RegisterPopup(popup);
        PopupAnimationHelper.AnimateOpen(popup, _config.Edge, AddTile);
    }

    private static TextBlock Glyph(string glyph)
    {
        var block = new TextBlock { Text = glyph, FontSize = 14, Width = 15, TextAlignment = TextAlignment.Center };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return block;
    }

    private static FrameworkElement PickerRow(FrameworkElement icon, string text, Action run)
    {
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 10, 0);
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } },
        };
        AutomationProperties.SetName(row, text);
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            run();
        };
        return row;
    }

    private void AddFromEditMode(DockItem item)
    {
        CloseAddPicker();
        // A widget added on another display's dock belongs to that display.
        if (item.Kind == DockItemKind.Widget) item.Display = _secondaryDevice;
        AppServices.ConfigService.AddItem(item);
    }

    private void CloseAddPicker()
    {
        if (_addPicker is not { } popup) return;
        _addPicker = null;
        _addPickerClosedAt = DateTime.UtcNow;
        if (popup.IsOpen && !PopupAnimationHelper.IsClosing(popup)) PopupAnimationHelper.ClosePopup(popup, _config.Edge, _addTile);
    }

    // ------------------------------------------------------------------ Done bar

    private void ShowEditBar()
    {
        if (_editBar is null)
        {
            var hint = new TextBlock
            {
                Text = L.T("Drag items to move them. × removes, the handle on a widget's edge resizes it."),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                MaxWidth = 460,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 14, 0),
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            var done = new Button { Content = L.T("Done"), MinWidth = 72, IsDefault = false };
            done.SetResourceReference(StyleProperty, "AccentButton");
            done.Click += (_, _) => DockEditMode.Exit();
            AutomationProperties.SetName(done, L.T("Done editing the dock"));

            var bar = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 6, 6, 6),
                Margin = new Thickness(8),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { hint, done } },
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.3 },
            };
            bar.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            bar.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
            _editBar = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = true,
                PopupAnimation = PopupAnimation.None,
                Child = bar,
            };
        }

        PopupPlacement.PlacePopup(_editBar, Root, _config.Edge, gap: 4);
        _editBar.IsOpen = true;
        if (_editBar.Child is FrameworkElement content) Motion.Appear(content, fromScale: 0.95);
        // Again once the "+" tile has changed the dock's size.
        OnEditLayoutChanged(null, EventArgs.Empty);
    }

    private void PlaceEditBar()
    {
        if (!_editing || _editBar is not { IsOpen: true } bar) return;
        PopupPlacement.PlacePopup(bar, Root, _config.Edge, gap: 4);
        // An open popup measures its place again when its offset changes.
        bar.HorizontalOffset = 1;
        bar.HorizontalOffset = 0;
    }
}
