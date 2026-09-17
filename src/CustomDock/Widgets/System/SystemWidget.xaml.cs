using System.ComponentModel;
using System.Globalization;
using System.Windows.Media.Animation;
using CustomDock.Core;
using CustomDock.Services;
using System.Windows;
using System.Windows.Controls;
using CustomDock.Controls;

namespace CustomDock.Widgets;

public sealed class SystemSettings : ObservableObject
{
    private int _updateIntervalSeconds = 2;

    public int UpdateIntervalSeconds { get => _updateIntervalSeconds; set => Set(ref _updateIntervalSeconds, Math.Clamp(value, 1, 30)); }
}

/// <summary>CPU ve bellek kullanımı (sayılar / halkalar / çubuklar).</summary>
public partial class SystemWidget : WidgetBase
{
    private SystemSettings _settings = new();

    public SystemWidget()
    {
        InitializeComponent();
    }

    protected override void OnAttached()
    {
        _settings = GetSettings<SystemSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        AppServices.SystemMonitor.UpdateInterval = TimeSpan.FromSeconds(_settings.UpdateIntervalSeconds);
        AppServices.SystemMonitor.Updated += OnUpdated;
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        AppServices.SystemMonitor.Updated -= OnUpdated;
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_numbers, Layout_rings, Layout_bars);
        OnUpdated(this, AppServices.SystemMonitor.Current);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        => AppServices.SystemMonitor.UpdateInterval = TimeSpan.FromSeconds(_settings.UpdateIntervalSeconds);

    private void OnUpdated(object? sender, SystemStats stats)
    {
        var culture = CultureInfo.CurrentCulture;
        string cpu = (stats.CpuPercent / 100).ToString("P0", culture);
        string ram = (stats.RamPercent / 100).ToString("P0", culture);

        switch (Variant)
        {
            case "rings":
                CpuRing.Value = stats.CpuPercent;
                RamRing.Value = stats.RamPercent;
                CpuRingText.Text = Math.Round(stats.CpuPercent).ToString(culture);
                RamRingText.Text = Math.Round(stats.RamPercent).ToString(culture);
                break;
            case "bars":
                AnimateBar(CpuBar, CpuBarTrack.ActualWidth * stats.CpuPercent / 100);
                AnimateBar(RamBar, RamBarTrack.ActualWidth * stats.RamPercent / 100);
                CpuBarText.Text = cpu;
                RamBarText.Text = ram;
                break;
            default:
                CpuNumber.Text = cpu;
                RamNumber.Text = ram;
                break;
        }

        ToolTip = $"İşlemci: {cpu}\nBellek: {stats.RamUsedGb:0.0} / {stats.RamTotalGb:0.0} GB ({ram})";
        RefreshCompact();
    }

    private Grid? _compactRings;
    private RingGauge? _compactCpu;
    private RingGauge? _compactRam;

    /// <summary>Kutucukta iç içe iki halka: dışta işlemci (pembe), içte bellek (mavi).</summary>
    protected override void UpdateCompact(CompactTile tile)
    {
        var stats = AppServices.SystemMonitor.Current;
        if (_compactRings is null)
        {
            _compactCpu = new RingGauge { Thickness = 2.6 };
            _compactCpu.SetResourceReference(RingGauge.FillProperty, "AccentMagentaBrush");
            _compactCpu.SetResourceReference(RingGauge.TrackProperty, "MagentaTrackBrush");
            _compactRam = new RingGauge { Thickness = 2.6, Margin = new Thickness(4.5) };
            _compactRam.SetResourceReference(RingGauge.FillProperty, "AccentBlueBrush");
            _compactRam.SetResourceReference(RingGauge.TrackProperty, "BlueTrackBrush");
            _compactRings = new Grid();
            _compactRings.Children.Add(_compactCpu);
            _compactRings.Children.Add(_compactRam);
        }
        _compactCpu!.Value = stats.CpuPercent;
        _compactRam!.Value = stats.RamPercent;
        tile.SetVisual(_compactRings);
        tile.SetTextBrushKey("AccentMagentaBrush");
        tile.Text = "%" + Math.Round(stats.CpuPercent).ToString(CultureInfo.CurrentCulture);
    }

    private void AnimateBar(System.Windows.FrameworkElement bar, double width)
    {
        if (!IsVisible || double.IsNaN(bar.Width))
        {
            bar.Width = Math.Max(0, width);
            return;
        }
        bar.BeginAnimation(WidthProperty, new DoubleAnimation(Math.Max(0, width), TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }
}
