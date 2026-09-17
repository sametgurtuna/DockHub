using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Widgets;

public partial class WeatherSettingsView : UserControl
{
    private readonly WeatherSettings _settings;

    public WeatherSettingsView(WeatherSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        DataContext = settings;
        CelsiusRadio.IsChecked = !settings.UseFahrenheit;
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SearchAsync();
    }

    private async Task SearchAsync()
    {
        var query = SearchBox.Text.Trim();
        if (query.Length < 2) return;

        SetStatus("Aranıyor…");
        ResultsList.ItemsSource = null;
        try
        {
            var results = await AppServices.Weather.SearchCityAsync(query);
            ResultsList.ItemsSource = results;
            SetStatus(results.Count == 0 ? "Sonuç bulunamadı." : null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Şehir araması başarısız");
            SetStatus("Arama yapılamadı. İnternet bağlantınızı kontrol edin.");
        }
    }

    private void OnResultClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GeoResult result) return;
        _settings.Latitude = result.Latitude;
        _settings.Longitude = result.Longitude;
        _settings.CityName = result.Name;
        _settings.LocationMode = WeatherLocationMode.City;
        ResultsList.ItemsSource = null;
        SearchBox.Text = "";
        SetStatus($"{result.DisplayName} seçildi.");
    }

    private void SetStatus(string? text)
    {
        StatusText.Text = text ?? "";
        StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
