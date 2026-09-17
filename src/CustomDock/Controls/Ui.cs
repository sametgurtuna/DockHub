using System.Windows;

namespace CustomDock.Controls;

/// <summary>Şablonlarda kullanılan ekli (attached) özellikler.</summary>
public static class Ui
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Ui),
        new FrameworkPropertyMetadata(new CornerRadius(6)));

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    /// <summary>
    /// Özel çizim kontrolleri için ölçü: varsayılan boyutu, verilen alanla sınırlar.
    /// (Sabit boyut döndürmek, Width/Height'tan büyükse WPF'in çizimi kırpmasına yol açar.)
    /// </summary>
    internal static Size Fit(Size available, double width, double height) => new(
        double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width),
        double.IsInfinity(available.Height) ? height : Math.Min(height, available.Height));
}
