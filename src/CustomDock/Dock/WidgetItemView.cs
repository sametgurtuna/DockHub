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
/// Dock'ta bir widget'ı kart içinde barındırır; sağ tık menüsü ve sürükleme sağlar.
/// Dikey dock'ta widget yerine özet kutucuğu gösterir; widget'ın tamamı tıklanınca bir panelde açılır.
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
        // Örtük stiller türetilmiş türlere uygulanmaz; kart stilini açıkça bağla.
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
        // Widget kendini gizlerse (ör. çalan yokken medya) kartı da gizle.
        System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(VisibilityProperty, typeof(UIElement))
            .AddValueChanged(widget, OnWidgetVisibilityChanged);
        Visibility = widget.Visibility;
        ApplyAppearance();
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

    /// <summary>Sağ tık menüsündeki "Ayarlar…" komutu.</summary>
    public static event Action<DockItem>? SettingsRequested;

    /// <summary>Widget kodundan (ör. panel içindeki dişli düğmesi) ayarlar sayfasını istemek için.</summary>
    public static void RequestSettings(DockItem item) => SettingsRequested?.Invoke(item);

    /// <summary>Yatay dock'ta tam kart, dikey dock'ta özet kutucuk.</summary>
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

    // ------------------------------------------------------------------ Kompakt mod: tıklama ve panel

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_compact || DockDragHelper.JustDragged || e.Handled) return;
        e.Handled = true;
        if (Widget.OnCompactClick()) return;
        // StaysOpen=false panel, kutucuğa basıldığı anda kapanır; aynı tıklama onu yeniden açmasın.
        if (_flyout?.IsOpen == true) CloseFlyout();
        else if (DateTime.UtcNow - _flyoutClosedAt > TimeSpan.FromMilliseconds(250)) OpenFlyout();
    }

    private void OpenFlyout()
    {
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
                StaysOpen = false,
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
        Motion.PopIn((FrameworkElement)_flyout.Child, PopupPlacement.EnterOffset(_host.Edge));
        _flyoutInteraction = true;
        _host.BeginInteraction();
        _flyout.IsOpen = true;
    }

    private void CloseFlyout()
    {
        if (_flyout is { IsOpen: true }) _flyout.IsOpen = false;
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
            menu.Items.Add(DockMenu.Submenu("Görünüm", "\uE8A9", descriptor.Variants.Select(v =>
                DockMenu.Check(v.Name, Widget.Variant == v.Id, () =>
                {
                    Item.Variant = v.Id;
                    AppServices.ConfigService.ScheduleSave();
                }))));
        }
        menu.Items.Add(DockMenu.Item("Widget ayarları…", "\uE713", () => SettingsRequested?.Invoke(Item)));
        menu.Items.Add(DockMenu.Item("Dock'tan kaldır", "\uE77A", () => AppServices.ConfigService.RemoveItem(Item.Id)));
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

/// <summary>Öğeler arasındaki ince ayraç.</summary>
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
        ContextMenu.Items.Add(DockMenu.Item("Ayracı kaldır", "\uE77A", () => AppServices.ConfigService.RemoveItem(item.Id)));
        DockDragHelper.Attach(this, () => new DataObject(DockDragHelper.ItemFormat, item.Id));
    }

    public DockItem? Item { get; }

    public SeparatorView() : this(false)
    {
    }

    /// <summary>Çalışan uygulamalar bölümünün başındaki otomatik ayraç için.</summary>
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
