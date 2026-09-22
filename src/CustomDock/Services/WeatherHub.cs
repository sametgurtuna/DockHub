using System.Globalization;
using System.Windows.Threading;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>Weather location (from widget settings).</summary>
public sealed record WeatherLocation(bool UseDevice, double Latitude, double Longitude, string Name, bool Fahrenheit)
{
    public string Key => UseDevice
        ? $"auto|{Fahrenheit}"
        : string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.###},{Longitude:0.###}|{Fahrenheit}");
}

/// <summary>
/// Shared cache so multiple weather widgets (dock + gallery previews) make only one request
/// for the same location. Data is refreshed every 30 minutes and cached to disk.
/// </summary>
public sealed class WeatherHub
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(3);

    private readonly Dictionary<string, Entry> _entries = new();

    public static WeatherHub Instance { get; } = new();

    public sealed class Entry
    {
        internal Entry(WeatherLocation location)
        {
            Location = location;
        }

        public WeatherLocation Location { get; }
        public WeatherData? Data { get; internal set; }
        public string? Error { get; internal set; }
        internal DispatcherTimer? Timer { get; set; }
        internal Task? Pending { get; set; }
        internal int Subscribers { get; set; }
        public event Action? Updated;
        internal void Raise() => Updated?.Invoke();
    }

    public Entry Subscribe(WeatherLocation location)
    {
        if (!_entries.TryGetValue(location.Key, out var entry))
        {
            entry = new Entry(location);
            var cache = JsonStore.LoadData<WeatherCacheFile>("weather-cache");
            if (cache.Entries.TryGetValue(location.Key, out var cached))
                entry.Data = cached;
            _entries[location.Key] = entry;
        }

        entry.Subscribers++;
        if (entry.Timer is null)
        {
            entry.Timer = new DispatcherTimer();
            entry.Timer.Tick += (_, _) => _ = RefreshAsync(entry);
            var age = entry.Data is null ? TimeSpan.MaxValue : DateTime.Now - entry.Data.FetchedAt;
            if (age >= RefreshInterval) _ = RefreshAsync(entry);
            else Schedule(entry, RefreshInterval - age);
        }
        return entry;
    }

    public void Unsubscribe(Entry entry)
    {
        if (--entry.Subscribers > 0) return;
        entry.Timer?.Stop();
        entry.Timer = null;
    }

    public Task RefreshAsync(Entry entry)
    {
        if (entry.Pending is { IsCompleted: false }) return entry.Pending;
        entry.Pending = RefreshCoreAsync(entry);
        return entry.Pending;
    }

    private async Task RefreshCoreAsync(Entry entry)
    {
        var location = entry.Location;
        try
        {
            double lat = location.Latitude, lon = location.Longitude;
            string name = location.Name;
            if (location.UseDevice)
            {
                var device = await AppServices.Weather.GetDeviceLocationAsync();
                if (device is null)
                {
                    entry.Error = "Location disabled";
                    entry.Raise();
                    Schedule(entry, RetryInterval);
                    return;
                }
                (lat, lon) = device.Value;
                name = "My Location";
            }

            var data = await AppServices.Weather.GetForecastAsync(lat, lon, location.Fahrenheit);
            data.LocationName = name;
            entry.Data = data;
            entry.Error = null;
            Save();
            Schedule(entry, RefreshInterval);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get weather data");
            entry.Error = "No connection";
            Schedule(entry, RetryInterval);
        }
        entry.Raise();
    }

    private static void Schedule(Entry entry, TimeSpan delay)
    {
        if (entry.Timer is null) return;
        entry.Timer.Stop();
        entry.Timer.Interval = delay < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : delay;
        entry.Timer.Start();
    }

    private void Save()
    {
        var file = new WeatherCacheFile();
        foreach (var (key, entry) in _entries)
        {
            if (entry.Data is not null) file.Entries[key] = entry.Data;
        }
        JsonStore.SaveData("weather-cache", file);
    }

    private sealed class WeatherCacheFile
    {
        public Dictionary<string, WeatherData> Entries { get; set; } = new();
    }
}
