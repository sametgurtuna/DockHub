using System.Windows;

namespace CustomDock.Controls;

/// <summary>Attached properties used in templates.</summary>
public static class Ui
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Ui),
        new FrameworkPropertyMetadata(new CornerRadius(6)));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    /// <summary>
    /// Measurement for custom-drawn controls: constrains default size to available space.
    /// (Returning a fixed size larger than Width/Height causes WPF to clip the drawing.)
    /// </summary>
    internal static Size Fit(Size available, double width, double height) => new(
        double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width),
        double.IsInfinity(available.Height) ? height : Math.Min(height, available.Height));
}
