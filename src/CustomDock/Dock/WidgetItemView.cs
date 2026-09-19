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
    private bool _flyoutInteraction;
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
        widget.BeforeCompactPopup = CloseFlyout;
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
        DockDragHelper.Attach(this, () => new DataObject(DockDragHelper.ItemFormat, item.Id));
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

    /// <summary>Full card on horizontal dock, summary tile on vertical dock.</summary>
    public void SetCompact(bool compact)
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

    private void OnWidgetVisibilityChanged(object? sender, EventArgs e) => Visibility = Widget.Visibility;

    private void ApplyAppearance()
    {
        Apply(this, compact: _compact);
        if (_flyoutCard is not null) Apply(_flyoutCard, compact: false);
    }

    private void Apply(WidgetCard card, bool compact)
    {
        if (Widget.CardBackground is { } background)
        {
            card.Background = background;
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        }
        else
        {
            card.SetResourceReference(BackgroundProperty, "CardBrush");
            card.SetResourceReference(BorderBrushProperty, "CardBorderBrush");
        }
        card.Padding = compact ? new Thickness(0) : Widget.CardPadding;
    }

    // ------------------------------------------------------------------ Compact mode: click and panel

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_compact || DockDragHelper.JustDragged || e.Handled) return;
        e.Handled = true;
        if (Widget.OnCompactClick()) return;
        if (_flyout?.IsOpen == true || (_flyout is not null && PopupAnimationHelper.IsClosing(_flyout))) CloseFlyout();
        else if (DateTime.UtcNow - _flyoutClosedAt > TimeSpan.FromMilliseconds(250) && !PopupAnimationHelper.IsClosing(_flyout!)) OpenFlyout();
    }

    private void OpenFlyout()
    {
        if (_flyout is not null && PopupAnimationHelper.IsClosing(_flyout))
            return;

        if (_flyout is null)
        {
            _flyoutCard = new WidgetCard { HoverEnabled = false };
            _flyoutCard.SetResourceReference(StyleProperty, typeof(WidgetCard));
            var frame = new Border
            {
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(6),
                BorderThickness = new Thickness(1),
                Child = _flyoutCard,
                Margin = new Thickness(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.35 },
            };
            frame.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            frame.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
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
        GlobalPopupDismissHook.RegisterPopup(_flyout);
        PopupAnimationHelper.AnimateOpen(_flyout, _host.Edge);
    }

    private void CloseFlyout()
    {
        if (_flyout is { IsOpen: true } && !PopupAnimationHelper.IsClosing(_flyout))
            PopupAnimationHelper.ClosePopup(_flyout, _host.Edge);
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
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
        menu.Items.Add(DockMenu.Item("Remove from dock", "\uE77A", () => AppServices.ConfigService.RemoveItem(Item.Id)));
    }

    public void Detach()
    {
        CloseFlyout();
        Widget.CardAppearanceChanged -= ApplyAppearance;
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
        DockDragHelper.Attach(this, () => new DataObject(DockDragHelper.ItemFormat, item.Id));
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
