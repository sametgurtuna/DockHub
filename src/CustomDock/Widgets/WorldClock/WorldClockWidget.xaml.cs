using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using CustomDock.Core;
using CustomDock.Controls;

namespace CustomDock.Widgets;

public sealed class WorldCity : ObservableObject
{
    private string _label = "";

    public string Label { get => _label; set => Set(ref _label, value); }

    /// <summary>Windows saat dilimi kimliği (ör. "Tokyo Standard Time") veya IANA kimliği.</summary>
    public string TimeZoneId { get; set; } = "UTC";
}

public sealed class WorldClockSettings : ObservableObject
{
    private bool _use24Hour = true;

    public ObservableCollection<WorldCity> Cities { get; set; } = new()
    {
        new WorldCity { Label = "Londra", TimeZoneId = "GMT Standard Time" },
        new WorldCity { Label = "New York", TimeZoneId = "Eastern Standard Time" },
        new WorldCity { Label = "Tokyo", TimeZoneId = "Tokyo Standard Time" },
    };

    public bool Use24Hour { get => _use24Hour; set => Set(ref _use24Hour, value); }

    /// <summary>Şehir listesi değiştiğinde çağrılır (koleksiyon değişiklikleri PropertyChanged üretmez).</summary>
    public void NotifyCitiesChanged() => OnPropertyChanged(nameof(Cities));

    public static IReadOnlyList<Option<string>> PopularCities { get; } = new List<Option<string>>
    {
        new("Turkey Standard Time", "İstanbul"),
        new("GMT Standard Time", "Londra"),
        new("W. Europe Standard Time", "Berlin"),
        new("Romance Standard Time", "Paris"),
        new("W. Europe Standard Time", "Amsterdam"),
        new("Russian Standard Time", "Moskova"),
        new("Azerbaijan Standard Time", "Bakü"),
        new("Arabian Standard Time", "Dubai"),
        new("India Standard Time", "Yeni Delhi"),
        new("Singapore Standard Time", "Singapur"),
        new("China Standard Time", "Pekin"),
        new("Tokyo Standard Time", "Tokyo"),
        new("Korea Standard Time", "Seul"),
        new("AUS Eastern Standard Time", "Sidney"),
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

/// <summary>Widget'ta gösterilen tek bir şehir.</summary>
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
            Details = $"{City.Label}: saat dilimi bulunamadı ({City.TimeZoneId})";
            return;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, Zone);
        Time = local;
        var culture = CultureInfo.CurrentCulture;
        TimeText = use24Hour ? local.ToString("H:mm", culture) : local.ToString("h:mm tt", culture);

        var diff = Zone.GetUtcOffset(utcNow) - TimeZoneInfo.Local.GetUtcOffset(utcNow);
        string offset = diff == TimeSpan.Zero ? "yerel saatle aynı"
            : $"{(diff > TimeSpan.Zero ? "+" : "−")}{Math.Abs(diff.TotalHours):0.##} sa";
        int dayDiff = (local.Date - DateTime.Now.Date).Days;
        string day = dayDiff switch { > 0 => " · yarın", < 0 => " · dün", _ => "" };
        Details = $"{City.Label} — {local.ToString("dddd HH:mm", culture)}\n{offset}{day}";
    }
}

/// <summary>Bir veya birden fazla şehrin saati.</summary>
public partial class WorldClockWidget : WidgetBase
{
    private readonly ObservableCollection<WorldClockItem> _items = new();
    private WorldClockSettings _settings = new();

    public WorldClockWidget()
    {
        InitializeComponent();
        Layout_multi.ItemsSource = _items;
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
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_single, Layout_multi);
        Rebuild();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void OnCitiesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnTick(object? sender, DateTime e) => Update();

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
            ToolTip = _items.Count == 0 ? "Şehir ekleyin" : string.Join("\n\n", _items.Select(i => i.Details));
            RefreshCompact();
            return;
        }
        var first = _items.FirstOrDefault();
        SingleTime.Text = first?.TimeText ?? "--:--";
        SingleCity.Text = first?.Label ?? "Şehir ekleyin";
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
