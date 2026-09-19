using System.Windows;
using System.Windows.Media;
using CustomDock.Services;

namespace CustomDock.Controls;

/// <summary>Vector-drawn colored weather icon (since WPF does not support colored emoji).</summary>
public sealed class WeatherIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(WeatherKind), typeof(WeatherIcon),
        new FrameworkPropertyMetadata(WeatherKind.Clear, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsDayProperty = DependencyProperty.Register(
        nameof(IsDay), typeof(bool), typeof(WeatherIcon),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CloudBrushProperty = DependencyProperty.Register(
        nameof(CloudBrush), typeof(Brush), typeof(WeatherIcon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush SunBrush = Frozen(0xFF, 0xC5, 0x2E);
    private static readonly Brush MoonBrush = Frozen(0xF5, 0xE6, 0xA8);
    private static readonly Brush RainBrush = Frozen(0x4F, 0xA8, 0xFF);
    private static readonly Brush SnowBrush = Frozen(0xB8, 0xE2, 0xFF);
    private static readonly Brush BoltBrush = Frozen(0xFF, 0xD6, 0x0A);

    public WeatherKind Kind { get => (WeatherKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool IsDay { get => (bool)GetValue(IsDayProperty); set => SetValue(IsDayProperty, value); }
    public Brush CloudBrush { get => (Brush)GetValue(CloudBrushProperty); set => SetValue(CloudBrushProperty, value); }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    protected override Size MeasureOverride(Size availableSize) => Ui.Fit(availableSize, 36, 36);

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) return;

        dc.PushTransform(new TranslateTransform((ActualWidth - s) / 2, (ActualHeight - s) / 2));
        dc.PushTransform(new ScaleTransform(s, s));

        switch (Kind)
        {
            case WeatherKind.Clear:
                if (IsDay) DrawSun(dc, 0.5, 0.5, 0.2);
                else DrawMoon(dc, 0.5, 0.5, 0.3);
                break;
            case WeatherKind.PartlyCloudy:
                if (IsDay) DrawSun(dc, 0.36, 0.36, 0.14);
                else DrawMoon(dc, 0.38, 0.36, 0.2);
                DrawCloud(dc, 0.08, 0.1);
                break;
            case WeatherKind.Cloudy:
                DrawCloud(dc, 0, 0);
                break;
            case WeatherKind.Fog:
                DrawCloud(dc, 0, -0.1);
                DrawLines(dc, CloudBrush, 0.18, 0.78, 0.82, 2, 0.1);
                break;
            case WeatherKind.Drizzle:
                DrawCloud(dc, 0, -0.12);
                DrawDrops(dc, 0.08);
                break;
            case WeatherKind.Rain:
                DrawCloud(dc, 0, -0.12);
                DrawDrops(dc, 0.16);
                break;
            case WeatherKind.Snow:
                DrawCloud(dc, 0, -0.12);
                foreach (var (x, y) in new[] { (0.34, 0.78), (0.52, 0.86), (0.7, 0.78) })
                    dc.DrawEllipse(SnowBrush, null, new Point(x, y), 0.045, 0.045);
                break;
            case WeatherKind.Thunder:
                DrawCloud(dc, 0, -0.12);
                var bolt = Geometry.Parse("M0.54,0.6 L0.4,0.8 L0.5,0.8 L0.44,0.96 L0.62,0.72 L0.52,0.72 L0.58,0.6 Z");
                dc.DrawGeometry(BoltBrush, null, bolt);
                break;
        }

        dc.Pop();
        dc.Pop();
    }

    private static void DrawSun(DrawingContext dc, double cx, double cy, double r)
    {
        var center = new Point(cx, cy);
        var pen = new Pen(SunBrush, r * 0.32) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            dc.DrawLine(pen,
                new Point(cx + Math.Cos(a) * r * 1.5, cy + Math.Sin(a) * r * 1.5),
                new Point(cx + Math.Cos(a) * r * 2.05, cy + Math.Sin(a) * r * 2.05));
        }
        dc.DrawEllipse(SunBrush, null, center, r, r);
    }

    private static void DrawMoon(DrawingContext dc, double cx, double cy, double r)
    {
        var moon = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(cx, cy), r, r),
            new EllipseGeometry(new Point(cx + r * 0.55, cy - r * 0.4), r * 0.85, r * 0.85));
        dc.DrawGeometry(MoonBrush, null, moon);
    }

    private void DrawCloud(DrawingContext dc, double dx, double dy)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new RectangleGeometry(new Rect(0.14 + dx, 0.52 + dy, 0.72, 0.26), 0.13, 0.13));
        group.Children.Add(new EllipseGeometry(new Point(0.36 + dx, 0.52 + dy), 0.16, 0.16));
        group.Children.Add(new EllipseGeometry(new Point(0.58 + dx, 0.46 + dy), 0.21, 0.21));
        dc.DrawGeometry(CloudBrush, null, group);
    }

    private static void DrawDrops(DrawingContext dc, double length)
    {
        var pen = new Pen(RainBrush, 0.06) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        foreach (double x in new[] { 0.34, 0.52, 0.7 })
            dc.DrawLine(pen, new Point(x, 0.74), new Point(x - length * 0.35, 0.74 + length));
    }

    private static void DrawLines(DrawingContext dc, Brush brush, double x1, double y, double x2, int count, double gap)
    {
        var pen = new Pen(brush, 0.06) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int i = 0; i < count; i++)
            dc.DrawLine(pen, new Point(x1 + i * 0.06, y + i * gap), new Point(x2 - i * 0.06, y + i * gap));
    }
}
