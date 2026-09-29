using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Widgets;

namespace CustomDock.Dock;

/// <summary>
/// Hosts a widget inside a card on the dock; provides right-click menu and dragging.
/// Shows a summary tile instead of widget on vertical dock; full widget opens in a panel when clicked.
/// </summary>
public sealed class WidgetItemView : WidgetCard
{
    private readonly IWidgetHost _host;
    private Popup? _flyout;
    private WidgetCard? _flyoutCard;
    private bool _compact;
    private bool _editing;
    private bool _flyoutInteraction;
    private bool _flyoutFromKeyboard;
    private DateTime _flyoutClosedAt;

    public WidgetItemView(DockItem item, WidgetBase widget, IWidgetHost host)
    {
        // Implicit styles don't apply to derived types; explicitly attach card style.
        SetResourceReference(StyleProperty, typeof(WidgetCard));
        Item = item;
        Widget = widget;
        _host = host;
        Content = widget;
        ContextMenu = new ContextMenu();
        ContextMenuOpening += OnContextMenuOpening;
        widget.CardAppearanceChanged += ApplyAppearance;
        widget.CompactAnchor = this;
        System.Windows.Automation.AutomationProperties.SetName(this, L.T("{0} widget", widget.Descriptor?.Name ?? ""));
        widget.BeforeCompactPopup = CloseFlyout;
        widget.IdleChanged += OnWidgetIdleChanged;
        item.PropertyChanged += OnIdleSettingChanged;
        // If widget hides itself (e.g. media widget when nothing is playing), hide the card too.
        System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(VisibilityProperty, typeof(UIElement))
            .AddValueChanged(widget, OnWidgetVisibilityChanged);
        Visibility = widget.Visibility;
        ApplyAppearance();
        if (widget.AllowDrop)
        {
            AllowDrop = true;
            DragOver += (_, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                }
            };
            Drop += (_, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    AppServices.RecycleBin.SendToRecycleBin(files);
                    e.Handled = true;
                }
            };
        }
        DockDragHelper.Attach(this, () => DockDragHelper.StringData(DockDragHelper.ItemFormat, item.Id));
        MouseEnter += (_, _) => WindowPreviewWindow.Instance.HidePreview();
        Loaded += OnFirstLoaded;
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        Motion.Appear(this, fromScale: 0.9);
    }

    public DockItem Item { get; }

    public WidgetBase Widget { get; }

    /// <summary>"Settings…" command in the right-click menu.</summary>
    public static event Action<DockItem>? SettingsRequested;

    /// <summary>Used by widget code (e.g. gear button inside panel) to request settings page.</summary>
    public static void RequestSettings(DockItem item) => SettingsRequested?.Invoke(item);

    private bool _verticalDock;

    /// <summary>Full card on horizontal dock, summary tile on vertical dock (and while idle, if the item asks for it).</summary>
    public void SetCompact(bool vertical)
    {
        _verticalDock = vertical;
        UpdateCompactMode();
    }

    private bool CollapsedWhileIdle => Item.CollapseWhenIdle && Widget.IsIdle;

    /// <summary>In edit mode a widget that hides itself (nothing playing) shows as its tile, so it can be moved or removed.</summary>
    private bool ShownForEditing => _editing && Widget.Visibility != Visibility.Visible;

    private void UpdateCompactMode() => SetCompactCore(_verticalDock || CollapsedWhileIdle || ShownForEditing);

    private void OnWidgetIdleChanged() => Dispatcher.BeginInvoke(UpdateCompactMode);

    private void OnIdleSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DockItem.CollapseWhenIdle)) UpdateCompactMode();
        else if (e.PropertyName == nameof(DockItem.Variant)) ApplyWidthClass();
    }

    private void SetCompactCore(bool compact)
    {
        if (compact == _compact && Content is not null) return;
        _compact = compact;
        Widget.IsCompact = compact;
        CloseFlyout();

        if (compact)
        {
            if (_flyoutCard is not null) _flyoutCard.Content = null;
            Content = Widget.Compact;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            Width = double.NaN;
            Height = 46;
        }
        else
        {
            if (_flyoutCard is not null) _flyoutCard.Content = null;
            Content = Widget;
            ClearValue(HeightProperty);
        }
        ApplyAppearance();
    }

    private void OnWidgetVisibilityChanged(object? sender, EventArgs e) => UpdateVisibility();

    private void UpdateVisibility()
    {
        Visibility = _editing ? Visibility.Visible : Widget.Visibility;
        UpdateCompactMode();
    }

    /// <summary>
    /// Snaps the card to the grid and gives it its variant's minimum width while "Even widget widths" is on (the card
    /// style then centers the widget in any extra room).
    /// </summary>
    private void ApplyWidthClass()
    {
        SnapToGrid = AlignWidths && !_compact;
        var width = Widget.Descriptor.Variants.FirstOrDefault(v => v.Id == Widget.Variant)?.Width ?? WidgetWidth.Auto;
        MinWidth = SnapToGrid ? WidgetWidths.MinCardWidth(width) : 0;
    }

    private void ApplyAppearance()
    {
        ApplyWidthClass();
        Apply(this, compact: _compact);
        if (_flyoutCard is not null) Apply(_flyoutCard, compact: false);
    }

    private void Apply(WidgetCard card, bool compact)
    {
        // Seamless style: no card on the dock (the panel of a tile keeps its card). A widget with a color of its own
        // (sticky note, water) keeps it, a little fainter.
        bool seamless = AppServices.Config.WidgetStyle == WidgetStyle.Seamless && !ReferenceEquals(card, _flyoutCard);
        if (Widget.CardBackground is { } background)
        {
            card.Background = seamless ? Faded(background) : background;
            card.BorderBrush = seamless ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        }
        else if (seamless)
        {
            // Transparent, not null: the whole card still takes the mouse (drag, menu, hover).
            card.Background = Brushes.Transparent;
            card.BorderBrush = Brushes.Transparent;
        }
        else
        {
            card.SetResourceReference(BackgroundProperty, "CardBrush");
            card.SetResourceReference(BorderBrushProperty, "CardBorderBrush");
        }
        card.Padding = compact ? new Thickness(0) : Widget.CardPadding;
    }

    /// <summary>A little fainter, so the colored card sits on the dock; its dark text keeps enough contrast. Contrast themes keep their colors.</summary>
    private static Brush Faded(Brush brush)
    {
        if (SystemParameters.HighContrast) return brush;
        var faded = brush.CloneCurrentValue();
        faded.Opacity = brush.Opacity * 0.8;
        faded.Freeze();
        return faded;
    }

    /// <summary>Picks up changed look settings (widget style, even widths).</summary>
    public void RefreshLook() => ApplyAppearance();

    // ------------------------------------------------------------------ Compact mode: click and panel

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_compact || DockDragHelper.JustDragged || e.Handled || _editing) return;
        e.Handled = true;
        if (Widget.OnCompactClick()) return;
        if (_flyout?.IsOpen == true || (_flyout is not null && PopupAnimationHelper.IsClosing(_flyout))) CloseFlyout();
        else if (DateTime.UtcNow - _flyoutClosedAt > TimeSpan.FromMilliseconds(250) && !PopupAnimationHelper.IsClosing(_flyout!)) OpenFlyout();
    }

    private void OpenFlyout()
    {
        if (_flyout is not null && (_flyout.IsOpen || PopupAnimationHelper.IsClosing(_flyout)))
            return;

        if (_flyout is null)
        {
            _flyoutCard = new WidgetCard { HoverEnabled = false };
            _flyoutCard.SetResourceReference(StyleProperty, typeof(WidgetCard));
            // The shared panel frame around the full widget; it is as wide as the widget's card.
            var frame = new WidgetFlyout
            {
                Title = Widget.Descriptor.Name,
                Padding = new Thickness(8, 8, 8, 6),
                Width = double.NaN,
                Content = _flyoutCard,
            };
            _flyout = new Popup
            {
                Child = frame,
                AllowsTransparency = true,
                StaysOpen = true,
                PopupAnimation = PopupAnimation.None,
                PlacementTarget = this,
            };
            _flyout.Closed += (_, _) =>
            {
                _flyoutClosedAt = DateTime.UtcNow;
                // Keyboard mode goes on at the tile.
                if (_flyoutFromKeyboard) WidgetFlyoutHost.ReturnFocus(_flyout, _host.Window);
                if (!_flyoutInteraction) return;
                _flyoutInteraction = false;
                _host.EndInteraction();
            };
        }

        if (_flyoutCard!.Content is null)
        {
            Content = Widget.Compact;
            _flyoutCard.Content = Widget;
            Apply(_flyoutCard, compact: false);
        }

        PopupPlacement.PlacePopup(_flyout, this, _host.Edge, gap: 2);
        _flyoutInteraction = true;
        _host.BeginInteraction();
        WidgetFlyoutHost.Attach(_flyout, CloseFlyout);
        GlobalPopupDismissHook.RegisterPopup(_flyout);
        PopupAnimationHelper.AnimateOpen(_flyout, _host.Edge, this);
        _flyoutFromKeyboard = _host.IsKeyboardNavigating;
        if (_flyoutFromKeyboard) WidgetFlyoutHost.FocusFirst(_flyout);
    }

    private void CloseFlyout()
    {
        if (_flyout is { IsOpen: true } && !PopupAnimationHelper.IsClosing(_flyout))
            PopupAnimationHelper.ClosePopup(_flyout, _host.Edge, this);
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e) => BuildContextMenu();

    /// <summary>Edit mode: the panel closes and the widget's own controls stop taking clicks (the card is dragged instead).</summary>
    public void SetEditing(bool editing)
    {
        if (_editing == editing) return;
        _editing = editing;
        if (editing) CloseFlyout();
        Widget.SetEditing(editing);
        UpdateVisibility();
    }

    /// <summary>Enter on the focused widget: runs its main action or opens its panel (compact), otherwise its menu.</summary>
    public void ActivateFromKeyboard()
    {
        if (!_compact) { OpenContextMenu(); return; }
        if (_flyout?.IsOpen == true) { CloseFlyout(); return; }
        if (Widget.OnCompactClick()) return;
        OpenFlyout();
    }

    /// <summary>Opens the right-click menu from the keyboard.</summary>
    public void OpenContextMenu()
    {
        BuildContextMenu();
        ContextMenu.PlacementTarget = this;
        ContextMenu.Placement = PlacementMode.Top;
        ContextMenu.IsOpen = true;
    }

    private void BuildContextMenu()
    {
        CloseFlyout();
        var menu = ContextMenu;
        menu.Items.Clear();
        var descriptor = Widget.Descriptor;
        menu.Items.Add(DockMenu.Header(descriptor.Name));

        int before = menu.Items.Count;
        Widget.AddContextMenuItems(menu.Items);
        if (menu.Items.Count > before)
            menu.Items.Insert(before, DockMenu.Separator());

        menu.Items.Add(DockMenu.Separator());
        if (descriptor.Variants.Count > 1)
        {
            menu.Items.Add(DockMenu.Submenu("Appearance", "\uE8A9", descriptor.Variants.Select(v =>
                DockMenu.Check(v.Name, Widget.Variant == v.Id, () =>
                {
                    Item.Variant = v.Id;
                    AppServices.ConfigService.ScheduleSave();
                }))));
        }
        menu.Items.Add(DockMenu.Item("Widget settings…", "\uE713", () => SettingsRequested?.Invoke(Item)));
        if (Widget.SupportsIdle)
            menu.Items.Add(DockMenu.Check("Collapse when idle", Item.CollapseWhenIdle, () =>
            {
                Item.CollapseWhenIdle = !Item.CollapseWhenIdle;
                AppServices.ConfigService.ScheduleSave();
            }));
        // Widgets inside a folder stay with their folder.
        if (AppServices.Config.Items.Contains(Item) && WidgetDisplays.Choices() is { Count: > 1 } displays)
        {
            var current = WidgetDisplays.Current(displays, Item);
            menu.Items.Add(DockMenu.Submenu("Show on", "\uE7F4", displays.Select(choice =>
                DockMenu.Check(choice.Label, ReferenceEquals(choice, current), () => WidgetDisplays.Move(Item, choice.Device)))));
        }
        menu.Items.Add(DockMenu.Check("Pin to right edge", Item.PinnedEnd, () =>
        {
            Item.PinnedEnd = !Item.PinnedEnd;
            AppServices.Config.NotifyItemsChanged();
        }));
        menu.Items.Add(DockMenu.Item("Edit dock", "\uE70F", () => (Window.GetWindow(this) as DockWindow)?.EnterEditMode()));
        menu.Items.Add(DockMenu.Item("Remove from dock", "\uE77A", () => AppServices.ConfigService.RemoveItem(Item.Id)));
    }

    public void Detach()
    {
        CloseFlyout();
        Widget.CardAppearanceChanged -= ApplyAppearance;
        Widget.IdleChanged -= OnWidgetIdleChanged;
        Item.PropertyChanged -= OnIdleSettingChanged;
        Widget.CompactAnchor = null;
        Widget.BeforeCompactPopup = null;
        System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(VisibilityProperty, typeof(UIElement))
            .RemoveValueChanged(Widget, OnWidgetVisibilityChanged);
    }
}

