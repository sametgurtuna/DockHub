using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using CustomDock.Core;
using Windows.Devices.Geolocation;

namespace CustomDock.Services;

public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Thunder }

public sealed class WeatherData
{
    public double Temperature { get; set; }
    public double ApparentTemperature { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public int WeatherCode { get; set; }
    public bool IsDay { get; set; } = true;
    public double Humidity { get; set; }
    public double WindSpeed { get; set; }
    public string Unit { get; set; } = "°C";
    public string LocationName { get; set; } = "";
    public DateTime FetchedAt { get; set; }
    public List<HourlyForecast> Hours { get; set; } = new();

    [JsonIgnore]
    public WeatherKind Kind => WeatherService.KindFromCode(WeatherCode);

    [JsonIgnore]
    public string Description => WeatherService.DescribeCode(WeatherCode);
}

public sealed class HourlyForecast
{
    public DateTime Time { get; set; }
    public double Temperature { get; set; }
    public int WeatherCode { get; set; }
    public bool IsDay { get; set; } = true;

    [JsonIgnore]
    public WeatherKind Kind => WeatherService.KindFromCode(WeatherCode);
}

public sealed record GeoResult(string Name, string Region, string Country, double Latitude, double Longitude)
{
    public string DisplayName =>
        string.Join(", ", new[] { Name, Region, Country }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
}

/// <summary>Open-Meteo (API anahtarı gerektirmez) ile hava durumu ve şehir arama.</summary>
public sealed class WeatherService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CustomDock/1.0 (+https://open-meteo.com)");
        return client;
    }

    public async Task<WeatherData> GetForecastAsync(double latitude, double longitude, bool fahrenheit, CancellationToken ct = default)
    {
        var inv = CultureInfo.InvariantCulture;
        var url = "https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={latitude.ToString("0.####", inv)}&longitude={longitude.ToString("0.####", inv)}" +
                  "&current=temperature_2m,apparent_temperature,weather_code,is_day,relative_humidity_2m,wind_speed_10m" +
                  "&daily=temperature_2m_max,temperature_2m_min&forecast_days=2&timezone=auto" +
                  "&hourly=temperature_2m,weather_code,is_day&forecast_hours=7" +
                  (fahrenheit ? "&temperature_unit=fahrenheit" : "");

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
        var current = doc.RootElement.GetProperty("current");
        var daily = doc.RootElement.GetProperty("daily");

        var hours = new List<HourlyForecast>();
        if (doc.RootElement.TryGetProperty("hourly", out var hourly))
        {
            var times = hourly.GetProperty("time");
            var temps = hourly.GetProperty("temperature_2m");
            var codes = hourly.GetProperty("weather_code");
            var days = hourly.GetProperty("is_day");
            for (int i = 0; i < times.GetArrayLength(); i++)
            {
                if (!DateTime.TryParse(times[i].GetString(), inv, DateTimeStyles.None, out var time)) continue;
                hours.Add(new HourlyForecast
                {
                    Time = time,
                    Temperature = temps[i].ValueKind == JsonValueKind.Number ? temps[i].GetDouble() : double.NaN,
                    WeatherCode = codes[i].ValueKind == JsonValueKind.Number ? codes[i].GetInt32() : 3,
                    IsDay = days[i].ValueKind != JsonValueKind.Number || days[i].GetInt32() == 1,
                });
            }
        }

        return new WeatherData
        {
            Hours = hours,
            Temperature = current.GetProperty("temperature_2m").GetDouble(),
            ApparentTemperature = current.GetProperty("apparent_temperature").GetDouble(),
            WeatherCode = current.GetProperty("weather_code").GetInt32(),
            IsDay = current.GetProperty("is_day").GetInt32() == 1,
            Humidity = current.GetProperty("relative_humidity_2m").GetDouble(),
            WindSpeed = current.GetProperty("wind_speed_10m").GetDouble(),
            High = daily.GetProperty("temperature_2m_max")[0].GetDouble(),
            Low = daily.GetProperty("temperature_2m_min")[0].GetDouble(),
            Unit = fahrenheit ? "°F" : "°C",
            FetchedAt = DateTime.Now,
        };
    }

    public async Task<IReadOnlyList<GeoResult>> SearchCityAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<GeoResult>();
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query.Trim())}&count=8&language={language}&format=json";

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
        if (!doc.RootElement.TryGetProperty("results", out var results)) return Array.Empty<GeoResult>();

        return results.EnumerateArray().Select(r => new GeoResult(
            r.GetProperty("name").GetString() ?? "",
            r.TryGetProperty("admin1", out var admin) ? admin.GetString() ?? "" : "",
            r.TryGetProperty("country", out var country) ? country.GetString() ?? "" : "",
            r.GetProperty("latitude").GetDouble(),
            r.GetProperty("longitude").GetDouble())).ToList();
    }

    /// <summary>
    /// Windows konum servisinden konum alır. Ayarlar &gt; Gizlilik &gt; Konum altında
    /// "Masaüstü uygulamalarının konumunuza erişmesine izin ver" açık olmalıdır.
    /// </summary>
    public async Task<(double Latitude, double Longitude)?> GetDeviceLocationAsync()
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed) return null;

            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(10));
            var point = position.Coordinate.Point.Position;
            return (point.Latitude, point.Longitude);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cihaz konumu alınamadı");
            return null;
        }
    }

    public static WeatherKind KindFromCode(int code) => code switch
    {
        0 or 1 => WeatherKind.Clear,
        2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        51 or 53 or 55 or 56 or 57 => WeatherKind.Drizzle,
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => WeatherKind.Rain,
        71 or 73 or 75 or 77 or 85 or 86 => WeatherKind.Snow,
        95 or 96 or 99 => WeatherKind.Thunder,
        _ => WeatherKind.Cloudy,
    };

    public static string DescribeCode(int code) => code switch
    {
        0 => "Açık",
        1 => "Çoğunlukla açık",
        2 => "Parçalı bulutlu",
        3 => "Kapalı",
        45 or 48 => "Sisli",
        51 or 53 or 55 => "Çisenti",
        56 or 57 => "Donan çisenti",
        61 => "Hafif yağış",
        63 => "Yağışlı",
        65 => "Kuvvetli yağış",
        66 or 67 => "Donan yağış",
        71 => "Hafif kar",
        73 => "Karlı",
        75 => "Yoğun kar",
        77 => "Kar taneleri",
        80 or 81 => "Sağanak",
        82 => "Şiddetli sağanak",
        85 or 86 => "Kar sağanağı",
        95 => "Gök gürültülü",
        96 or 99 => "Dolulu fırtına",
        _ => "—",
    };
}
