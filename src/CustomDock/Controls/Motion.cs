using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CustomDock.Controls;

/// <summary>Dock genelinde tutarlı, yumuşak animasyonlar.</summary>
public static class Motion
{
    private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction Spring = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });

    private static IEasingFunction Freeze(EasingFunctionBase easing)
    {
        easing.Freeze();
        return easing;
    }

    public static void Fade(UIElement element, double to, int milliseconds)
        => element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = EaseOut },
            HandoffBehavior.SnapshotAndReplace);

    public static void Scale(ScaleTransform transform, double to, int milliseconds, IEasingFunction? easing = null)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing ?? EaseOut };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// Dock öğelerinin (uygulama düğmesi, widget kartı) büyüme (fare üzerine gelince) ve kayma (yeniden sıralama)
    /// animasyonlarını aynı anda, birbirini bozmadan kullanabilmesi için paylaşılan dönüşüm çifti.
    /// </summary>
    public readonly record struct ItemTransform(ScaleTransform Magnify, TranslateTransform Reorder);

    /// <summary>Öğenin RenderTransform'unu (Ölçek + Kaydırma) döndürür; yoksa oluşturur.</summary>
    public static ItemTransform GetItemTransform(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup { Children: [ScaleTransform scale, TranslateTransform translate] })
            return new ItemTransform(scale, translate);

        var newScale = new ScaleTransform();
        var newTranslate = new TranslateTransform();
        element.RenderTransform = new TransformGroup { Children = { newScale, newTranslate } };
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        return new ItemTransform(newScale, newTranslate);
    }

    /// <summary>Öğe küçükten büyüyerek ve belirerek görünür (dock'a eklenen öğeler, açılan paneller).</summary>
    public static void Appear(FrameworkElement element, double fromScale = 0.82, int milliseconds = 260)
    {
        var (scale, _) = GetItemTransform(element);
        var grow = new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = Spring };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds * 0.7)) { EasingFunction = EaseOut });
    }

    /// <summary>
    /// Bir öğeyi eski konumundan (dx, dy uzaklıkta) yeni (düzenden gelen) konumuna kaydırarak taşır (FLIP tekniği).
    /// Dock'ta yeniden sıralama olduğunda öğelerin sıçramadan kayarak yer değiştirmesi için kullanılır.
    /// </summary>
    public static void SlideFrom(FrameworkElement element, Vector delta, int milliseconds = 260)
    {
        var (_, translate) = GetItemTransform(element);
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        translate.X = delta.X;
        translate.Y = delta.Y;

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, duration) { EasingFunction = EaseOut });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { EasingFunction = EaseOut });
    }

    /// <summary>Popup içeriği için: hafif kayma + belirme. <paramref name="offset"/> kaymanın yönüdür.</summary>
    public static void PopIn(FrameworkElement element, Vector offset, int milliseconds = 200)
    {
        var translate = new TranslateTransform(offset.X, offset.Y);
        var scale = new ScaleTransform(0.97, 0.97);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new TransformGroup { Children = { scale, translate } };

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, duration) { EasingFunction = EaseOut });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { EasingFunction = EaseOut });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration) { EasingFunction = EaseOut });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration) { EasingFunction = EaseOut });
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds * 0.75)) { EasingFunction = EaseOut });
    }
}
