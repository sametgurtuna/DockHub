using System.Globalization;
using System.Windows;
using System.Windows.Media.Animation;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Widgets;

/// <summary>Claude Code abonelik kullanımı: 5 saatlik ve haftalık limit (sayılar / halkalar / çubuklar).</summary>
public partial class AIUsageWidget : WidgetBase
{
    public AIUsageWidget()
    {
        InitializeComponent();
    }

    protected override void OnAttached() => AppServices.AIUsage.Updated += OnUpdated;

    protected override void OnDetached() => AppServices.AIUsage.Updated -= OnUpdated;

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_numbers, Layout_rings, Layout_bars);
        OnUpdated(this, AppServices.AIUsage.Current);
    }

    private void OnUpdated(object? sender, AIUsageData data)
    {
        var culture = CultureInfo.CurrentCulture;
        double session = data.SessionPercent ?? 0;
        double week = data.WeekPercent ?? 0;
        string sessionText = data.SessionPercent is null ? "—" : $"%{Math.Round(session).ToString(culture)}";
        string weekText = data.WeekPercent is null ? "—" : $"%{Math.Round(week).ToString(culture)}";

        switch (Variant)
        {
            case "rings":
                SessionRing.Value = session;
                WeekRing.Value = week;
                SessionRingText.Text = data.SessionPercent is null ? "" : Math.Round(session).ToString(culture);
                WeekRingText.Text = data.WeekPercent is null ? "" : Math.Round(week).ToString(culture);
                break;
            case "bars":
                AnimateBar(SessionBar, SessionBarTrack.ActualWidth * session / 100);
                AnimateBar(WeekBar, WeekBarTrack.ActualWidth * week / 100);
                SessionBarText.Text = sessionText;
                WeekBarText.Text = weekText;
                break;
            default:
                SessionNumber.Text = sessionText;
                WeekNumber.Text = weekText;
                break;
        }

        var tooltip = new List<string>();
        tooltip.Add(data.SessionPercent is null
            ? "5 saat: bilinmiyor"
            : $"5 saat: {sessionText}" + (data.SessionResets is null ? "" : $" (yenilenme: {data.SessionResets})"));
        tooltip.Add(data.WeekPercent is null
            ? "Haftalık: bilinmiyor"
            : $"Haftalık: {weekText}" + (data.WeekResets is null ? "" : $" (yenilenme: {data.WeekResets})"));
        if (AppServices.AIUsage.Error is { } error)
            tooltip.Add($"Hata: {error}");
        ToolTip = string.Join("\n", tooltip);

        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var data = AppServices.AIUsage.Current;
        double session = data.SessionPercent ?? 0;
        bool warn = session >= 90;
        tile.ShowRing(session, 100, warn ? "AccentRedBrush" : "AccentOrangeBrush", "OrangeTrackBrush",
            data.SessionPercent is null ? null : Math.Round(session).ToString(CultureInfo.CurrentCulture));
        tile.Text = "Claude";
    }

    private void AnimateBar(FrameworkElement bar, double width)
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
