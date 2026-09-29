namespace CustomDock.Widgets;

/// <summary>Width of a widget panel: the three sizes every panel picks from.</summary>
public enum FlyoutSize { Narrow, Standard, Wide }

/// <summary>Measures shared by all widget panels (see <see cref="Controls.WidgetFlyout"/>).</summary>
public static class WidgetFlyoutLayout
{
    public const double NarrowWidth = 280;
    public const double StandardWidth = 340;
    public const double WideWidth = 420;

    /// <summary>Corner radius of a panel.</summary>
    public const double CornerRadius = 14;

    public static double WidthOf(FlyoutSize size) => size switch
    {
        FlyoutSize.Narrow => NarrowWidth,
        FlyoutSize.Wide => WideWidth,
        _ => StandardWidth,
    };

    /// <summary>The size closest to a width a panel used to have (panels built in code still pass one).</summary>
    public static FlyoutSize SizeFor(double width) =>
        width <= (NarrowWidth + StandardWidth) / 2 ? FlyoutSize.Narrow
        : width <= (StandardWidth + WideWidth) / 2 ? FlyoutSize.Standard
        : FlyoutSize.Wide;
}
