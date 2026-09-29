using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CustomDock.Controls;

/// <summary>Where a card draws the thin line that separates it from the widget before it (Seamless widget style).</summary>
public enum CardDivider { None, Left, Top }

/// <summary>Rounded-corner widget card inside the dock.</summary>
public class WidgetCard : ContentControl
{
    /// <summary>A thin line in the gap before the card: at its left, or at its top on a side dock.</summary>
    public static readonly DependencyProperty DividerProperty = DependencyProperty.Register(
        nameof(Divider), typeof(CardDivider), typeof(WidgetCard), new PropertyMetadata(CardDivider.None));

    public static readonly DependencyProperty HoverEnabledProperty = DependencyProperty.Register(
        nameof(HoverEnabled), typeof(bool), typeof(WidgetCard), new PropertyMetadata(true));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(WidgetCard), new PropertyMetadata(new CornerRadius(10)));

    /// <summary>
    /// Round the card's width up to a multiple of half its height, so a row of different widgets keeps an even
    /// rhythm. Rounding only adds room (the card style centers the content then), it never clips.
    /// </summary>
    public static readonly DependencyProperty SnapToGridProperty = DependencyProperty.Register(
        nameof(SnapToGrid), typeof(bool), typeof(WidgetCard), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>
    /// Set by content that keeps the mouse to itself (a web view is a window of its own, so WPF never sees the
    /// pointer over it): the card is highlighted as if the mouse were over it.
    /// </summary>
    public static readonly DependencyProperty IsContentHoveredProperty = DependencyProperty.Register(
        nameof(IsContentHovered), typeof(bool), typeof(WidgetCard), new PropertyMetadata(false, (d, _) => ((WidgetCard)d).UpdateHighlight()));

    private static readonly DependencyPropertyKey IsHighlightedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsHighlighted), typeof(bool), typeof(WidgetCard), new PropertyMetadata(false));

    /// <summary>The mouse is over the card or its content; drives the hover look in the card style.</summary>
    public static readonly DependencyProperty IsHighlightedProperty = IsHighlightedPropertyKey.DependencyProperty;

    public bool HoverEnabled { get => (bool)GetValue(HoverEnabledProperty); set => SetValue(HoverEnabledProperty, value); }

    public CardDivider Divider { get => (CardDivider)GetValue(DividerProperty); set => SetValue(DividerProperty, value); }

    public bool IsContentHovered { get => (bool)GetValue(IsContentHoveredProperty); set => SetValue(IsContentHoveredProperty, value); }

    public bool IsHighlighted => (bool)GetValue(IsHighlightedProperty);

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateHighlight();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateHighlight();
    }

    private void UpdateHighlight() => SetValue(IsHighlightedPropertyKey, IsMouseOver || IsContentHovered);

    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public bool SnapToGrid { get => (bool)GetValue(SnapToGridProperty); set => SetValue(SnapToGridProperty, value); }

    /// <summary>The "Even widget widths" setting, read by dock cards.</summary>
    public static bool AlignWidths { get; set; } = true;

    protected override Size MeasureOverride(Size constraint)
    {
        var size = base.MeasureOverride(constraint);
        if (!SnapToGrid || size.Width <= 0 || double.IsInfinity(size.Width)) return size;

        double height = double.IsNaN(Height) || Height <= 0 ? size.Height : Height;
        double step = height / 2;
        if (step < 8) return size;
        double snapped = Math.Ceiling((size.Width - 0.5) / step) * step;
        if (!double.IsInfinity(constraint.Width)) snapped = Math.Min(snapped, constraint.Width);
        return new Size(Math.Max(size.Width, snapped), size.Height);
    }
}
