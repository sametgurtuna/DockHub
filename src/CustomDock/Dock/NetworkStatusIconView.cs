using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Dock;

/// <summary>
/// Always-on network status glyph (ethernet / Wi-Fi / disconnected) shown next to the pinned tray icons.
/// Unlike <see cref="TrayIconView"/>, this doesn't depend on Windows exposing a real notify icon for the connection.
/// </summary>
public sealed class NetworkStatusIconView : Border
{
    private readonly TextBlock _glyph;

    public NetworkStatusIconView()
    {
        Width = 28;
        Height = 46;
        Background = Brushes.Transparent;
        Focusable = false;

        var hover = new Border { CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 9, 0, 9) };
        _glyph = new TextBlock
        {
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var grid = new Grid();
        grid.Children.Add(hover);
        grid.Children.Add(_glyph);
        Child = grid;

        MouseEnter += (_, _) => hover.SetResourceReference(BackgroundProperty, "DockHoverBrush");
        MouseLeave += (_, _) => hover.Background = null;
        MouseLeftButtonUp += (_, _) =>
        {
            try { AppServices.Shell?.ShowQuickSettings(); }
            catch (Exception ex) { Log.Error(ex, "Failed to open quick settings from network icon"); }
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppServices.NetworkStatus.Changed += OnStatusChanged;
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => AppServices.NetworkStatus.Changed -= OnStatusChanged;

    private void OnStatusChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var status = AppServices.NetworkStatus.Status;
        (_glyph.Text, ToolTip) = status switch
        {
            NetworkStatusKind.Ethernet => ("", "Ethernet connected"),
            NetworkStatusKind.Wifi => ("", "Wi-Fi connected"),
            _ => ("", "No network connection"),
        };
        _glyph.Opacity = status == NetworkStatusKind.Disconnected ? 0.45 : 1.0;
    }
}
