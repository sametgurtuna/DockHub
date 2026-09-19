using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using CustomDock.Core;
using CustomDock.Controls;
using CustomDock.Dock;

namespace CustomDock.Widgets;

public sealed class WorldCity : ObservableObject
{
    private string _label = "";

    public string Label { get => _label; set => Set(ref _label, value); }

    /// <summary>Windows time zone ID (e.g. "Tokyo Standard Time") or IANA ID.</summary>
    public string TimeZoneId { get; set; } = "UTC";
}

public sealed class WorldClockSettings : ObservableObject
{
    private bool _use24Hour = true;

    public ObservableCollection<WorldCity> Cities { get; set; } = new()
    {
        new WorldCity { Label = "London", TimeZoneId = "GMT Standard Time" },
        new WorldCity { Label = "New York", TimeZoneId = "Eastern Standard Time" },
        new WorldCity { Label = "Tokyo", TimeZoneId = "Tokyo Standard Time" },
    };

    public bool Use24Hour { get => _use24Hour; set => Set(ref _use24Hour, value); }

    /// <summary>Called when cities list changes (collection changes do not trigger PropertyChanged).</summary>
    public void NotifyCitiesChanged() => OnPropertyChanged(nameof(Cities));

    public static IReadOnlyList<Option<string>> PopularCities { get; } = new List<Option<string>>
    {
        new("Turkey Standard Time", "Istanbul"),
        new("GMT Standard Time", "London"),
        new("W. Europe Standard Time", "Berlin"),
        new("Romance Standard Time", "Paris"),
        new("W. Europe Standard Time", "Amsterdam"),
        new("Russian Standard Time", "Moscow"),
        new("Azerbaijan Standard Time", "Baku"),
        new("Arabian Standard Time", "Dubai"),
        new("India Standard Time", "New Delhi"),
        new("Singapore Standard Time", "Singapore"),
        new("China Standard Time", "Beijing"),
        new("Tokyo Standard Time", "Tokyo"),
        new("Korea Standard Time", "Seoul"),
        new("AUS Eastern Standard Time", "Sydney"),
        new("E. South America Standard Time", "São Paulo"),
        new("Eastern Standard Time", "New York"),
        new("Eastern Standard Time", "Toronto"),
        new("Central Standard Time", "Chicago"),
        new("Pacific Standard Time", "San Francisco"),
        new("Hawaiian Standard Time", "Honolulu"),
        new("UTC", "UTC"),
    };

    public static TimeZoneInfo? FindZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return null; }
    }
}

/// <summary>A single city displayed in the widget.</summary>
public sealed class WorldClockItem : ObservableObject
{
    private DateTime _time;
    private string _timeText = "";
    private string _details = "";

    public WorldClockItem(WorldCity city)
    {
        City = city;
        Zone = WorldClockSettings.FindZone(city.TimeZoneId);
    }

    public WorldCity City { get; }

    public TimeZoneInfo? Zone { get; }

    public string Label => City.Label;

    public DateTime Time { get => _time; private set => Set(ref _time, value); }

    public string TimeText { get => _timeText; private set => Set(ref _timeText, value); }

    public string Details { get => _details; private set => Set(ref _details, value); }

    public void Update(DateTime utcNow, bool use24Hour)
    {
        if (Zone is null)
        {
            TimeText = "--:--";
            Details = $"{City.Label}: timezone not found ({City.TimeZoneId})";
            return;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, Zone);
        Time = local;
        var culture = CultureInfo.CurrentCulture;
        TimeText = use24Hour ? local.ToString("H:mm", culture) : local.ToString("h:mm tt", culture);

        var diff = Zone.GetUtcOffset(utcNow) - TimeZoneInfo.Local.GetUtcOffset(utcNow);
        string offset = diff == TimeSpan.Zero ? "same as local time"
            : $"{(diff > TimeSpan.Zero ? "+" : "−")}{Math.Abs(diff.TotalHours):0.##} hr";
        int dayDiff = (local.Date - DateTime.Now.Date).Days;
        string day = dayDiff switch { > 0 => " · tomorrow", < 0 => " · yesterday", _ => "" };
        Details = $"{City.Label} — {local.ToString("dddd HH:mm", culture)}\n{offset}{day}";
    }
}

/// <summary>Clock for one or more cities.</summary>
public partial class WorldClockWidget : WidgetBase
{
    private readonly ObservableCollection<WorldClockItem> _items = new();
    private WorldClockSettings _settings = new();

    public WorldClockWidget()
    {
        InitializeComponent();
        Layout_multi.ItemsSource = _items;
        ClockPopup.Closed += (_, _) => UpdateTickSubscription();
    }

