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
    private AIProvider _provider = AIProvider.Claude;
    private int _geminiDailyLimit = 1000;

    /// <summary>How often usage is checked (for Claude, each check starts a short background process).</summary>
    public int RefreshMinutes { get => _refreshMinutes; set => Set(ref _refreshMinutes, Math.Clamp(value, 5, 120)); }

    public AIProvider Provider { get => _provider; set => Set(ref _provider, value); }

    /// <summary>Gemini CLI requests per day the widget measures against (1,000 on the free tier).</summary>
    public int GeminiDailyLimit { get => _geminiDailyLimit; set => Set(ref _geminiDailyLimit, Math.Clamp(value, 1, 1_000_000)); }

    public static IReadOnlyList<Option<AIProvider>> Providers { get; } = new[]
    {
        new Option<AIProvider>(AIProvider.Claude, "Claude Code"),
        new Option<AIProvider>(AIProvider.Codex, "OpenAI Codex"),
        new Option<AIProvider>(AIProvider.Gemini, "Gemini CLI"),
    };
}

/// <summary>AI coding assistant limits: Claude Code, OpenAI Codex or Gemini CLI (numbers / rings / bars).</summary>
public partial class AIUsageWidget : WidgetBase
{
    private AIUsageSettings _settings = new();
    private IAIUsageSource? _source;

    public AIUsageWidget()
    {
        InitializeComponent();
    }

    private static IAIUsageSource SourceFor(AIProvider provider) => provider switch
    {
        AIProvider.Codex => AppServices.CodexUsage,
        AIProvider.Gemini => AppServices.GeminiUsage,
        _ => AppServices.AIUsage,
    };

    private IAIUsageSource Source => _source ?? SourceFor(_settings.Provider);

    protected override void OnAttached()
    {
        _settings = GetSettings<AIUsageSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        Subscribe();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        Unsubscribe();
    }

    private void Subscribe()
    {
        _source = SourceFor(_settings.Provider);
        if (_source is GeminiUsageService gemini) gemini.DailyLimit = _settings.GeminiDailyLimit;
        _source.RequestInterval(this, TimeSpan.FromMinutes(_settings.RefreshMinutes));
        _source.Updated += OnUpdated;
    }

    private void Unsubscribe()
    {
        if (_source is null) return;
        _source.RequestInterval(this, null);
        _source.Updated -= OnUpdated;
        _source = null;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AIUsageSettings.Provider) or nameof(AIUsageSettings.GeminiDailyLimit))
        {
            Unsubscribe();
            Subscribe();
            OnUpdated(this, Source.Current);
            _ = Source.RefreshAsync();
            return;
        }
        Source.RequestInterval(this, TimeSpan.FromMinutes(_settings.RefreshMinutes));
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item("Refresh now", "", () => _ = Source.RefreshAsync(), !Source.IsRefreshing));
    }

    /// <summary>Explains a failed update in plain words.</summary>
    private string? StatusMessage() => (_settings.Provider, Source.Status) switch
    {
        (AIProvider.Codex, AIUsageStatus.CliNotFound) => L.T("Codex CLI not found. Install it and sign in to see your limits."),
        (AIProvider.Codex, AIUsageStatus.ParseFailed) => L.T("No Codex limits yet. They appear after you use Codex once."),
        (AIProvider.Gemini, AIUsageStatus.CliNotFound) => L.T("Gemini CLI not found. Install it and sign in to see today's requests."),
        (_, AIUsageStatus.CliNotFound) => L.T("Claude Code CLI not found. Install it and sign in to see your limits."),
        (_, AIUsageStatus.NotLoggedIn) => L.T("Not signed in. Run \"claude\" in a terminal and log in."),
        (_, AIUsageStatus.Timeout) => L.T("The Claude CLI didn't answer in time. Retrying later."),
        (_, AIUsageStatus.ParseFailed) => L.T("Couldn't read the usage output of the Claude CLI. Automatic checks are paused; right-click › Refresh now to try again."),
        (_, AIUsageStatus.Unknown) => L.T("Couldn't update usage. Retrying later."),
        _ => null,
    };

    private string ProviderName => _settings.Provider switch
    {
        AIProvider.Codex => "Codex",
        AIProvider.Gemini => "Gemini",
        _ => "Claude",
    };

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_numbers, Layout_rings, Layout_bars);
        OnUpdated(this, Source.Current);
    }

    /// <summary>Limit names and whether there is a second limit (Gemini has only the daily one).</summary>
    private void ApplyLabels(AIUsageData data)
    {
        string first = data.PrimaryLabel ?? L.T("5-hour");
        string second = data.SecondaryLabel ?? L.T("Weekly");
        NumbersLabel1.Text = RingsLabel1.Text = BarsLabel1.Text = first;
        NumbersLabel2.Text = RingsLabel2.Text = BarsLabel2.Text = second;
        var visibility = data.HasSecondary ? Visibility.Visible : Visibility.Collapsed;
        NumbersSecond.Visibility = RingsSecond.Visibility = visibility;
        BarsLabel2.Visibility = WeekBarTrack.Visibility = WeekBarText.Visibility = visibility;
        Layout_bars.RowDefinitions[1].Height = new GridLength(data.HasSecondary ? 19 : 0);
    }

    private void OnUpdated(object? sender, AIUsageData data)
    {
        ApplyLabels(data);
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

        // Near a limit the ring or bar turns red; without data the widget dims.
        string sessionBrush = session >= 90 ? "AccentRedBrush" : "AccentOrangeBrush";
        string weekBrush = week >= 90 ? "AccentRedBrush" : "AccentBlueBrush";
        SessionRing.SetResourceReference(RingGauge.FillProperty, sessionBrush);
        WeekRing.SetResourceReference(RingGauge.FillProperty, weekBrush);
        SessionBar.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, sessionBrush);
        WeekBar.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, weekBrush);
        Opacity = data.SessionPercent is null && data.WeekPercent is null && Source.Status != AIUsageStatus.Pending ? 0.55 : 1;

        string first = data.PrimaryLabel ?? L.T("5-hour");
        string second = data.SecondaryLabel ?? L.T("Weekly");
        var tooltip = new List<string> { data.Title ?? L.T("Claude Code usage") };
        tooltip.Add(data.SessionPercent is null
            ? L.T("{0}: unknown", first)
            : $"{first}: {sessionText}" + (data.SessionResets is null ? "" : " " + L.T("(resets {0})", data.SessionResets)));
        if (data.HasSecondary)
            tooltip.Add(data.WeekPercent is null
                ? L.T("{0}: unknown", second)
                : $"{second}: {weekText}" + (data.WeekResets is null ? "" : " " + L.T("(resets {0})", data.WeekResets)));
        if (data.Detail is { } detail)
            tooltip.Add(detail);
        if (StatusMessage() is { } status)
            tooltip.Add(status);
        if (data.FetchedAt != default)
            tooltip.Add(L.T("Updated {0}", data.FetchedAt.ToString("t")));
        ToolTip = string.Join("\n", tooltip);

        RefreshCompact();
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var data = Source.Current;
        double session = data.SessionPercent ?? 0;
        bool warn = session >= 90;
        tile.ShowRing(session, 100, warn ? "AccentRedBrush" : "AccentOrangeBrush", "OrangeTrackBrush",
            data.SessionPercent is null ? null : Math.Round(session).ToString(CultureInfo.CurrentCulture));
        tile.Text = ProviderName;
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
