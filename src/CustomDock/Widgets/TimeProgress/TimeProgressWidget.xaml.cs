using System.ComponentModel;
using System.Globalization;
using System.Windows.Controls;
using CustomDock.Core;
using CustomDock.Dock;

namespace CustomDock.Widgets;

public enum TimeProgressMode { Day, Week, Month, Year }

public sealed class TimeProgressSettings : ObservableObject
{
    private TimeProgressMode _mode = TimeProgressMode.Year;

    public TimeProgressMode Mode { get => _mode; set => Set(ref _mode, value); }

    public static IReadOnlyList<Option<TimeProgressMode>> ModeOptions { get; } =
        Enum.GetValues<TimeProgressMode>().Select(m => new Option<TimeProgressMode>(m, TimeProgressWidget.ModeName(m))).ToList();
}

/// <summary>Progress of the day / week / month / year.</summary>
public partial class TimeProgressWidget : WidgetBase
{
    private TimeProgressSettings _settings = new();

    public TimeProgressWidget()
    {
        InitializeComponent();
        Ticks.SizeChanged += (_, _) => Ticks.InvalidateVisual();
    }

    public static string ModeName(TimeProgressMode mode) => mode switch
    {
        TimeProgressMode.Day => "Day",
        TimeProgressMode.Week => "Week",
        TimeProgressMode.Month => "Month",
        _ => "Year",
    };

    protected override void OnAttached()
    {
        _settings = GetSettings<TimeProgressSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        AppServices.Clock.MinuteTick += OnTick;
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        AppServices.Clock.MinuteTick -= OnTick;
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_bar, Layout_ring);
        Render();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void OnTick(object? sender, DateTime e) => Render();

    public static (DateTime Start, DateTime End) GetPeriod(TimeProgressMode mode, DateTime now)
    {
        switch (mode)
        {
            case TimeProgressMode.Week:
                var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
                int offset = ((int)now.DayOfWeek - (int)firstDay + 7) % 7;
                var weekStart = now.Date.AddDays(-offset);
                return (weekStart, weekStart.AddDays(7));
            case TimeProgressMode.Month:
                var monthStart = new DateTime(now.Year, now.Month, 1);
                return (monthStart, monthStart.AddMonths(1));
            case TimeProgressMode.Day:
                return (now.Date, now.Date.AddDays(1));
            default:
                var yearStart = new DateTime(now.Year, 1, 1);
                return (yearStart, yearStart.AddYears(1));
        }
    }

    private void Render()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        var mode = _settings.Mode;
        var (start, end) = GetPeriod(mode, now);
        double fraction = Math.Clamp((now - start).TotalSeconds / (end - start).TotalSeconds, 0, 1);
        var remaining = end - now;
        string percent = fraction.ToString("P0", culture);

        string title = mode switch
        {
            TimeProgressMode.Day => now.ToString("dddd", culture),
            TimeProgressMode.Week => $"Week {culture.Calendar.GetWeekOfYear(now, CalendarWeekRule.FirstFourDayWeek, culture.DateTimeFormat.FirstDayOfWeek)}",
            TimeProgressMode.Month => now.ToString("MMMM", culture),
            _ => now.Year.ToString(culture),
        };
        string detail = mode switch
        {
            TimeProgressMode.Day => $"{(int)remaining.TotalHours}h {remaining.Minutes}m left",
            TimeProgressMode.Week => $"{remaining.Days}d {remaining.Hours}h left",
            _ => $"{Math.Ceiling(remaining.TotalDays):0} days left",
        };

        BarTitle.Text = title;
        BarPercent.Text = percent;
        Ticks.Value = fraction;

        Ring.Value = fraction;
        RingPercent.Text = percent;
        RingTitle.Text = title;
        RingDetail.Text = detail;

        ToolTip = $"{ModeName(mode)}: {percent} elapsed\n{detail}\nRight-click: day / week / month / year";
        _fraction = fraction;
        RefreshCompact();
    }

    private double _fraction;

    protected override void UpdateCompact(CompactTile tile)
    {
        tile.ShowRing(_fraction, 1, "AccentPurpleBrush", "TrackBrush",
            Math.Round(_fraction * 100).ToString(CultureInfo.CurrentCulture));
        tile.Text = ModeName(_settings.Mode);
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        foreach (var option in TimeProgressSettings.ModeOptions)
            items.Add(DockMenu.Check(option.Label, _settings.Mode == option.Value, () => _settings.Mode = option.Value));
    }
}
