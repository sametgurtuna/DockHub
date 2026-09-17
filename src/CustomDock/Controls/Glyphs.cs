using System.Windows;
using System.Windows.Media;

namespace CustomDock.Controls;

/// <summary>Windows 11 tarzı dört kareli logo.</summary>
public sealed class WindowsLogo : FrameworkElement
{
    private static readonly Brush LogoBrush = CreateBrush();

    private static Brush CreateBrush()
    {
        var brush = new LinearGradientBrush(Color.FromRgb(0x4C, 0xC2, 0xFF), Color.FromRgb(0x00, 0x67, 0xC0), new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    protected override Size MeasureOverride(Size availableSize) => Ui.Fit(availableSize, 20, 20);

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        double ox = (ActualWidth - s) / 2, oy = (ActualHeight - s) / 2;
        double gap = s * 0.06;
        double cell = (s - gap) / 2;
        double r = s * 0.04;
        dc.PushOpacityMask(null);
        foreach (var (x, y) in new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0) })
        {
            var rect = new Rect(ox + x * (cell + gap), oy + y * (cell + gap), cell, cell);
            dc.DrawRoundedRectangle(LogoBrush, null, rect, r, r);
        }
        dc.Pop();
    }
}

public enum DeviceGlyphKind { Battery, Disk, Memory, Cpu, Laptop }

/// <summary>Durum widget'ı için küçük çizgi ikonlar.</summary>
public sealed class DeviceGlyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(DeviceGlyphKind), typeof(DeviceGlyph),
        new FrameworkPropertyMetadata(DeviceGlyphKind.Laptop, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(DeviceGlyph),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public DeviceGlyphKind Kind { get => (DeviceGlyphKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    private static readonly Dictionary<DeviceGlyphKind, Geometry> Shapes = new()
    {
        [DeviceGlyphKind.Laptop] = Parse("M3,5 H13 V11 H3 Z M1,13 H15"),
        [DeviceGlyphKind.Battery] = Parse("M2,5 H13 V11 H2 Z M14,7 V9"),
        [DeviceGlyphKind.Disk] = Parse("M3,4 C3,2 13,2 13,4 V12 C13,14 3,14 3,12 Z M3,4 C3,6 13,6 13,4"),
        [DeviceGlyphKind.Memory] = Parse("M2,5 H14 V10 H2 Z M4,10 V12 M7,10 V12 M10,10 V12 M13,10 V12 M5,7 H6 M8,7 H9 M11,7 H12"),
        [DeviceGlyphKind.Cpu] = Parse("M4,4 H12 V12 H4 Z M6.5,6.5 H9.5 V9.5 H6.5 Z M6,2 V4 M10,2 V4 M6,12 V14 M10,12 V14 M2,6 H4 M2,10 H4 M12,6 H14 M12,10 H14"),
    };

    private static Geometry Parse(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    protected override Size MeasureOverride(Size availableSize) => Ui.Fit(availableSize, 16, 16);

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - s) / 2, (ActualHeight - s) / 2));
        dc.PushTransform(new ScaleTransform(s / 16, s / 16));
        var pen = new Pen(Stroke, 1.3) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, Shapes[Kind]);
        dc.Pop();
        dc.Pop();
    }
}
