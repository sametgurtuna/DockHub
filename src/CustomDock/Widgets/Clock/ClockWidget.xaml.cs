using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using CustomDock.Core;
using CustomDock.Controls;
using CustomDock.Dock;

namespace CustomDock.Widgets;

public enum ClockDateFormat { Short, Long, Numeric, WeekdayOnly }

public sealed class ClockSettings : ObservableObject
{
    private bool _use24Hour = true;
    private bool _showSeconds;
    private ClockDateFormat _dateFormat = ClockDateFormat.Short;

    public bool Use24Hour { get => _use24Hour; set => Set(ref _use24Hour, value); }

    public bool ShowSeconds { get => _showSeconds; set => Set(ref _showSeconds, value); }

    public ClockDateFormat DateFormat { get => _dateFormat; set => Set(ref _dateFormat, value); }

    public static IReadOnlyList<Option<ClockDateFormat>> DateFormatOptions { get; } =
        Enum.GetValues<ClockDateFormat>().Select(f => new Option<ClockDateFormat>(f, FormatDate(DateTime.Now, f))).ToList();

    public static string FormatDate(DateTime date, ClockDateFormat format)
    {
        var culture = CultureInfo.CurrentCulture;
        return format switch
        {
            ClockDateFormat.Long => date.ToString("d MMMM dddd", culture),
            ClockDateFormat.Numeric => date.ToString(culture.DateTimeFormat.ShortDatePattern, culture),
            ClockDateFormat.WeekdayOnly => date.ToString("dddd", culture),
            _ => date.ToString("ddd, d MMM", culture),
        };
    }

    public string FormatTime(DateTime time, bool? seconds = null)
    {
        var culture = CultureInfo.CurrentCulture;
        string sec = (seconds ?? ShowSeconds) ? ":ss" : "";
        if (Use24Hour) return time.ToString("H:mm" + sec, culture);
        var designator = time.Hour < 12 ? culture.DateTimeFormat.AMDesignator : culture.DateTimeFormat.PMDesignator;
        return time.ToString("h:mm" + sec, culture) + (string.IsNullOrEmpty(designator) ? "" : " " + designator);
    }
}

/// <summary>Clock and date (analog / digital / calendar).</summary>
public partial class ClockWidget : WidgetBase
{
    private ClockSettings _settings = new();

    public ClockWidget()
    {
        InitializeComponent();
        Cursor = Cursors.Hand;
        ClockPopup.Closed += (_, _) => Subscribe();
    }

