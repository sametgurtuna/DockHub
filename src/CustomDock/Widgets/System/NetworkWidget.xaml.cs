using System.Windows;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Widgets;

/// <summary>Real-time download / upload speed, optional chart.</summary>
public partial class NetworkWidget : WidgetBase
{
    public NetworkWidget()
    {
        InitializeComponent();
    }

    protected override void OnAttached() => AppServices.Network.Updated += OnUpdated;

    protected override void OnDetached() => AppServices.Network.Updated -= OnUpdated;

    protected override void OnVariantChanged()
    {
        Chart.Visibility = Variant == "chart" ? Visibility.Visible : Visibility.Collapsed;
        OnUpdated(this, AppServices.Network.Current);
    }

    private void OnUpdated(object? sender, NetworkStats stats)
    {
        (DownValue.Text, DownUnit.Text) = NetworkMonitorService.Format(stats.DownBytesPerSec);
        (UpValue.Text, UpUnit.Text) = NetworkMonitorService.Format(stats.UpBytesPerSec);

        if (Variant == "chart")
        {
            var history = AppServices.Network.History;
            Chart.SetData(history.Select(h => h.DownBytesPerSec).ToList(), history.Select(h => h.UpBytesPerSec).ToList());
        }

        ToolTip = $"Download: {DownValue.Text} {DownUnit.Text}\nUpload: {UpValue.Text} {UpUnit.Text}";
        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var (value, unit) = NetworkMonitorService.Format(AppServices.Network.Current.DownBytesPerSec);
        tile.ShowGlyph(Descriptor.Icon, Descriptor.AccentKey);
        tile.Text = value + unit[..1];
    }
}
