using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CustomDock.Controls;

public enum MotionLevel { Full, Reduced, Off }

/// <summary>Consistent, smooth animations throughout the dock.</summary>
public static class Motion
{
    /// <summary>
    /// Reduced: no zoom, bounce, slide or genie effects, only short fades. Off: changes are instant.
    /// Follows Windows' "Animation effects" setting unless overridden in Settings.
    /// </summary>
    public static MotionLevel Level { get; set; } = MotionLevel.Full;

    public static bool IsReduced => Level != MotionLevel.Full;

    /// <summary>Quick fade used instead of movement when motion is reduced.</summary>
    private static void SimpleFade(UIElement element, double from, double to, Action? completed = null)
    {
        if (Level == MotionLevel.Off)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = to;
            completed?.Invoke();
            return;
        }
        var fade = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(120));
        if (completed is not null) fade.Completed += (_, _) => completed();
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }
    private static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    private static readonly IEasingFunction EaseIn = Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });
    private static readonly IEasingFunction Spring = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });
    private static readonly IEasingFunction MacSpring = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.16 });

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
        if (IsReduced)
        {
            // Hover and press zoom are decorative: skip them entirely.
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            transform.ScaleX = transform.ScaleY = 1;
            return;
        }
        ScaleCore(transform, to, milliseconds, easing);
    }

    private static void ScaleCore(ScaleTransform transform, double to, int milliseconds, IEasingFunction? easing)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = easing ?? EaseOut };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// Shared transform pair so dock items (app buttons, widget cards) can use magnification (on mouse hover)
    /// and translation (reordering) animations simultaneously without interfering with each other.
    /// </summary>
    public readonly record struct ItemTransform(ScaleTransform Magnify, TranslateTransform Reorder);

    /// <summary>Returns the item's RenderTransform (Scale + Translate); creates it if absent.</summary>
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

    /// <summary>Item appears by scaling up from smaller size and fading in (items added to dock, opened panels).</summary>
    public static void Appear(FrameworkElement element, double fromScale = 0.82, int milliseconds = 260)
    {
        if (IsReduced) { SimpleFade(element, 0, 1); return; }
        AppearCore(element, fromScale, milliseconds);
    }

    private static void AppearCore(FrameworkElement element, double fromScale, int milliseconds)
    {
        var (scale, _) = GetItemTransform(element);
        var grow = new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = Spring };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds * 0.7)) { EasingFunction = EaseOut });
    }

    /// <summary>
    /// Slides an item from its old position (dx, dy distance) to its new (layout-derived) position (FLIP technique).
    /// Used when reordering dock items so they slide smoothly into place without jumping.
    /// </summary>
    public static void SlideFrom(FrameworkElement element, Vector delta, int milliseconds = 260)
    {
        if (IsReduced) return;
        SlideFromCore(element, delta, milliseconds);
    }

    private static void SlideFromCore(FrameworkElement element, Vector delta, int milliseconds)
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

    /// <summary>macOS-style window/popover open: smooth zoom-out from origin, gentle spring overshoot, and fade-in.</summary>
    public static void PopIn(FrameworkElement element, Vector offset, Point origin, int milliseconds = 230)
    {
        if (IsReduced) { element.RenderTransform = Transform.Identity; SimpleFade(element, 0, 1); return; }
        PopInCore(element, offset, origin, milliseconds);
    }

    private static void PopInCore(FrameworkElement element, Vector offset, Point origin, int milliseconds)
    {
        var translate = new TranslateTransform(offset.X, offset.Y);
        var scale = new ScaleTransform(0.85, 0.85);
        element.RenderTransformOrigin = origin;
        element.RenderTransform = new TransformGroup { Children = { scale, translate } };
        element.Opacity = 0;

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, duration) { EasingFunction = EaseOut });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { EasingFunction = EaseOut });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration) { EasingFunction = MacSpring });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration) { EasingFunction = MacSpring });
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(milliseconds * 0.75)) { EasingFunction = EaseOut });
    }

    /// <summary>For popup content: subtle slide + fade in. <paramref name="offset"/> is slide direction.</summary>
    public static void PopIn(FrameworkElement element, Vector offset, int milliseconds = 230)
        => PopIn(element, offset, new Point(0.5, 0.5), milliseconds);

    /// <summary>macOS-style window/popover close: shrinks back towards dock origin and fades out cleanly.</summary>
    public static void PopOut(FrameworkElement element, Vector offset, Action? onCompleted = null, int milliseconds = 160)
    {
        if (IsReduced) { SimpleFade(element, element.Opacity, 0, onCompleted); return; }
        PopOutCore(element, offset, onCompleted, milliseconds);
    }

    private static void PopOutCore(FrameworkElement element, Vector offset, Action? onCompleted, int milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var tg = element.RenderTransform as TransformGroup;
        ScaleTransform scale;
        TranslateTransform translate;

        if (tg is { Children: [ScaleTransform s, TranslateTransform t] })
        {
            scale = s;
            translate = t;
        }
        else
        {
            scale = new ScaleTransform(1, 1);
            translate = new TranslateTransform(0, 0);
            element.RenderTransform = new TransformGroup { Children = { scale, translate } };
        }

        var scaleAnim = new DoubleAnimation(0.85, duration) { EasingFunction = EaseIn };
        var slideX = new DoubleAnimation(offset.X, duration) { EasingFunction = EaseIn };
        var slideY = new DoubleAnimation(offset.Y, duration) { EasingFunction = EaseIn };
        var fade = new DoubleAnimation(0, duration) { EasingFunction = EaseOut };

        bool completedFired = false;
        System.Windows.Threading.DispatcherTimer? safetyTimer = null;
        void Finish()
        {
            if (completedFired) return;
            completedFired = true;
            safetyTimer?.Stop();
            fade.Completed -= OnCompletedHandler;
            onCompleted?.Invoke();
        }

        void OnCompletedHandler(object? sender, EventArgs e) => Finish();

        fade.Completed += OnCompletedHandler;

        safetyTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds + 60) };
        safetyTimer.Tick += (_, _) => Finish();
        safetyTimer.Start();

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        translate.BeginAnimation(TranslateTransform.XProperty, slideX);
        translate.BeginAnimation(TranslateTransform.YProperty, slideY);
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>
    /// Fan-out animation: items appear one by one with staggered delay,
    /// sliding from center and scaling up with spring easing.
    /// </summary>
    public static void FanOut(FrameworkElement element, Vector offset, int index, int staggerMs = 50, int durationMs = 300)
    {
        if (IsReduced)
        {
            var (s, t) = GetItemTransform(element);
            s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            t.BeginAnimation(TranslateTransform.XProperty, null);
            t.BeginAnimation(TranslateTransform.YProperty, null);
            s.ScaleX = s.ScaleY = 1;
            t.X = t.Y = 0;
            SimpleFade(element, 0, 1);
            return;
        }
        FanOutCore(element, offset, index, staggerMs, durationMs);
    }

    private static void FanOutCore(FrameworkElement element, Vector offset, int index, int staggerMs, int durationMs)
    {
        element.Opacity = 0;
        var (scale, translate) = GetItemTransform(element);
        scale.ScaleX = scale.ScaleY = 0.3;
        translate.X = -offset.X;
        translate.Y = -offset.Y;

        var delay = TimeSpan.FromMilliseconds(index * staggerMs);
        var duration = TimeSpan.FromMilliseconds(durationMs);

        var scaleAnim = new DoubleAnimation(1, duration) { EasingFunction = Spring, BeginTime = delay };
        var slideX = new DoubleAnimation(0, duration) { EasingFunction = Spring, BeginTime = delay };
        var slideY = new DoubleAnimation(0, duration) { EasingFunction = Spring, BeginTime = delay };
        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(durationMs * 0.5)) { EasingFunction = EaseOut, BeginTime = delay };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        translate.BeginAnimation(TranslateTransform.XProperty, slideX);
        translate.BeginAnimation(TranslateTransform.YProperty, slideY);
        element.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }
}
