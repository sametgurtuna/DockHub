using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

public sealed class PingSettings : ObservableObject
{
    private string _target = PingService.DefaultTarget;

    /// <summary>Host name or address to ping (Cloudflare's 1.1.1.1 by default).</summary>
    public string Target
    {
        get => _target;
        set => Set(ref _target, string.IsNullOrWhiteSpace(value) ? PingService.DefaultTarget : value.Trim());
    }
}

/// <summary>Round-trip time to a host every five seconds, with packet loss and a trend line.</summary>
public sealed class PingWidget : WidgetBase
{
    public const string Icon = "M3,17 A9,9 0 0 1 21,17 M12,17 L16.5,10.5 M12,18.5 A1.5,1.5 0 1 1 12.01,18.5";

    private readonly StackPanel _numberLayout;
    private readonly StackPanel _chartLayout;
    private readonly TextBlock _numberValue;
    private readonly TextBlock _chartValue;
    private readonly TextBlock _chartCaption;
    private readonly Sparkline _chart;
    private readonly Popup _popup;
    private readonly StackPanel _panel;
    private PingSettings _settings = new();
    private PingMonitor? _monitor;
    private PingStats _stats = new();

    public PingWidget()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        _numberValue = Value(15);
        _numberLayout = new StackPanel
        {
            Name = "Layout_number",
            VerticalAlignment = VerticalAlignment.Center,
            Children = { WidgetUi.Text("CaptionText", L.T("Ping")), _numberValue },
        };

