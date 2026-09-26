using System.Windows;
using System.Windows.Controls;

namespace CustomDock.Controls;

/// <summary>Rounded-corner widget card inside the dock.</summary>
public class WidgetCard : ContentControl
{
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

    public bool HoverEnabled { get => (bool)GetValue(HoverEnabledProperty); set => SetValue(HoverEnabledProperty, value); }

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
