using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Widgets;

public sealed class GpuSettings : ObservableObject
{
    private int _updateIntervalSeconds = 2;

    public int UpdateIntervalSeconds { get => _updateIntervalSeconds; set => Set(ref _updateIntervalSeconds, Math.Clamp(value, 1, 30)); }
}

/// <summary>GPU load and video memory (numbers / rings / bars), like the CPU and memory widget.</summary>
public sealed class GpuWidget : WidgetBase
{
    private readonly StackPanel _numbers;
    private readonly TextBlock _loadNumber;
    private readonly TextBlock _memoryNumber;
    private readonly StackPanel _rings;
    private readonly RingGauge _loadRing;
    private readonly TextBlock _loadRingText;
    private readonly RingGauge _memoryRing;
    private readonly TextBlock _memoryRingText;
    private readonly StackPanel _bars;
    private readonly (Border Fill, Grid Track, TextBlock Value) _loadBar;
    private readonly (Border Fill, Grid Track, TextBlock Value) _memoryBar;
    private GpuSettings _settings = new();

    public GpuWidget()
    {
        var load = WidgetUi.Number("GPU", "AccentGreenBrush");
        var memory = WidgetUi.Number("VRAM", "AccentPurpleBrush");
        memory.Panel.Margin = new Thickness(18, 0, 0, 0);
        _loadNumber = load.Value;
        _memoryNumber = memory.Value;
        _numbers = new StackPanel { Name = "Layout_numbers", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { load.Panel, memory.Panel } };

        var loadRing = WidgetUi.Ring("GPU", "AccentGreenBrush", "GreenTrackBrush");
        var memoryRing = WidgetUi.Ring("VRAM", "AccentPurpleBrush", "BlueTrackBrush");
        memoryRing.Panel.Margin = new Thickness(10, 0, 0, 0);
        (_loadRing, _loadRingText) = (loadRing.Ring, loadRing.Value);
        (_memoryRing, _memoryRingText) = (memoryRing.Ring, memoryRing.Value);
        _rings = new StackPanel { Name = "Layout_rings", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { loadRing.Panel, memoryRing.Panel } };

        var loadBar = WidgetUi.Bar("GPU", "AccentGreenBrush", "GreenTrackBrush");
        var memoryBar = WidgetUi.Bar("VRAM", "AccentPurpleBrush", "BlueTrackBrush");
        _loadBar = (loadBar.Fill, loadBar.Track, loadBar.Value);
        _memoryBar = (memoryBar.Fill, memoryBar.Track, memoryBar.Value);
        _bars = new StackPanel { Name = "Layout_bars", VerticalAlignment = VerticalAlignment.Center, Children = { loadBar.Row, memoryBar.Row } };

        Content = new Grid { Background = System.Windows.Media.Brushes.Transparent, Children = { _numbers, _rings, _bars } };
    }

    private static GpuMonitorService Gpu => AppServices.Gpu;

    protected override void OnAttached()
    {
        _settings = GetSettings<GpuSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        Gpu.UpdateInterval = TimeSpan.FromSeconds(_settings.UpdateIntervalSeconds);
        Gpu.Updated += OnUpdated;
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        Gpu.Updated -= OnUpdated;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        => Gpu.UpdateInterval = TimeSpan.FromSeconds(_settings.UpdateIntervalSeconds);

    protected override void OnVariantChanged()
    {
        ShowLayout(_numbers, _rings, _bars);
        OnUpdated(this, Gpu.Current);
    }

    private void OnUpdated(object? sender, GpuStats stats)
    {
        var culture = CultureInfo.CurrentCulture;
        string load = stats.Available ? (stats.Utilization / 100).ToString("P0", culture) : "—";
        string memory = stats.Available ? stats.MemoryUsedGb.ToString("0.0", culture) + " GB" : "—";
        string memoryShort = stats.Available ? stats.MemoryUsedGb.ToString("0.#", culture) : "";

        switch (Variant)
        {
            case "rings":
                _loadRing.Value = stats.Utilization;
                _memoryRing.Value = stats.MemoryTotalGb > 0 ? stats.MemoryPercent : 0;
                _loadRingText.Text = stats.Available ? Math.Round(stats.Utilization).ToString(culture) : "";
                _memoryRingText.Text = memoryShort;
                break;
            case "bars":
                WidgetUi.AnimateWidth(_loadBar.Fill, _loadBar.Track.ActualWidth * stats.Utilization / 100);
                WidgetUi.AnimateWidth(_memoryBar.Fill, _memoryBar.Track.ActualWidth * stats.MemoryPercent / 100);
                _loadBar.Value.Text = load;
                _memoryBar.Value.Text = memoryShort.Length > 0 ? memoryShort + " G" : "—";
                break;
            default:
                _loadNumber.Text = load;
                _memoryNumber.Text = memory;
                break;
        }

        ToolTip = !stats.Available
            ? L.T("GPU counters are not available on this PC.")
            : string.Join("\n", new[]
            {
                stats.Name.Length > 0 ? stats.Name : L.T("Graphics card"),
                L.T("GPU load: {0}", load),
                stats.MemoryTotalGb > 0
                    ? L.T("Video memory: {0} of {1} GB", stats.MemoryUsedGb.ToString("0.0", culture), stats.MemoryTotalGb.ToString("0.#", culture))
                    : L.T("Video memory: {0}", memory),
            });
        Opacity = stats.Available || Gpu.Current == GpuStats.Empty ? 1 : 0.55;
        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var stats = Gpu.Current;
        tile.ShowRing(stats.Utilization, 100, "AccentGreenBrush", "GreenTrackBrush", stats.Available ? Math.Round(stats.Utilization).ToString(CultureInfo.CurrentCulture) : null);
        tile.Text = "GPU";
    }
}