        _chartValue = Value(14);
        _chartCaption = WidgetUi.Text("CaptionText");
        _chart = new Sparkline { Width = 64, Height = 24, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, SecondaryStroke = System.Windows.Media.Brushes.Transparent };
        _chart.SetResourceReference(Sparkline.PrimaryStrokeProperty, "AccentGreenBrush");
        _chartLayout = new StackPanel
        {
            Name = "Layout_chart",
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _chartCaption, _chartValue } }, _chart },
        };

        (_popup, _, _panel) = WidgetUi.Flyout(FlyoutSize.Narrow, L.T("Ping"), null);
        Content = new Grid { Background = System.Windows.Media.Brushes.Transparent, Children = { _numberLayout, _chartLayout, _popup } };
        Cursor = System.Windows.Input.Cursors.Hand;
        MouseLeftButtonUp += (_, e) =>
        {
            if (DockDragHelper.JustDragged) return;
            e.Handled = true;
            BuildPanel();
            OpenPopup(_popup);
        };
    }

    private static TextBlock Value(double size)
    {
        var value = WidgetUi.Text("TitleText", "—", size);
        System.Windows.Documents.Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        return value;
    }

    protected override void OnAttached()
    {
        _settings = GetSettings<PingSettings>();
        if (IsPreview)
        {
            foreach (var ms in new double?[] { 18, 21, 17, 24, 19, 42, 20, 18, 22, 19, 23, 21 }) _stats.Add(ms);
            return;
        }
        _settings.PropertyChanged += OnSettingsChanged;
        Watch();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        Unwatch();
    }

    private void Watch()
    {
        _monitor = AppServices.Ping.Watch(_settings.Target);
        _stats = _monitor.Stats;
        _monitor.Updated += Render;
    }

    private void Unwatch()
    {
        if (_monitor is null) return;
        _monitor.Updated -= Render;
        AppServices.Ping.Unwatch(_monitor);
        _monitor = null;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        Unwatch();
        Watch();
        Render();
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(_numberLayout, _chartLayout);
        Render();
    }

    private static string Ms(double? value) => value is { } ms ? L.T("{0} ms", Math.Round(ms).ToString(CultureInfo.CurrentCulture)) : "—";

    private string LatestText => _stats.Count == 0 ? "…" : _stats.LastLost ? L.T("Lost") : Ms(_stats.Latest);

    private string Target => _monitor?.Target ?? _settings.Target;

    private void Render()
    {
        string brush = _stats.Count == 0 ? "TextSecondaryBrush" : PingStats.BrushFor(_stats.LastLost ? null : _stats.Latest);
        foreach (var value in new[] { _numberValue, _chartValue })
        {
            value.Text = LatestText;
            value.SetResourceReference(TextBlock.ForegroundProperty, brush);
        }
        _chartCaption.Text = _stats.Loss > 0 ? L.T("Ping · {0}% lost", Math.Round(_stats.Loss).ToString(CultureInfo.CurrentCulture)) : L.T("Ping");
        _chart.SetResourceReference(Sparkline.PrimaryStrokeProperty, _stats.Loss > 0 ? "AccentOrangeBrush" : "AccentGreenBrush");
        _chart.SetData(_stats.History, Array.Empty<double>());

        var lines = new List<string>
        {
            L.T("Ping to {0}: {1}", Target, LatestText),
            L.T("Average {0}, packet loss {1}%", Ms(_stats.Average), Math.Round(_stats.Loss).ToString(CultureInfo.CurrentCulture)),
        };
        if (_monitor?.Error is { } error) lines.Add(error);
        ToolTip = string.Join("\n", lines);
        if (_popup.IsOpen) BuildPanel();
        RefreshCompact();
    }

    /// <summary>The panel: the target, the last and average times, their range, jitter, loss and the trend.</summary>
    private void BuildPanel()
    {
        _panel.Children.Clear();
        void Row(string label, string value, string brush = "TextPrimaryBrush")
        {
            var grid = new Grid { Margin = new Thickness(4, 3, 4, 3) };
            var text = WidgetUi.Text("CaptionText", label);
            var number = WidgetUi.Text("TitleText", value, 12.5);
            number.HorizontalAlignment = HorizontalAlignment.Right;
            number.SetResourceReference(TextBlock.ForegroundProperty, brush);
            grid.Children.Add(text);
            grid.Children.Add(number);
            _panel.Children.Add(grid);
        }
        Row(L.T("Target"), Target);
        Row(L.T("Last reply"), LatestText, _stats.Count == 0 ? "TextSecondaryBrush" : PingStats.BrushFor(_stats.LastLost ? null : _stats.Latest));
        Row(L.T("Average"), Ms(_stats.Average));
        Row(L.T("Fastest and slowest"), _stats.Min is null ? "—" : $"{Ms(_stats.Min)} – {Ms(_stats.Max)}");
        Row(L.T("Jitter"), Ms(_stats.Jitter));
        Row(L.T("Packet loss"), $"{Math.Round(_stats.Loss).ToString(CultureInfo.CurrentCulture)}%", _stats.Loss > 0 ? "AccentOrangeBrush" : "TextPrimaryBrush");

        var chart = new Sparkline { Height = 44, Margin = new Thickness(4, 8, 4, 4), SecondaryStroke = System.Windows.Media.Brushes.Transparent };
        chart.SetResourceReference(Sparkline.PrimaryStrokeProperty, "AccentGreenBrush");
        chart.SetData(_stats.History, Array.Empty<double>());
        _panel.Children.Add(chart);

        var note = WidgetUi.Text("CaptionText", L.T("Every {0} seconds, the last {1} replies.", (int)PingMonitor.Interval.TotalSeconds, PingStats.Window));
        note.Margin = new Thickness(4, 2, 4, 0);
        note.TextWrapping = TextWrapping.Wrap;
        _panel.Children.Add(note);
        if (_monitor?.Error is { } error)
        {
            var problem = WidgetUi.Text("CaptionText", error);
            problem.Margin = new Thickness(4, 2, 4, 0);
            problem.TextWrapping = TextWrapping.Wrap;
            _panel.Children.Add(problem);
        }
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        tile.ShowGlyph(Descriptor.Icon, _stats.Count == 0 ? "TextSecondaryBrush" : PingStats.BrushFor(_stats.LastLost ? null : _stats.Latest));
        tile.Text = _stats.Count == 0 || _stats.LastLost ? null : Math.Round(_stats.Latest ?? 0).ToString(CultureInfo.CurrentCulture);
    }
}
