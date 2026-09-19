using System.Windows;
using System.Windows.Controls;

namespace CustomDock.Controls;

/// <summary>
/// Three-child layout: first child at start, last child at end, center fills remaining space.
/// Orientation can be horizontal (bottom/top dock) or vertical (left/right dock).
/// </summary>
public sealed class DockZonesPanel : Panel
{
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(DockZonesPanel),
        new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    private bool Horizontal => Orientation == Orientation.Horizontal;

    protected override Size MeasureOverride(Size available)
    {
        if (InternalChildren.Count != 3) return base.MeasureOverride(available);
        var start = InternalChildren[0];
        var center = InternalChildren[1];
        var end = InternalChildren[2];

        var unbounded = Horizontal ? new Size(double.PositiveInfinity, available.Height) : new Size(available.Width, double.PositiveInfinity);
        start.Measure(unbounded);
        end.Measure(unbounded);

        double used = Length(start.DesiredSize) + Length(end.DesiredSize);
        double remaining = Math.Max(0, (Horizontal ? available.Width : available.Height) - used);
        center.Measure(Horizontal ? new Size(remaining, available.Height) : new Size(available.Width, remaining));

        double length = used + Length(center.DesiredSize);
        double thickness = Math.Max(Thickness(start.DesiredSize), Math.Max(Thickness(center.DesiredSize), Thickness(end.DesiredSize)));
        return Horizontal ? new Size(length, thickness) : new Size(thickness, length);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (InternalChildren.Count != 3) return base.ArrangeOverride(final);
        var start = InternalChildren[0];
        var center = InternalChildren[1];
        var end = InternalChildren[2];

        double total = Horizontal ? final.Width : final.Height;
        double thickness = Horizontal ? final.Height : final.Width;
        double startLen = Length(start.DesiredSize);
        double endLen = Length(end.DesiredSize);
        double centerLen = Math.Max(0, total - startLen - endLen);

        Arrange(start, 0, startLen, thickness);
        Arrange(center, startLen, centerLen, thickness);
        Arrange(end, total - endLen, endLen, thickness);
        return final;
    }

    private void Arrange(UIElement element, double offset, double length, double thickness)
        => element.Arrange(Horizontal ? new Rect(offset, 0, length, thickness) : new Rect(0, offset, thickness, length));

    private double Length(Size size) => Horizontal ? size.Width : size.Height;

    private double Thickness(Size size) => Horizontal ? size.Height : size.Width;
}
