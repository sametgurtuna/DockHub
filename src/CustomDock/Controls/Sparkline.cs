using System.Windows;
using System.Windows.Media;

namespace CustomDock.Controls;

/// <summary>Small two-series line chart (download solid, upload dashed line).</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty PrimaryStrokeProperty = DependencyProperty.Register(
        nameof(PrimaryStroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondaryStrokeProperty = DependencyProperty.Register(
        nameof(SecondaryStroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.HotPink, FrameworkPropertyMetadataOptions.AffectsRender));

    private IReadOnlyList<double> _primary = Array.Empty<double>();
    private IReadOnlyList<double> _secondary = Array.Empty<double>();

    public Brush PrimaryStroke { get => (Brush)GetValue(PrimaryStrokeProperty); set => SetValue(PrimaryStrokeProperty, value); }
    public Brush SecondaryStroke { get => (Brush)GetValue(SecondaryStrokeProperty); set => SetValue(SecondaryStrokeProperty, value); }

    public void SetData(IReadOnlyList<double> primary, IReadOnlyList<double> secondary)
    {
        _primary = primary;
        _secondary = secondary;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => Ui.Fit(availableSize, 70, 26);

    protected override void OnRender(DrawingContext dc)
    {
        double max = Math.Max(1, Math.Max(_primary.DefaultIfEmpty(0).Max(), _secondary.DefaultIfEmpty(0).Max()));
        DrawSeries(dc, _secondary, max, new Pen(SecondaryStroke, 1.2) { DashStyle = new DashStyle(new[] { 2.0, 2.0 }, 0) });
        DrawSeries(dc, _primary, max, new Pen(PrimaryStroke, 1.6) { LineJoin = PenLineJoin.Round });
    }

    private void DrawSeries(DrawingContext dc, IReadOnlyList<double> values, double max, Pen pen)
    {
        if (values.Count < 2) return;
        double w = ActualWidth, h = ActualHeight - 2;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int i = 0; i < values.Count; i++)
            {
                var point = new Point(w * i / (values.Count - 1), 1 + h - h * Math.Clamp(values[i] / max, 0, 1));
                if (i == 0) ctx.BeginFigure(point, false, false);
                else ctx.LineTo(point, true, true);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
