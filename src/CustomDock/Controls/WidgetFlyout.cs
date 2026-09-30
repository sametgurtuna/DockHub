using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CustomDock.Widgets;

namespace CustomDock.Controls;

/// <summary>
/// The frame of every widget panel: a rounded glass card with a shadow, a header (icon, title, actions such as
/// refresh or settings, and an optional close button), the content, and an optional footer. Its width is one of three
/// sizes (<see cref="FlyoutSize"/>). Put it in a <see cref="Popup"/> the widget opens with WidgetBase.OpenPopup, which
/// adds the shared behavior (Esc closes, focus; see <see cref="WidgetFlyoutHost"/>).
/// </summary>
[TemplatePart(Name = "PART_Close", Type = typeof(ButtonBase))]
public class WidgetFlyout : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(WidgetFlyout), new PropertyMetadata(null));

    /// <summary>Glyph (icon font) before the title.</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(WidgetFlyout), new PropertyMetadata(null));

    /// <summary>Buttons at the right of the header (before the close button).</summary>
    public static readonly DependencyProperty HeaderActionsProperty = DependencyProperty.Register(
        nameof(HeaderActions), typeof(object), typeof(WidgetFlyout), new PropertyMetadata(null));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(object), typeof(WidgetFlyout), new PropertyMetadata(null));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(FlyoutSize), typeof(WidgetFlyout),
        new FrameworkPropertyMetadata(FlyoutSize.Standard, (d, e) => ((WidgetFlyout)d).Width = WidgetFlyoutLayout.WidthOf((FlyoutSize)e.NewValue)));

    public static readonly DependencyProperty ShowCloseButtonProperty = DependencyProperty.Register(
        nameof(ShowCloseButton), typeof(bool), typeof(WidgetFlyout), new PropertyMetadata(false));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(WidgetFlyout), new PropertyMetadata(new CornerRadius(WidgetFlyoutLayout.CornerRadius)));

    /// <summary>The close button was pressed (the popup's owner closes it; see <see cref="WidgetFlyoutHost"/>).</summary>
    public static readonly RoutedEvent CloseRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(CloseRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(WidgetFlyout));

    public WidgetFlyout()
    {
        Width = WidgetFlyoutLayout.WidthOf(FlyoutSize.Standard);
    }

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    public object? HeaderActions { get => GetValue(HeaderActionsProperty); set => SetValue(HeaderActionsProperty, value); }

    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    public FlyoutSize Size { get => (FlyoutSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public bool ShowCloseButton { get => (bool)GetValue(ShowCloseButtonProperty); set => SetValue(ShowCloseButtonProperty, value); }

    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public event RoutedEventHandler CloseRequested
    {
        add => AddHandler(CloseRequestedEvent, value);
        remove => RemoveHandler(CloseRequestedEvent, value);
    }

    private ButtonBase? _close;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_close is not null) _close.Click -= OnCloseClick;
        _close = GetTemplateChild("PART_Close") as ButtonBase;
        if (_close is null) return;
        _close.Click += OnCloseClick;
        _close.ToolTip = Core.L.T("Close");
        System.Windows.Automation.AutomationProperties.SetName(_close, Core.L.T("Close"));
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => RaiseEvent(new RoutedEventArgs(CloseRequestedEvent, this));
}
