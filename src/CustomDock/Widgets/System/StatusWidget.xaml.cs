using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Widgets;

public sealed class StatusSettings : ObservableObject
{
    private bool _showBattery = true;
    private bool _showDisk = true;
    private bool _showMemory = true;
    private bool _showCpu = true;

    public bool ShowBattery { get => _showBattery; set => Set(ref _showBattery, value); }

    public bool ShowDisk { get => _showDisk; set => Set(ref _showDisk, value); }

    public bool ShowMemory { get => _showMemory; set => Set(ref _showMemory, value); }

    public bool ShowCpu { get => _showCpu; set => Set(ref _showCpu, value); }
}

/// <summary>Battery / disk / memory / CPU utilization rings (Dockset "Status" view).</summary>
public partial class StatusWidget : WidgetBase
{
    private sealed class Cell
    {
        public required DeviceGlyphKind Kind { get; init; }
        public required RingGauge Ring { get; init; }
        public required TextBlock Inner { get; init; }
        public required TextBlock Caption { get; init; }
        public required FrameworkElement Root { get; init; }
    }

    private readonly List<Cell> _cells = new();
    private StatusSettings _settings = new();

    public StatusWidget()
    {
        InitializeComponent();
    }

    protected override void OnAttached()
    {
        _settings = GetSettings<StatusSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        AppServices.SystemMonitor.Updated += OnUpdated;
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        AppServices.SystemMonitor.Updated -= OnUpdated;
    }

    protected override void OnVariantChanged() => Rebuild();

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        Panel.Children.Clear();
        _cells.Clear();
        var stats = AppServices.SystemMonitor.Current;

        var kinds = new List<DeviceGlyphKind>();
        if (_settings.ShowBattery && (stats.BatteryPercent is not null || AppServices.SystemMonitor.Current.RamTotalGb == 0)) kinds.Add(DeviceGlyphKind.Battery);
        if (_settings.ShowDisk) kinds.Add(DeviceGlyphKind.Disk);
        if (_settings.ShowMemory) kinds.Add(DeviceGlyphKind.Memory);
        if (_settings.ShowCpu) kinds.Add(DeviceGlyphKind.Cpu);

        foreach (var kind in kinds)
        {
            var cell = CreateCell(kind);
            _cells.Add(cell);
            Panel.Children.Add(cell.Root);
        }

        OnUpdated(this, stats);
    }

    private Cell CreateCell(DeviceGlyphKind kind)
    {
        bool iconsOnly = Variant == "icons";
        bool percentInside = Variant == "percent";
        double size = iconsOnly ? 32 : 26;

        var ring = new RingGauge { Width = size, Height = size, Thickness = iconsOnly ? 3.5 : 3 };
        ring.SetResourceReference(RingGauge.FillProperty, "AccentGreenBrush");
        ring.SetResourceReference(RingGauge.TrackProperty, "GreenTrackBrush");

        var glyph = new DeviceGlyph { Kind = kind, Width = iconsOnly ? 15 : 13, Height = iconsOnly ? 15 : 13 };
        glyph.SetResourceReference(DeviceGlyph.StrokeProperty, "TextPrimaryBrush");

        var inner = new TextBlock
        {
            FontSize = 8.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)FindResource("DisplayFont"),
        };
        inner.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        var ringHost = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center };
        ringHost.Children.Add(ring);
        ringHost.Children.Add(percentInside ? inner : glyph);

        var caption = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.None };
        caption.Style = (Style)FindResource("MicroText");
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var root = new StackPanel { Width = iconsOnly ? 40 : 38, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        root.Children.Add(ringHost);
        if (!iconsOnly)
            root.Children.Add(caption);

        return new Cell { Kind = kind, Ring = ring, Inner = inner, Caption = caption, Root = root };
    }

    private void OnUpdated(object? sender, SystemStats stats)
    {
        var tooltip = new List<string>();
        foreach (var cell in _cells)
        {
            (double value, string label) = Describe(cell.Kind, stats);

            cell.Ring.Value = value;
            string text = Math.Round(value).ToString(System.Globalization.CultureInfo.CurrentCulture);
            cell.Inner.Text = text;
            cell.Inner.FontSize = text.Length > 2 ? 7.5 : 8.5;
            cell.Caption.Text = Variant == "percent" ? ShortLabel(cell.Kind, stats) : text + "%";
            tooltip.Add($"{label}: {text}%");

            bool warn = cell.Kind == DeviceGlyphKind.Battery ? value < 20 && !stats.IsCharging : value >= 90;
            cell.Ring.SetResourceReference(RingGauge.FillProperty, warn ? "AccentRedBrush" : "AccentGreenBrush");
        }

        if (stats.BatteryPercent is null && _cells.Any(c => c.Kind == DeviceGlyphKind.Battery) && stats.RamTotalGb > 0)
        {
            // No battery (desktop PC): remove battery ring.
            Rebuild();
            return;
        }

        ToolTip = string.Join("\n", tooltip);
        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var stats = AppServices.SystemMonitor.Current;
        if (_cells.FirstOrDefault() is not { } cell)
        {
            tile.ShowGlyph(Descriptor.Icon, Descriptor.AccentKey);
            tile.Text = null;
            return;
        }

        var (value, _) = Describe(cell.Kind, stats);
        bool warn = cell.Kind == DeviceGlyphKind.Battery ? value < 20 && !stats.IsCharging : value >= 90;
        tile.ShowRing(value, 100, warn ? "AccentRedBrush" : "AccentGreenBrush", "GreenTrackBrush",
            Math.Round(value).ToString(System.Globalization.CultureInfo.CurrentCulture));
        tile.Text = ShortLabel(cell.Kind, stats);
    }

    private static string ShortLabel(DeviceGlyphKind kind, SystemStats stats) => kind switch
    {
        DeviceGlyphKind.Battery => stats.IsCharging ? "Chg" : "Bat",
        DeviceGlyphKind.Disk => "Disk",
        DeviceGlyphKind.Memory => "RAM",
        _ => "CPU",
    };

    private static (double Value, string Label) Describe(DeviceGlyphKind kind, SystemStats stats) => kind switch
    {
        DeviceGlyphKind.Battery => (stats.BatteryPercent ?? 0, "Battery"),
        DeviceGlyphKind.Disk => (stats.DiskPercent, "Disk (used)"),
        DeviceGlyphKind.Memory => (stats.RamPercent, "Memory"),
        _ => (stats.CpuPercent, "CPU"),
    };
}