    protected override void OnAttached()
    {
        _settings = GetSettings<ClockSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        AppServices.Reminders.Items.CollectionChanged += OnRemindersChanged;
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        AppServices.Reminders.Items.CollectionChanged -= OnRemindersChanged;
        Unsubscribe();
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_analog, Layout_digital, Layout_calendar);
        Subscribe();
        Render(DateTime.Now);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        Subscribe();
        Render(DateTime.Now);
    }

    private void OnRemindersChanged(object? sender, NotifyCollectionChangedEventArgs e) => Render(DateTime.Now);

    private void Subscribe()
    {
        Unsubscribe();
        if ((_settings.ShowSeconds && Variant != "calendar") || ClockPopup.IsOpen)
            AppServices.Clock.SecondTick += OnTick;
        else
            AppServices.Clock.MinuteTick += OnTick;
    }

    private void Unsubscribe()
    {
        AppServices.Clock.SecondTick -= OnTick;
        AppServices.Clock.MinuteTick -= OnTick;
    }

    private void OnTick(object? sender, DateTime now)
    {
        Render(now);
        if (ClockPopup.IsOpen)
            RenderPopup(now);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (DockDragHelper.JustDragged) return;
        ToggleClockPopup();
        e.Handled = true;
    }

    public override bool OnCompactClick()
    {
        ToggleClockPopup();
        return true;
    }

    private void ToggleClockPopup()
    {
        if (ClockPopup.IsOpen || Dock.PopupAnimationHelper.IsClosing(ClockPopup))
        {
            ClosePopup(ClockPopup);
            return;
        }

        RenderPopup(DateTime.Now);
        OpenPopup(ClockPopup);
        Subscribe();
    }

    private void RenderPopup(DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        PopupClockTime.Text = _settings.Use24Hour ? now.ToString("HH:mm:ss", culture) : now.ToString("hh:mm:ss", culture);
        PopupClockDate.Text = now.ToString("dddd, MMMM d", culture);
        PopupClockZone.Text = GetLocalZoneString();
    }

    private void OnPopupCloseClick(object sender, RoutedEventArgs e)
    {
        ClosePopup(ClockPopup);
    }

    private static string GetLocalZoneString()
    {
        var local = TimeZoneInfo.Local;
        var offset = local.GetUtcOffset(DateTime.Now);
        string gmt = $"GMT{(offset >= TimeSpan.Zero ? "+" : "-")}{Math.Abs(offset.Hours):D2}:{Math.Abs(offset.Minutes):D2}";

        string city = "";
        string displayName = local.DisplayName;
        int closeParen = displayName.IndexOf(')');
        if (closeParen >= 0 && closeParen < displayName.Length - 1)
        {
            city = displayName[(closeParen + 1)..].Trim();
            int comma = city.IndexOf(',');
            if (comma > 0)
                city = city[..comma].Trim();
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            city = local.StandardName;
            if (city.EndsWith(" Standard Time", StringComparison.OrdinalIgnoreCase))
                city = city[..^14].Trim();
        }

        return $"{city} · {gmt}";
    }

    private void Render(DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        string time = _settings.FormatTime(now);
        string date = ClockSettings.FormatDate(now, _settings.DateFormat);
        ToolTip = now.ToString("D", culture);

        switch (Variant)
        {
            case "digital":
                DigitalTime.Text = time;
                DigitalDate.Text = date;
                break;
            case "calendar":
                CalendarWeekday.Text = now.ToString("ddd", culture).ToUpper(culture);
                CalendarDay.Text = now.Day.ToString(culture);
                RenderNextEvent(now);
                break;
            default:
                Analog.Time = now;
                Analog.ShowSeconds = _settings.ShowSeconds;
                AnalogTime.Text = time;
                AnalogDate.Text = date;
                break;
        }
        RefreshCompact();
    }

    private AnalogClock? _compactClock;

    protected override void UpdateCompact(CompactTile tile)
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        tile.SetTextBrushKey("TextPrimaryBrush");
        switch (Variant)
        {
            case "digital":
                tile.ShowLabel(now.ToString(_settings.Use24Hour ? "HH" : "hh", culture) + "\n" + now.ToString("mm", culture), 12.5);
                tile.Text = null;
                break;
            case "calendar":
                tile.ShowLabel(now.Day.ToString(culture), 15);
                tile.SetTextBrushKey("AccentRedBrush");
                tile.Text = now.ToString("ddd", culture).ToUpper(culture);
                break;
            default:
                _compactClock ??= new AnalogClock();
                _compactClock.Time = now;
                tile.SetVisual(_compactClock);
                tile.Text = now.ToString(_settings.Use24Hour ? "H:mm" : "h:mm", culture);
                break;
        }
    }

    private void RenderNextEvent(DateTime now)
    {
        var next = AppServices.Reminders.Items.FirstOrDefault(r => r.Due >= now);
        if (next is null)
        {
            EventDot.Visibility = Visibility.Collapsed;
            EventTitle.Text = "No events today";
            EventTime.Text = now.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            return;
        }

        EventDot.Visibility = Visibility.Visible;
        EventTitle.Text = next.Text;
        EventTime.Text = next.Due.Date == now.Date
            ? _settings.FormatTime(next.Due, false)
            : next.Due.ToString("d MMM", CultureInfo.CurrentCulture) + " · " + _settings.FormatTime(next.Due, false);
    }
}
