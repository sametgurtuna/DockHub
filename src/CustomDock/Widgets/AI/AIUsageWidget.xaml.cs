using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

public sealed class AIUsageSettings : ObservableObject
{
    private int _refreshMinutes = 15;

    /// <summary>How often the Claude CLI is asked for usage (each check starts a short background process).</summary>
    public int RefreshMinutes { get => _refreshMinutes; set => Set(ref _refreshMinutes, Math.Clamp(value, 5, 120)); }
}

/// <summary>Claude Code subscription usage: 5-hour and weekly limits (numbers / rings / bars).</summary>
public partial class AIUsageWidget : WidgetBase
{
    private AIUsageSettings _settings = new();

    public AIUsageWidget()
    {
        InitializeComponent();
    }

    protected override void OnAttached()
    {
        _settings = GetSettings<AIUsageSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        AppServices.AIUsage.RequestInterval(this, TimeSpan.FromMinutes(_settings.RefreshMinutes));
        AppServices.AIUsage.Updated += OnUpdated;
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        AppServices.AIUsage.RequestInterval(this, null);
        AppServices.AIUsage.Updated -= OnUpdated;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        => AppServices.AIUsage.RequestInterval(this, TimeSpan.FromMinutes(_settings.RefreshMinutes));

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item("Refresh now", "", () => _ = AppServices.AIUsage.RefreshAsync(), !AppServices.AIUsage.IsRefreshing));
    }

    /// <summary>Explains a failed update in plain words.</summary>
    private static string? StatusMessage(AIUsageService service) => service.Status switch
    {
        AIUsageStatus.CliNotFound => "Claude Code CLI not found. Install it and sign in to see your limits.",
        AIUsageStatus.NotLoggedIn => "Not signed in. Run \"claude\" in a terminal and log in.",
        AIUsageStatus.Timeout => "The Claude CLI didn't answer in time. Retrying later.",
        AIUsageStatus.ParseFailed => "Couldn't read the usage output of the Claude CLI. Automatic checks are paused; right-click › Refresh now to try again.",
        AIUsageStatus.Unknown => "Couldn't update usage. Retrying later.",
        _ => null,
    };

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
        string sessionText = data.SessionPercent is null ? "—" : $"{Math.Round(session).ToString(culture)}%";
        string weekText = data.WeekPercent is null ? "—" : $"{Math.Round(week).ToString(culture)}%";

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
            ? "5-hour: unknown"
            : $"5-hour: {sessionText}" + (data.SessionResets is null ? "" : $" (resets: {data.SessionResets})"));
        tooltip.Add(data.WeekPercent is null
            ? "Weekly: unknown"
            : $"Weekly: {weekText}" + (data.WeekResets is null ? "" : $" (resets: {data.WeekResets})"));
        if (StatusMessage(AppServices.AIUsage) is { } status)
            tooltip.Add(status);
        if (data.FetchedAt != default)
            tooltip.Add($"Updated {data.FetchedAt:t}");
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
