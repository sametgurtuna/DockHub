using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;
using CustomDock.Controls;

namespace CustomDock.Widgets;

public enum WeatherLocationMode { Auto, City }

public sealed class WeatherSettings : ObservableObject
{
    private WeatherLocationMode _locationMode = WeatherLocationMode.City;
    private string _cityName = "İstanbul";
    private double _latitude = 41.0138;
    private double _longitude = 28.9497;
    private bool _useFahrenheit;

    public WeatherLocationMode LocationMode { get => _locationMode; set => Set(ref _locationMode, value); }

    public string CityName { get => _cityName; set => Set(ref _cityName, value); }

    public double Latitude { get => _latitude; set => Set(ref _latitude, value); }

    public double Longitude { get => _longitude; set => Set(ref _longitude, value); }

    public bool UseFahrenheit { get => _useFahrenheit; set => Set(ref _useFahrenheit, value); }

    public WeatherLocation ToLocation()
        => new(LocationMode == WeatherLocationMode.Auto, Latitude, Longitude, CityName, UseFahrenheit);
}

public sealed record HourlyItem(string Hour, WeatherKind Kind, bool IsDay, string Temperature);

/// <summary>Open-Meteo ile güncel / saatlik hava durumu. Tıklayınca yeniler.</summary>
public partial class WeatherWidget : WidgetBase
{
    private WeatherSettings _settings = new();
    private WeatherHub.Entry? _entry;
    private bool _resubscribeQueued;

    public WeatherWidget()
    {
        InitializeComponent();
    }

    protected override void OnAttached()
    {
        _settings = GetSettings<WeatherSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        Subscribe();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        Unsubscribe();
    }

    protected override void OnVariantChanged()
    {
        HourlyList.Visibility = Variant == "hourly" ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }

    private async void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Şehir seçimi birden fazla özelliği art arda değiştirir; tek yeniden abonelik yeterli.
        if (_resubscribeQueued) return;
        _resubscribeQueued = true;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        _resubscribeQueued = false;
        if (!IsAttached) return;
        Unsubscribe();
        Subscribe();
    }

    private void Subscribe()
    {
        _entry = WeatherHub.Instance.Subscribe(_settings.ToLocation());
        _entry.Updated += Render;
        Render();
    }

    private void Unsubscribe()
    {
        if (_entry is null) return;
        _entry.Updated -= Render;
        WeatherHub.Instance.Unsubscribe(_entry);
        _entry = null;
    }

    private async void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (_entry is not null && !IsPreview)
        {
            SubText.Text = "Yenileniyor…";
            await WeatherHub.Instance.RefreshAsync(_entry);
        }
    }

    private void Render()
    {
        RenderCore();
        RefreshCompact();
    }

    private WeatherIcon? _compactIcon;

    protected override void UpdateCompact(CompactTile tile)
    {
        if (_entry?.Data is not { } data)
        {
            tile.ShowGlyph(Descriptor.Icon, Descriptor.AccentKey);
            tile.Text = "--°";
            return;
        }
        if (_compactIcon is null)
        {
            _compactIcon = new WeatherIcon();
            _compactIcon.SetResourceReference(WeatherIcon.CloudBrushProperty, "CloudBrush");
        }
        _compactIcon.Kind = data.Kind;
        _compactIcon.IsDay = data.IsDay;
        tile.SetVisual(_compactIcon);
        tile.Text = $"{Math.Round(data.Temperature):0}°";
    }

    private void RenderCore()
    {
        var data = _entry?.Data;
        if (data is null)
        {
            TempText.Text = "--°";
            SubText.Text = _entry?.Error ?? "Yükleniyor…";
            HourlyList.ItemsSource = null;
            return;
        }

        Icon.Kind = data.Kind;
        Icon.IsDay = data.IsDay;
        TempText.Text = $"{Math.Round(data.Temperature):0}°";
        SubText.Text = Variant == "conditions" ? data.Description : data.LocationName;

        var culture = CultureInfo.CurrentCulture;
        HourlyList.ItemsSource = data.Hours
            .Where(h => h.Time > DateTime.Now.AddMinutes(-30) && !double.IsNaN(h.Temperature))
            .Skip(1)
            .Take(3)
            .Select(h => new HourlyItem(h.Time.ToString("HH", culture), h.Kind, h.IsDay, $"{Math.Round(h.Temperature):0}°"))
            .ToList();

        ToolTip =
            $"{data.LocationName} — {data.Description}\n" +
            $"Sıcaklık {data.Temperature:0.#}{data.Unit} · Hissedilen {data.ApparentTemperature:0}{data.Unit}\n" +
            $"En yüksek {data.High:0}° · En düşük {data.Low:0}°\n" +
            $"Nem %{data.Humidity:0} · Rüzgâr {data.WindSpeed:0} km/sa\n" +
            $"Güncellendi {data.FetchedAt:HH:mm}" + (_entry?.Error is { } error ? $" ({error})" : "") + " · yenilemek için tıklayın";
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item("Yenile", "\uE72C", () => { if (_entry is not null) _ = WeatherHub.Instance.RefreshAsync(_entry); }));
    }
}