    protected override void OnAttached()
    {
        _settings = GetSettings<WorldClockSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        _settings.Cities.CollectionChanged += OnCitiesChanged;
        AppServices.Clock.MinuteTick += OnTick;
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        _settings.Cities.CollectionChanged -= OnCitiesChanged;
        AppServices.Clock.MinuteTick -= OnTick;
        AppServices.Clock.SecondTick -= OnTick;
        _secondTickSubscribed = false;
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_single, Layout_multi);
        Rebuild();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void OnCitiesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnTick(object? sender, DateTime e)
    {
        Update();
        if (ClockPopup.IsOpen)
            RenderPopup();
    }

    private void OnWidgetMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
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

        RenderPopup();
        OpenPopup(ClockPopup);
        UpdateTickSubscription();
    }

    private void RenderPopup()
    {
        var first = _items.FirstOrDefault();
        var zone = first?.Zone ?? TimeZoneInfo.Local;
        var nowUtc = DateTime.UtcNow;
        var cityTime = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var culture = CultureInfo.CurrentCulture;

        PopupClockTime.Text = _settings.Use24Hour ? cityTime.ToString("HH:mm:ss", culture) : cityTime.ToString("hh:mm:ss", culture);
        PopupClockDate.Text = cityTime.ToString("dddd, MMMM d", culture);

        var offset = zone.GetUtcOffset(cityTime);
        string gmt = $"GMT{(offset >= TimeSpan.Zero ? "+" : "-")}{Math.Abs(offset.Hours):D2}:{Math.Abs(offset.Minutes):D2}";
        string cityName = first?.Label ?? GetLocalCityName();
        PopupClockZone.Text = $"{cityName} · {gmt}";
    }

    private void OnPopupCloseClick(object sender, RoutedEventArgs e)
    {
        ClosePopup(ClockPopup);
    }

    private static string GetLocalCityName()
    {
        var local = TimeZoneInfo.Local;
        string displayName = local.DisplayName;
        int closeParen = displayName.IndexOf(')');
        if (closeParen >= 0 && closeParen < displayName.Length - 1)
        {
            string c = displayName[(closeParen + 1)..].Trim();
            int comma = c.IndexOf(',');
            if (comma > 0) c = c[..comma].Trim();
            if (!string.IsNullOrWhiteSpace(c)) return c;
        }
        string name = local.StandardName;
        if (name.EndsWith(" Standard Time", StringComparison.OrdinalIgnoreCase))
            name = name[..^14].Trim();
        return string.IsNullOrWhiteSpace(name) ? "Local" : name;
    }

    private bool _secondTickSubscribed;

    private void UpdateTickSubscription()
    {
        bool shouldSecondTick = ClockPopup.IsOpen;
        if (shouldSecondTick == _secondTickSubscribed) return;
        _secondTickSubscribed = shouldSecondTick;
        if (shouldSecondTick)
        {
            AppServices.Clock.MinuteTick -= OnTick;
            AppServices.Clock.SecondTick += OnTick;
        }
        else
        {
            AppServices.Clock.SecondTick -= OnTick;
            AppServices.Clock.MinuteTick += OnTick;
        }
    }

    private void Rebuild()
    {
        _items.Clear();
        var cities = Variant == "single" ? _settings.Cities.Take(1) : _settings.Cities.Take(4);
        foreach (var city in cities)
            _items.Add(new WorldClockItem(city));
        Update();
    }

    private void Update()
    {
        var utc = DateTime.UtcNow;
        foreach (var item in _items)
            item.Update(utc, _settings.Use24Hour);

        if (Variant != "single")
        {
            ToolTip = _items.Count == 0 ? "Add a city" : string.Join("\n\n", _items.Select(i => i.Details));
            RefreshCompact();
            return;
        }
        var first = _items.FirstOrDefault();
        SingleTime.Text = first?.TimeText ?? "--:--";
        SingleCity.Text = first?.Label ?? "Add a city";
        SingleClock.Time = first?.Time ?? DateTime.Now;
        ToolTip = first?.Details;
        Layout_single.Visibility = Visibility.Visible;
        RefreshCompact();
    }

    private AnalogClock? _compactClock;

    protected override void UpdateCompact(CompactTile tile)
    {
        var first = _items.FirstOrDefault();
        _compactClock ??= new AnalogClock();
        _compactClock.Time = first?.Time ?? DateTime.Now;
        tile.SetVisual(_compactClock);
        var label = (first?.Label ?? "").ToUpper(CultureInfo.CurrentCulture);
        tile.Text = label.Length > 3 ? label[..3] : label;
    }
}
