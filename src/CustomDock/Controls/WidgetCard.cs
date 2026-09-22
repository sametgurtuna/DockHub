using System.Windows;
using System.Windows.Controls;

namespace CustomDock.Controls;

/// <summary>Rounded-corner widget card inside the dock.</summary>
public class WidgetCard : ContentControl
{
    public static readonly DependencyProperty HoverEnabledProperty = DependencyProperty.Register(
        nameof(HoverEnabled), typeof(bool), typeof(WidgetCard), new PropertyMetadata(true));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(WidgetCard), new PropertyMetadata(new CornerRadius(10)));

    public bool HoverEnabled { get => (bool)GetValue(HoverEnabledProperty); set => SetValue(HoverEnabledProperty, value); }

    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
}
