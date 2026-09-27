namespace CustomDock.Widgets;

/// <summary>
/// Width classes that give the dock an even rhythm: a square tile, a standard card and a wide card. With
/// "Even widget widths" on, a card is at least as wide as its class (content that needs more still gets it, so
/// nothing is clipped). The widths are whole steps of the half-height grid the cards snap to, and the dock's size
/// scales them with everything else.
/// </summary>
public enum WidgetWidth { Auto, Compact, Standard, Wide }

public static class WidgetWidths
{
    /// <summary>Card height in the unscaled dock (see the WidgetCard style).</summary>
    public const double CardHeight = 46;

    /// <summary>Smallest card width of a class, in units of the card height: 1, 2.5 and 4.</summary>
    public static double MinCardWidth(WidgetWidth width, double cardHeight = CardHeight) => width switch
    {
        WidgetWidth.Compact => cardHeight,
        WidgetWidth.Standard => cardHeight * 2.5,
        WidgetWidth.Wide => cardHeight * 4,
        _ => 0,
    };
}
