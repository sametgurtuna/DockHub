using System.Windows;
using System.Windows.Media;

namespace CustomDock.Controls;

/// <summary>Küçük analog saat. Gündüz açık, gece koyu kadran kullanır.</summary>
public sealed class AnalogClock : FrameworkElement
{
    public static readonly DependencyProperty TimeProperty = DependencyProperty.Register(
        nameof(Time), typeof(DateTime), typeof(AnalogClock),
        new FrameworkPropertyMetadata(DateTime.MinValue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowSecondsProperty = DependencyProperty.Register(
        nameof(ShowSeconds), typeof(bool), typeof(AnalogClock),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(AnalogClock),
        new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DayFaceProperty = DependencyProperty.Register(
        nameof(DayFace), typeof(Brush), typeof(AnalogClock),
        new FrameworkPropertyMetadata(Brushes.WhiteSmoke, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NightFaceProperty = DependencyProperty.Register(
        nameof(NightFace), typeof(Brush), typeof(AnalogClock),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AlwaysDarkProperty = DependencyProperty.Register(
        nameof(AlwaysDark), typeof(bool), typeof(AnalogClock),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Gündüz/gece ayrımı yapmadan her zaman koyu kadran kullanır.</summary>
    public bool AlwaysDark { get => (bool)GetValue(AlwaysDarkProperty); set => SetValue(AlwaysDarkProperty, value); }

    private static readonly Brush DarkInk = Frozen(Color.FromRgb(0x1C, 0x1C, 0x1E));
    private static readonly Brush LightInk = Frozen(Color.FromRgb(0xF2, 0xF2, 0xF7));
    private static readonly Brush DarkTicks = Frozen(Color.FromArgb(0x80, 0x1C, 0x1C, 0x1E));
    private static readonly Brush LightTicks = Frozen(Color.FromArgb(0x80, 0xF2, 0xF2, 0xF7));
    private static readonly Brush FaceOutline = Frozen(Color.FromArgb(0x33, 0x80, 0x80, 0x88));

    public DateTime Time { get => (DateTime)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }
    public bool ShowSeconds { get => (bool)GetValue(ShowSecondsProperty); set => SetValue(ShowSecondsProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush DayFace { get => (Brush)GetValue(DayFaceProperty); set => SetValue(DayFaceProperty, value); }
    public Brush NightFace { get => (Brush)GetValue(NightFaceProperty); set => SetValue(NightFaceProperty, value); }

    public static bool IsDaytime(DateTime time) => time.Hour is >= 6 and < 18;

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    protected override Size MeasureOverride(Size availableSize) => Ui.Fit(availableSize, 36, 36);

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var time = Time == DateTime.MinValue ? DateTime.Now : Time;
        bool day = !AlwaysDark && IsDaytime(time);
        var ink = day ? DarkInk : LightInk;
        var ticks = day ? DarkTicks : LightTicks;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size / 2;

        // İnce çerçeve: açık kadran açık zeminde de seçilebilsin.
        dc.DrawEllipse(day ? DayFace : NightFace, new Pen(FaceOutline, 1), c, r - 0.5, r - 0.5);

        var tickPen = new Pen(ticks, Math.Max(1, size / 36));
        for (int i = 0; i < 12; i++)
        {
            double a = i * 30;
            double inner = i % 3 == 0 ? r * 0.72 : r * 0.8;
            dc.DrawLine(tickPen, Polar(c, inner, a), Polar(c, r * 0.9, a));
        }

        double minutes = time.Minute + time.Second / 60.0;
        double hours = time.Hour % 12 + minutes / 60.0;

        var hourPen = new Pen(ink, size / 14) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var minutePen = new Pen(ink, size / 20) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(hourPen, c, Polar(c, r * 0.48, hours * 30));
        dc.DrawLine(minutePen, c, Polar(c, r * 0.72, minutes * 6));

        if (ShowSeconds)
        {
            var secondPen = new Pen(Accent, Math.Max(1, size / 40)) { EndLineCap = PenLineCap.Round };
            dc.DrawLine(secondPen, Polar(c, -r * 0.15, time.Second * 6), Polar(c, r * 0.8, time.Second * 6));
        }

        dc.DrawEllipse(Accent, null, c, size / 22, size / 22);
    }

    private static Point Polar(Point c, double radius, double angleDegrees)
    {
        double rad = (angleDegrees - 90) * Math.PI / 180.0;
        return new Point(c.X + radius * Math.Cos(rad), c.Y + radius * Math.Sin(rad));
    }
}
