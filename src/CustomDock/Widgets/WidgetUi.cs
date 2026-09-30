using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CustomDock.Controls;

namespace CustomDock.Widgets;

/// <summary>Building blocks for widgets whose layout is written in code (same styles as the XAML widgets).</summary>
internal static class WidgetUi
{
    public static TextBlock Text(string style, string text = "", double? fontSize = null)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(FrameworkElement.StyleProperty, style);
        if (fontSize is { } size) block.FontSize = size;
        return block;
    }

    public static TextBlock Glyph(string glyph, double size = 15, string brush = "TextPrimaryBrush")
    {
        var block = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        block.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return block;
    }

    public static Ellipse Dot(string brush)
    {
        var dot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 5, 0) };
        dot.SetResourceReference(Shape.FillProperty, brush);
        return dot;
    }

    /// <summary>Value on top, a colored dot and a caption below (the "Numbers" layout of the system widgets).</summary>
    public static (StackPanel Panel, TextBlock Value) Number(string caption, string brush, double minWidth = 56)
    {
        var value = Text("ValueText", "", 19);
        value.LineHeight = 22;
        var label = new StackPanel { Orientation = Orientation.Horizontal, Children = { Dot(brush), Text("CaptionText", caption) } };
        return (new StackPanel { MinWidth = minWidth, Children = { value, label } }, value);
    }

    public static (StackPanel Panel, RingGauge Ring, TextBlock Value) Ring(string caption, string fill, string track)
    {
        var ring = new RingGauge { Thickness = 3.5 };
        ring.SetResourceReference(RingGauge.FillProperty, fill);
        ring.SetResourceReference(RingGauge.TrackProperty, track);
        var value = new TextBlock { FontSize = 8.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        value.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        value.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var label = Text("MicroText", caption);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.Margin = new Thickness(0, 1, 0, 0);
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var panel = new StackPanel { Width = 46, Children = { new Grid { Width = 28, Height = 28, Children = { ring, value } }, label } };
        return (panel, ring, value);
    }

    /// <summary>A labelled progress bar row: caption, track with fill, value text.</summary>
    public static (Grid Row, Border Fill, Grid Track, TextBlock Value) Bar(string caption, string fill, string track, double width = 172)
    {
        var grid = new Grid { Width = width, Height = 19 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        var label = Text("CaptionText", caption);
        label.VerticalAlignment = VerticalAlignment.Center;
        var trackGrid = new Grid { Height = 5, VerticalAlignment = VerticalAlignment.Center };
        var back = new Border { CornerRadius = new CornerRadius(2.5) };
        back.SetResourceReference(Border.BackgroundProperty, track);
        var bar = new Border { CornerRadius = new CornerRadius(2.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        bar.SetResourceReference(Border.BackgroundProperty, fill);
        trackGrid.Children.Add(back);
        trackGrid.Children.Add(bar);
        Grid.SetColumn(trackGrid, 1);
        var value = Text("TitleText");
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 2);
        grid.Children.Add(label);
        grid.Children.Add(trackGrid);
        grid.Children.Add(value);
        return (grid, bar, trackGrid, value);
    }

    public static void AnimateWidth(FrameworkElement element, double width)
    {
        width = Math.Max(0, width);
        if (!element.IsVisible || double.IsNaN(element.Width))
        {
            element.Width = width;
            return;
        }
        element.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>Small round icon button (media-style) used inside widget cards.</summary>
    public static Button IconButton(string glyph, string tooltip, double size = 30, double fontSize = 14)
    {
        var button = new Button
        {
            Content = glyph,
            Width = size,
            Height = size,
            FontSize = fontSize,
            Padding = new Thickness(0),
            ToolTip = tooltip,
            Focusable = false,
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "DockButton");
        return button;
    }

    /// <summary>
    /// A widget panel built in code (<see cref="WidgetFlyout"/> in a popup): add controls to the returned panel. Add
    /// the popup to the widget's own panel so it picks up the dock's theme resources.
    /// </summary>
    public static (Popup Popup, WidgetFlyout Flyout, StackPanel Content) Flyout(FlyoutSize size, string? title = null,
        string? icon = null, object? headerActions = null)
    {
        var content = new StackPanel();
        var flyout = new WidgetFlyout { Size = size, Title = title, Icon = icon, HeaderActions = headerActions, Content = content };
        var popup = new Popup
        {
            StaysOpen = true,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.None,
            Placement = PlacementMode.Top,
            Child = flyout,
        };
        return (popup, flyout, content);
    }

    /// <summary>A panel without a title (the caller adds its own header), in the size closest to <paramref name="width"/>.</summary>
    public static (Popup Popup, StackPanel Content) PopupShell(double width)
    {
        var (popup, _, content) = Flyout(WidgetFlyoutLayout.SizeFor(width));
        return (popup, content);
    }

    /// <summary>
    /// What a panel shows when it has nothing to list: an icon, a title, an explanation and, if given, a button that
    /// does something about it.
    /// </summary>
    public static StackPanel EmptyState(string glyph, string title, string? description = null, string? actionText = null, Action? action = null)
    {
        var panel = new StackPanel { Margin = new Thickness(12, 14, 12, 10), HorizontalAlignment = HorizontalAlignment.Center };
        var icon = Glyph(glyph, 22, "TextTertiaryBrush");
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(icon);
        var heading = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        panel.Children.Add(heading);
        if (!string.IsNullOrEmpty(description))
        {
            var text = new TextBlock { Text = description, FontSize = 11.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            panel.Children.Add(text);
        }
        if (actionText is not null && action is not null)
        {
            var button = new Button { Content = actionText, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => action();
            panel.Children.Add(button);
        }
        return panel;
    }

    /// <summary>Round icon disc (the "Icon only" layout used by several widgets).</summary>
    public static (Border Disc, TextBlock Glyph) IconDisc(string glyph, string brush, double size = 36)
    {
        var icon = Glyph(glyph, size * 0.45, brush);
        var disc = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Child = icon };
        disc.SetResourceReference(Border.BackgroundProperty, "SubtleFillBrush");
        return (disc, icon);
    }

    /// <summary>A clickable row in a flyout list: hover highlight and hand cursor.</summary>
    public static Border HoverRow(UIElement child, Action onClick)
    {
        var row = new Border
        {
            Padding = new Thickness(8, 7, 8, 7),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Child = child,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            onClick();
        };
        return row;
    }
}
