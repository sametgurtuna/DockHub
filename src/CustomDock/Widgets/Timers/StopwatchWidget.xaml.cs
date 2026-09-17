using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using CustomDock.Core;
using CustomDock.Dock;

namespace CustomDock.Widgets;

/// <summary>Kronometre: tıkla başlat/duraklat, sağ tık → sıfırla.</summary>
public partial class StopwatchWidget : WidgetBase
{
    private readonly Stopwatch _stopwatch = new();

    public StopwatchWidget()
    {
        InitializeComponent();
    }

    protected override void OnAttached() => Render();

    protected override void OnDetached() => AppServices.Clock.SecondTick -= OnTick;

    private void OnClick(object sender, MouseButtonEventArgs e) => Toggle();

    private void Toggle()
    {
        if (_stopwatch.IsRunning)
        {
            _stopwatch.Stop();
            AppServices.Clock.SecondTick -= OnTick;
        }
        else
        {
            _stopwatch.Start();
            AppServices.Clock.SecondTick += OnTick;
        }
        Render();
    }

    private void Reset()
    {
        _stopwatch.Reset();
        AppServices.Clock.SecondTick -= OnTick;
        Render();
    }

    private void OnTick(object? sender, DateTime e) => Render();

    private void Render()
    {
        var elapsed = _stopwatch.Elapsed;
        TimeText.Text = TimerFormat.Format(elapsed);
        StatusText.Text = _stopwatch.IsRunning ? "Çalışıyor" : elapsed > TimeSpan.Zero ? "Duraklatıldı" : "Hazır";
        ToolTip = _stopwatch.IsRunning ? "Duraklatmak için tıklayın" : "Başlatmak için tıklayın · Sağ tık: sıfırla";
        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        tile.ShowGlyph(Descriptor.Icon, _stopwatch.IsRunning ? "AccentOrangeBrush" : "TextSecondaryBrush");
        tile.Text = TimerFormat.Format(_stopwatch.Elapsed);
    }

    public override bool OnCompactClick()
    {
        Toggle();
        return true;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(_stopwatch.IsRunning ? "Duraklat" : "Başlat", _stopwatch.IsRunning ? "\uE769" : "\uE768", Toggle));
        items.Add(DockMenu.Item("Sıfırla", "\uE72C", Reset, _stopwatch.Elapsed > TimeSpan.Zero));
    }
}

public static class TimerFormat
{
    public static string Format(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    /// <summary>Geri sayımlarda kalan süreyi yukarı yuvarlar (4:59.3 → 5:00 değil, 5:00 → 5:00).</summary>
    public static string FormatRemaining(TimeSpan span)
        => Format(TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, span.TotalSeconds))));
}
