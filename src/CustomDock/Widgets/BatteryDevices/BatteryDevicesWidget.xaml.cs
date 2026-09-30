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

public sealed class BatteryDevicesSettings : ObservableObject
{
    private string[] _order = Array.Empty<string>();
    private int _lowBatteryAlert = 15;

    /// <summary>Device ids in the order chosen in the panel; the first connected one is shown on the dock.</summary>
    public string[] Order { get => _order; set => Set(ref _order, value ?? Array.Empty<string>()); }

    /// <summary>Level in percent at which a notification warns about a device's battery; 0 turns it off.</summary>
    public int LowBatteryAlert { get => _lowBatteryAlert; set => Set(ref _lowBatteryAlert, Math.Clamp(value, 0, 50)); }
}

public partial class BatteryDevicesWidget : WidgetBase
{
    private const string HeadsetIconPath = "M12,3 A9,9 0 0 0 3,12 V18 A3,3 0 0 0 6,21 H7 A2,2 0 0 0 9,19 V15 A2,2 0 0 0 7,13 H5 V12 A7,7 0 0 1 19,12 V13 H17 A2,2 0 0 0 15,15 V19 A2,2 0 0 0 17,21 H18 A3,3 0 0 0 21,18 V12 A9,9 0 0 0 12,3 Z";
    private const string MouseIconPath = "M12,2 C8.7,2 6,4.7 6,8 V16 C6,19.3 8.7,22 12,22 C15.3,22 18,19.3 18,16 V8 C18,4.7 15.3,2 12,2 Z M11,4.1 V9 H8 V8 C8,5.8 9.3,4.1 11,4.1 Z M13,4.1 C14.7,4.1 16,5.8 16,8 V9 H13 V4.1 Z";
    private const string BatteryIconPath = "M4,7 H18 A2,2 0 0 1 20,9 V15 A2,2 0 0 1 18,17 H4 A2,2 0 0 1 2,15 V9 A2,2 0 0 1 4,7 Z M20,11 H22 V13 H20 Z";
    private const string KeyboardIconPath = "M3,6 H21 A1,1 0 0 1 22,7 V17 A1,1 0 0 1 21,18 H3 A1,1 0 0 1 2,17 V7 A1,1 0 0 1 3,6 Z M5,9 V11 H7 V9 Z M9,9 V11 H11 V9 Z M13,9 V11 H15 V9 Z M17,9 V11 H19 V9 Z M7,13.5 V15.5 H17 V13.5 Z";
    private const string ControllerIconPath = "M7,7 H17 C20,7 22,10 22,14 C22,17 20.5,18.5 19,18.5 C17.5,18.5 16.5,17 15.5,16 H8.5 C7.5,17 6.5,18.5 5,18.5 C3.5,18.5 2,17 2,14 C2,10 4,7 7,7 Z M6,10 V11.5 H4.5 V13 H6 V14.5 H7.5 V13 H9 V11.5 H7.5 V10 Z M15,11.5 A1,1 0 1 1 17,11.5 A1,1 0 1 1 15,11.5 Z M17,13.5 A1,1 0 1 1 19,13.5 A1,1 0 1 1 17,13.5 Z";

    private static readonly Geometry HeadsetGeometry = Geometry.Parse(HeadsetIconPath);
    private static readonly Geometry MouseGeometry = Geometry.Parse(MouseIconPath);
    private static readonly Geometry BatteryGeometry = Geometry.Parse(BatteryIconPath);
    private static readonly Geometry KeyboardGeometry = Geometry.Parse(KeyboardIconPath);
    private static readonly Geometry ControllerGeometry = Geometry.Parse(ControllerIconPath);

    private BatteryDevicesSettings _settings = new();

    // Every widget copy on the dock shares one warning state, so a device is announced once; the highest level wins.
    private static readonly LowBatteryAlerts s_alerts = new();
    private static readonly Dictionary<BatteryDevicesWidget, int> s_alertLevels = new();