/// <summary>Thin separator between items.</summary>
public sealed class SeparatorView : Border
{
    private readonly Border _line;

    public SeparatorView(DockItem item, bool vertical)
    {
        Item = item;
        Background = Brushes.Transparent;
        _line = new Border { CornerRadius = new CornerRadius(0.5) };
        _line.SetResourceReference(BackgroundProperty, "SeparatorBrush");
        Child = _line;
        SetOrientation(vertical);
        ContextMenu = new ContextMenu();
        ContextMenu.Items.Add(DockMenu.Item("Remove separator", "\uE77A", () => AppServices.ConfigService.RemoveItem(item.Id)));
        DockDragHelper.Attach(this, () => DockDragHelper.StringData(DockDragHelper.ItemFormat, item.Id));
    }

    public DockItem? Item { get; }

    public SeparatorView() : this(false)
    {
    }

    /// <summary>For the automatic separator at the beginning of the running applications section.</summary>
    public SeparatorView(bool vertical)
    {
        Background = Brushes.Transparent;
        _line = new Border();
        _line.SetResourceReference(BackgroundProperty, "SeparatorBrush");
        Child = _line;
        SetOrientation(vertical);
    }

    public void SetOrientation(bool vertical)
    {
        if (vertical)
        {
            Width = 46;
            Height = 13;
            _line.Width = 28;
            _line.Height = 1;
        }
        else
        {
            Width = 13;
            Height = 46;
            _line.Width = 1;
            _line.Height = 28;
        }
    }
}
