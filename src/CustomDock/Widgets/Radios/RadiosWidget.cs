using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;
using Windows.Devices.Radios;

namespace CustomDock.Widgets;

/// <summary>Wi-Fi and Bluetooth switches, like the Quick Settings buttons.</summary>
public sealed class RadiosWidget : WidgetBase
{
    public const string Icon = "M5,12.5 A10,10 0 0 1 19,12.5 M8,15.5 A5.5,5.5 0 0 1 16,15.5 M12,19 H12.01 M2,9.5 A14,14 0 0 1 22,9.5";
    private const string WiFiGlyph = "";
    private const string BluetoothGlyph = "";

    private readonly StackPanel _buttonsLayout;
    private readonly StackPanel _iconsLayout;
    private readonly List<(RadioKind Kind, Border Disc, TextBlock Glyph, TextBlock? Label)> _toggles = new();

    public RadiosWidget()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        _buttonsLayout = new StackPanel { Name = "Layout_buttons", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _iconsLayout = new StackPanel { Name = "Layout_icons", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (kind, glyph, name) in new[] { (RadioKind.WiFi, WiFiGlyph, "Wi-Fi"), (RadioKind.Bluetooth, BluetoothGlyph, "Bluetooth") })
        {
            var button = CreateToggle(kind, glyph, name, withLabel: true);
            var icon = CreateToggle(kind, glyph, name, withLabel: false);
            if (_buttonsLayout.Children.Count > 0)
            {
                button.Margin = new Thickness(14, 0, 0, 0);
                icon.Margin = new Thickness(6, 0, 0, 0);
            }
            _buttonsLayout.Children.Add(button);
            _iconsLayout.Children.Add(icon);
        }
        Content = new Grid { Background = System.Windows.Media.Brushes.Transparent, Children = { _buttonsLayout, _iconsLayout } };
    }

    private static RadioService Radios => AppServices.Radios;

    private FrameworkElement CreateToggle(RadioKind kind, string glyph, string name, bool withLabel)
    {
        var (disc, icon) = WidgetUi.IconDisc(glyph, "TextPrimaryBrush", withLabel ? 30 : 34);
        TextBlock? label = null;
        FrameworkElement element = disc;
        if (withLabel)
        {
            label = WidgetUi.Text("CaptionText");
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(7, 0, 0, 0);
            var title = WidgetUi.Text("TitleText", name, 12);
            element = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { disc, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0), Children = { title, label } } },
            };
            label.Margin = new Thickness(0);
        }
        element.Cursor = Cursors.Hand;
        element.MouseLeftButtonUp += async (_, e) =>
        {
            if (DockDragHelper.JustDragged || IsPreview) return;
            e.Handled = true;
            if (!await Radios.ToggleAsync(kind))
                NetworkStatusIconView.OpenSettings(kind == RadioKind.WiFi ? "ms-settings:network-wifi" : "ms-settings:bluetooth");
        };
        _toggles.Add((kind, disc, icon, label));
        return element;
    }

    protected override void OnAttached()
    {
        if (IsPreview) return;
        Radios.Changed += Render;
        _ = Radios.EnsureStartedAsync();
    }

    protected override void OnDetached()
    {
        if (IsPreview) return;
        Radios.Changed -= Render;
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(_buttonsLayout, _iconsLayout);
        Render();
    }

    private bool? StateOf(RadioKind kind) => IsPreview ? kind == RadioKind.WiFi : kind == RadioKind.WiFi ? Radios.WiFi : Radios.Bluetooth;

    private static string Describe(bool? state) => state switch
    {
        true => L.T("On"),
        false => L.T("Off"),
        null => L.T("Not available"),
    };

    private void Render()
    {
        foreach (var (kind, disc, glyph, label) in _toggles)
        {
            bool? state = StateOf(kind);
            if (state == true)
            {
                disc.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                glyph.SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
            }
            else
            {
                disc.SetResourceReference(Border.BackgroundProperty, "SubtleFillBrush");
                glyph.SetResourceReference(TextBlock.ForegroundProperty, state is null ? "TextTertiaryBrush" : "TextPrimaryBrush");
            }
            string name = kind == RadioKind.WiFi ? "Wi-Fi" : "Bluetooth";
            if (label is not null) label.Text = Describe(state);
            disc.ToolTip = $"{name}: {Describe(state)}\n" + (Radios.AccessDenied && !IsPreview ? L.T("Click to open settings") : L.T("Click to turn on or off"));
        }
        ToolTip = null;
        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        tile.ShowGlyph(Descriptor.Icon, StateOf(RadioKind.WiFi) == true ? "AccentBlueBrush" : "TextTertiaryBrush");
        tile.Text = StateOf(RadioKind.Bluetooth) == true ? "BT" : null;
        tile.ToolTip = $"Wi-Fi: {Describe(StateOf(RadioKind.WiFi))}\nBluetooth: {Describe(StateOf(RadioKind.Bluetooth))}";
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(L.T("Wi-Fi settings"), "", () => NetworkStatusIconView.OpenSettings("ms-settings:network-wifi")));
        items.Add(DockMenu.Item(L.T("Bluetooth settings"), "", () => NetworkStatusIconView.OpenSettings("ms-settings:bluetooth")));
        items.Add(DockMenu.Item(L.T("Airplane mode"), "", () => NetworkStatusIconView.OpenSettings("ms-settings:network-airplanemode")));
    }
}
