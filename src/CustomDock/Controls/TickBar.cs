using System.Windows;
using System.Windows.Media;

namespace CustomDock.Controls;

/// <summary>İnce dikey çizgilerden oluşan "barkod" ilerleme çubuğu (Dockset zaman ilerlemesi stili).</summary>
public sealed class TickBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(TickBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OnBrushProperty = DependencyProperty.Register(
        nameof(OnBrush), typeof(Brush), typeof(TickBar),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OffBrushProperty = DependencyProperty.Register(
        nameof(OffBrush), typeof(Brush), typeof(TickBar),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TickWidthProperty = DependencyProperty.Register(
        nameof(TickWidth), typeof(double), typeof(TickBar),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(TickBar),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0-1 arası doluluk.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush OnBrush { get => (Brush)GetValue(OnBrushProperty); set => SetValue(OnBrushProperty, value); }
    public Brush OffBrush { get => (Brush)GetValue(OffBrushProperty); set => SetValue(OffBrushProperty, value); }
    public double TickWidth { get => (double)GetValue(TickWidthProperty); set => SetValue(TickWidthProperty, value); }
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
        => new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, 14);

    protected override void OnRender(DrawingContext dc)
    {
        double step = TickWidth + Gap;
        if (ActualWidth < step || ActualHeight <= 0) return;

        int count = (int)((ActualWidth + Gap) / step);
        double offset = (ActualWidth - (count * step - Gap)) / 2;
        int filled = (int)Math.Round(Math.Clamp(Value, 0, 1) * count);
        double radius = Math.Min(1, TickWidth / 2);

        for (int i = 0; i < count; i++)
        {
            var rect = new Rect(offset + i * step, 0, TickWidth, ActualHeight);
            dc.DrawRoundedRectangle(i < filled ? OnBrush : OffBrush, null, rect, radius, radius);
        }
    }
}
