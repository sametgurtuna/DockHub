using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

public partial class BatteryDevicesWidget : WidgetBase
{
    private const string HeadsetIconPath = "M12,3 A9,9 0 0 0 3,12 V18 A3,3 0 0 0 6,21 H7 A2,2 0 0 0 9,19 V15 A2,2 0 0 0 7,13 H5 V12 A7,7 0 0 1 19,12 V13 H17 A2,2 0 0 0 15,15 V19 A2,2 0 0 0 17,21 H18 A3,3 0 0 0 21,18 V12 A9,9 0 0 0 12,3 Z";
    private const string MouseIconPath = "M12,2 C8.7,2 6,4.7 6,8 V16 C6,19.3 8.7,22 12,22 C15.3,22 18,19.3 18,16 V8 C18,4.7 15.3,2 12,2 Z M11,4.1 V9 H8 V8 C8,5.8 9.3,4.1 11,4.1 Z M13,4.1 C14.7,4.1 16,5.8 16,8 V9 H13 V4.1 Z";
    private const string BatteryIconPath = "M4,7 H18 A2,2 0 0 1 20,9 V15 A2,2 0 0 1 18,17 H4 A2,2 0 0 1 2,15 V9 A2,2 0 0 1 4,7 Z M20,11 H22 V13 H20 Z";

    private static readonly Geometry HeadsetGeometry = Geometry.Parse(HeadsetIconPath);
    private static readonly Geometry MouseGeometry = Geometry.Parse(MouseIconPath);
    private static readonly Geometry BatteryGeometry = Geometry.Parse(BatteryIconPath);

    static BatteryDevicesWidget()
    {
        HeadsetGeometry.Freeze();
        MouseGeometry.Freeze();
        BatteryGeometry.Freeze();
    }

    public BatteryDevicesWidget()
    {
        InitializeComponent();
    }

    protected override void OnAttached()
    {
        AppServices.DeviceBattery.Updated += OnBatteryUpdated;
        Render();
    }

    protected override void OnDetached()
    {
        AppServices.DeviceBattery.Updated -= OnBatteryUpdated;
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_single, Layout_multi);
        Render();
    }

    private void OnBatteryUpdated(object? sender, EventArgs e) => Render();

    private void Render()
    {
        var devices = AppServices.DeviceBattery.Devices;
        var primary = AppServices.DeviceBattery.PrimaryDevice;

        // 1. Tekli görünüm
        if (primary is not null)
        {
            string shortName = primary.Name;
            int paren = shortName.IndexOf('(');
            if (paren > 3) shortName = shortName[..paren].Trim();

            SingleBatteryRing.Value = primary.BatteryPercent;
            var (fillKey, trackKey) = GetBrushKeys(primary.BatteryPercent, primary.IsCharging);
            SingleBatteryRing.SetResourceReference(RingGauge.FillProperty, fillKey);
            SingleBatteryRing.SetResourceReference(RingGauge.TrackProperty, trackKey);

            SingleIcon.Data = GetDeviceGeometry(primary);
            SingleDeviceName.Text = shortName;
            SingleBatteryText.Text = primary.IsCharging ? $"%{primary.BatteryPercent} ⚡" : $"%{primary.BatteryPercent}";
        }
        else
        {
            SingleBatteryRing.Value = 0;
            SingleBatteryRing.SetResourceReference(RingGauge.FillProperty, "TextTertiaryBrush");
            SingleIcon.Data = BatteryGeometry;
            SingleDeviceName.Text = "Aygıt Yok";
            SingleBatteryText.Text = "—";
        }

        // 2. Çoklu görünüm
        MultiDevicesList.Items.Clear();
        if (devices.Count == 0)
        {
            MultiEmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            MultiEmptyText.Visibility = Visibility.Collapsed;
            foreach (var dev in devices.Take(3))
            {
                var item = CreateMultiItem(dev);
                MultiDevicesList.Items.Add(item);
            }
        }

        // Tooltip
        if (devices.Count > 0)
        {
            var lines = devices.Select(d => $"{d.Name}: %{d.BatteryPercent}" + (d.IsCharging ? " (Şarj oluyor)" : ""));
            ToolTip = string.Join("\n", lines);
        }
        else
        {
            ToolTip = "Bağlı aygıt pili bulunamadı";
        }

        RefreshCompact();
    }

    private static (string fillKey, string trackKey) GetBrushKeys(int percent, bool isCharging)
    {
        if (isCharging) return ("AccentCyanBrush", "BlueTrackBrush");
        if (percent > 50) return ("AccentGreenBrush", "GreenTrackBrush");
        if (percent > 20) return ("AccentYellowBrush", "YellowTrackBrush");
        return ("AccentRedBrush", "RedTrackBrush");
    }

    private static Geometry GetDeviceGeometry(BatteryDeviceInfo dev)
    {
        if (dev.IsHeadset) return HeadsetGeometry;
        if (dev.IsMouse) return MouseGeometry;
        return BatteryGeometry;
    }

    private FrameworkElement CreateMultiItem(BatteryDeviceInfo dev)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var grid = new Grid { Width = 26, Height = 26, Margin = new Thickness(0, 0, 5, 0) };
        var ring = new RingGauge { Thickness = 2.8, Value = dev.BatteryPercent };
        var (fillKey, trackKey) = GetBrushKeys(dev.BatteryPercent, dev.IsCharging);
        ring.SetResourceReference(RingGauge.FillProperty, fillKey);
        ring.SetResourceReference(RingGauge.TrackProperty, trackKey);

        var path = new System.Windows.Shapes.Path
        {
            Data = GetDeviceGeometry(dev),
            Width = 11,
            Height = 11,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        path.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "TextPrimaryBrush");

        grid.Children.Add(ring);
        grid.Children.Add(path);

        var text = new TextBlock
        {
            Text = $"%{dev.BatteryPercent}",
            Style = (Style)FindResource("CaptionText"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        panel.Children.Add(grid);
        panel.Children.Add(text);
        return panel;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (DockDragHelper.JustDragged) return;
        AppServices.DeviceBattery.Refresh();
        e.Handled = true;
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var primary = AppServices.DeviceBattery.PrimaryDevice;
        if (primary is not null)
        {
            var (fillKey, trackKey) = GetBrushKeys(primary.BatteryPercent, primary.IsCharging);
            tile.ShowRing(primary.BatteryPercent, 100, fillKey, trackKey, primary.BatteryPercent.ToString(CultureInfo.CurrentCulture));
            tile.Text = primary.IsHeadset ? "Kulaklık" : (primary.IsMouse ? "Fare" : "Pil");
        }
        else
        {
            tile.ShowGlyph(BatteryGeometry, "TextTertiaryBrush");
            tile.Text = "—";
        }
    }

    public override bool OnCompactClick()
    {
        AppServices.DeviceBattery.Refresh();
        return true;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item("Şimdi yenile", "\uE72C", () => AppServices.DeviceBattery.Refresh()));
        items.Add(DockMenu.Item("Bluetooth ayarları…", "\uE702", () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
            }
            catch { }
        }));
    }
}