    static BatteryDevicesWidget()
    {
        HeadsetGeometry.Freeze();
        MouseGeometry.Freeze();
        BatteryGeometry.Freeze();
        KeyboardGeometry.Freeze();
        ControllerGeometry.Freeze();
    }

    public BatteryDevicesWidget()
    {
        InitializeComponent();
    }

    protected override void OnAttached()
    {
        _settings = GetSettings<BatteryDevicesSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        if (!IsPreview) s_alertLevels[this] = _settings.LowBatteryAlert;
        AppServices.DeviceBattery.Updated += OnBatteryUpdated;
        Render();
    }

    protected override void OnDetached()
    {
        AppServices.DeviceBattery.Updated -= OnBatteryUpdated;
        _settings.PropertyChanged -= OnSettingsChanged;
        s_alertLevels.Remove(this);
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BatteryDevicesSettings.LowBatteryAlert) && s_alertLevels.ContainsKey(this))
            s_alertLevels[this] = _settings.LowBatteryAlert;
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_single, Layout_multi);
        Render();
    }

    private void OnBatteryUpdated(object? sender, EventArgs e)
    {
        WarnAboutLowBatteries();
        Render();
    }

    /// <summary>Sends one notification per device that just reached the low battery level.</summary>
    private static void WarnAboutLowBatteries()
    {
        int level = s_alertLevels.Count == 0 ? 0 : s_alertLevels.Values.Max();
        foreach (var device in s_alerts.Check(AppServices.DeviceBattery.Devices, level))
        {
            string tag = "battery-" + device.Id;
            if (tag.Length > 64) tag = tag[..64]; // toast tags are limited to 64 characters
            AppServices.Notifications.Show(L.T("{0} battery is low", device.Name),
                L.T("{0}% left. Charge it soon.", device.BatteryPercent), tag);
        }
    }

    /// <summary>Connected devices in the user's order; devices never ordered follow in scan order.</summary>
    private List<BatteryDeviceInfo> OrderedDevices()
    {
        var order = _settings.Order;
        return AppServices.DeviceBattery.Devices
            .Select((device, index) => (device, index))
            .OrderBy(x => Array.IndexOf(order, x.device.Id) is var rank and >= 0 ? rank : int.MaxValue)
            .ThenBy(x => x.index)
            .Select(x => x.device)
            .ToList();
    }

    private void Render()
    {
        var devices = OrderedDevices();
        var primary = devices.FirstOrDefault();

        // 1. Single view
        if (primary is not null)
        {
            SingleBatteryRing.Value = primary.BatteryPercent;
            var (fillKey, trackKey) = GetBrushKeys(primary.BatteryPercent, primary.IsCharging);
            SingleBatteryRing.SetResourceReference(RingGauge.FillProperty, fillKey);
            SingleBatteryRing.SetResourceReference(RingGauge.TrackProperty, trackKey);

            SingleIcon.Data = GetDeviceGeometry(primary);
            SingleDeviceName.Text = ShortName(primary);
            SingleBatteryText.Text = primary.IsCharging ? $"⚡ {primary.BatteryPercent}%" : $"{primary.BatteryPercent}%";
        }
        else
        {
            SingleBatteryRing.Value = 0;
            SingleBatteryRing.SetResourceReference(RingGauge.FillProperty, "TextTertiaryBrush");
            SingleIcon.Data = BatteryGeometry;
            SingleDeviceName.Text = L.T("No Devices");
            SingleBatteryText.Text = "—";
        }

        // 2. Multi view
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
            var lines = devices.Select(d => $"{d.Name}: {d.BatteryPercent}%" + (d.IsCharging ? $" ({L.T("Charging")})" : ""));
            ToolTip = string.Join("\n", lines);
        }
        else
        {
            ToolTip = L.T("No connected device battery found");
        }

        if (DevicesPopup.IsOpen) BuildDeviceList(devices);
        RefreshCompact();
    }

    private static string ShortName(BatteryDeviceInfo device)
    {
        string name = device.Name;
        int paren = name.IndexOf('(');
        return paren > 3 ? name[..paren].Trim() : name;
    }

    private static (string fillKey, string trackKey) GetBrushKeys(int percent, bool isCharging)
    {
        if (isCharging) return ("AccentCyanBrush", "BlueTrackBrush");
        if (percent > 50) return ("AccentGreenBrush", "GreenTrackBrush");
        if (percent > 20) return ("AccentYellowBrush", "YellowTrackBrush");
        return ("AccentRedBrush", "RedTrackBrush");
    }

    private static Geometry GetDeviceGeometry(BatteryDeviceInfo dev) => dev.EffectiveKind switch
    {
        BatteryDeviceKind.Headset => HeadsetGeometry,
        BatteryDeviceKind.Mouse => MouseGeometry,
        BatteryDeviceKind.Keyboard => KeyboardGeometry,
        BatteryDeviceKind.Controller => ControllerGeometry,
        _ => BatteryGeometry,
    };

    private static string KindLabel(BatteryDeviceInfo dev) => dev.EffectiveKind switch
    {
        BatteryDeviceKind.Headset => L.T("Headset"),
        BatteryDeviceKind.Mouse => L.T("Mouse"),
        BatteryDeviceKind.Keyboard => L.T("Keyboard"),
        BatteryDeviceKind.Controller => L.T("Controller"),
        _ => L.T("Battery"),
    };

    private static Grid CreateRing(BatteryDeviceInfo dev, double size, double iconSize, double thickness)
    {
        var grid = new Grid { Width = size, Height = size };
        var ring = new RingGauge { Thickness = thickness, Value = dev.BatteryPercent };
        var (fillKey, trackKey) = GetBrushKeys(dev.BatteryPercent, dev.IsCharging);
        ring.SetResourceReference(RingGauge.FillProperty, fillKey);
        ring.SetResourceReference(RingGauge.TrackProperty, trackKey);

        var path = new System.Windows.Shapes.Path
        {
            Data = GetDeviceGeometry(dev),
            Width = iconSize,
            Height = iconSize,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        path.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "TextPrimaryBrush");

        grid.Children.Add(ring);
        grid.Children.Add(path);
        return grid;
    }

    private FrameworkElement CreateMultiItem(BatteryDeviceInfo dev)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var ring = CreateRing(dev, 26, 11, 2.8);
        ring.Margin = new Thickness(0, 0, 5, 0);

        var text = new TextBlock
        {
            Text = $"{dev.BatteryPercent}%",
            Style = (Style)FindResource("CaptionText"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        panel.Children.Add(ring);
        panel.Children.Add(text);
        return panel;
    }

    // ------------------------------------------------------------------ Devices panel

    private void ToggleDevicesPanel()
    {
        if (!DevicesPopup.IsOpen)
        {
            BuildDeviceList(OrderedDevices());
            AppServices.DeviceBattery.Refresh();
        }
        TogglePopup(DevicesPopup);
    }

    private void BuildDeviceList(List<BatteryDeviceInfo> devices)
    {
        DeviceList.Children.Clear();
        if (devices.Count == 0)
        {
            DeviceList.Children.Add(WidgetUi.EmptyState("\uE83F", L.T("No connected device battery found"),
                L.T("Turn on a Bluetooth headset, mouse, keyboard or controller, or plug in its receiver."), L.T("Refresh"), AppServices.DeviceBattery.Refresh));
            return;
        }

        for (int i = 0; i < devices.Count; i++)
            DeviceList.Children.Add(CreateDeviceRow(devices[i], i, devices.Count));
    }

    private FrameworkElement CreateDeviceRow(BatteryDeviceInfo dev, int index, int count)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var ring = CreateRing(dev, 30, 12, 3);
        ring.Margin = new Thickness(0, 0, 10, 0);
        grid.Children.Add(ring);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = ShortName(dev),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = dev.Name,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        texts.Children.Add(name);

        var status = new List<string>();
        if (index == 0) status.Add(L.T("Shown on dock"));
        if (dev.IsCharging) status.Add(L.T("Charging"));
        if (status.Count > 0)
        {
            var caption = new TextBlock { Text = string.Join(" · ", status), FontSize = 10.5 };
            caption.SetResourceReference(TextBlock.ForegroundProperty, index == 0 ? "AccentBrush" : "TextSecondaryBrush");
            texts.Children.Add(caption);
        }
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var percent = new TextBlock
        {
            Text = $"{dev.BatteryPercent}%",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0),
        };
        percent.SetResourceReference(TextBlock.ForegroundProperty, GetBrushKeys(dev.BatteryPercent, dev.IsCharging).fillKey);
        Grid.SetColumn(percent, 2);
        grid.Children.Add(percent);

        var arrows = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        arrows.Children.Add(CreateMoveButton("", L.T("Move up"), index > 0, () => Move(dev, -1)));
        arrows.Children.Add(CreateMoveButton("", L.T("Move down"), index < count - 1, () => Move(dev, +1)));
        Grid.SetColumn(arrows, 3);
        grid.Children.Add(arrows);

        var row = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 7, 4, 7),
            Margin = new Thickness(0, 0, 0, index < count - 1 ? 6 : 0),
            Child = grid,
        };
        row.SetResourceReference(Border.BackgroundProperty, "SurfaceLightBrush");
        return row;
    }

    private Button CreateMoveButton(string glyph, string toolTip, bool enabled, Action onClick)
    {
        var button = new Button
        {
            Content = glyph,
            ToolTip = toolTip,
            IsEnabled = enabled,
            Style = (Style)FindResource("CardIconButton"),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Swaps the device with its visible neighbour, keeping disconnected devices where they were.</summary>
    private void Move(BatteryDeviceInfo device, int delta)
    {
        var visible = OrderedDevices();
        int from = visible.FindIndex(d => d.Id == device.Id);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= visible.Count) return;

        var order = _settings.Order.ToList();
        foreach (var d in visible)
            if (!order.Contains(d.Id)) order.Add(d.Id);

        int a = order.IndexOf(visible[from].Id), b = order.IndexOf(visible[to].Id);
        (order[a], order[b]) = (order[b], order[a]);
        _settings.Order = order.ToArray();
        Render();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => AppServices.DeviceBattery.Refresh();

    // Clicks inside the panel must not bubble up to the widget and toggle it closed.
    private void OnPopupMouseUp(object sender, MouseButtonEventArgs e) => e.Handled = true;

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (DockDragHelper.JustDragged || IsPreview || e.Handled) return;
        ToggleDevicesPanel();
        e.Handled = true;
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var primary = OrderedDevices().FirstOrDefault();
        if (primary is not null)
        {
            var (fillKey, trackKey) = GetBrushKeys(primary.BatteryPercent, primary.IsCharging);
            tile.ShowRing(primary.BatteryPercent, 100, fillKey, trackKey, primary.BatteryPercent.ToString(CultureInfo.CurrentCulture));
            tile.Text = KindLabel(primary);
        }
        else
        {
            tile.ShowGlyph(BatteryGeometry, "TextTertiaryBrush");
            tile.Text = "—";
        }
    }

    public override bool OnCompactClick()
    {
        ToggleDevicesPanel();
        return true;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(L.T("Show all devices"), "", ToggleDevicesPanel));
        items.Add(DockMenu.Item("Refresh now", "", () => AppServices.DeviceBattery.Refresh()));
        items.Add(DockMenu.Item("Bluetooth settings…", "", () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
            }
            catch { }
        }));
    }
}
