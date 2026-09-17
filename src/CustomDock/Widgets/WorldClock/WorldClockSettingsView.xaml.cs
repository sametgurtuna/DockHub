using System.Windows;
using System.Windows.Controls;
using CustomDock.Core;

namespace CustomDock.Widgets;

public partial class WorldClockSettingsView : UserControl
{
    private readonly WorldClockSettings _settings;

    public WorldClockSettingsView(WorldClockSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        DataContext = settings;
        CityList.ItemsSource = settings.Cities;
        ZoneCombo.ItemsSource = TimeZoneInfo.GetSystemTimeZones();
        ZoneCombo.SelectedItem = TimeZoneInfo.Local;
        PopularCombo.SelectedIndex = 0;
    }

    private void OnAddPopularClick(object sender, RoutedEventArgs e)
    {
        if (PopularCombo.SelectedItem is Option<string> option)
            Add(option.Label, option.Value);
    }

    private void OnAddZoneClick(object sender, RoutedEventArgs e)
    {
        if (ZoneCombo.SelectedItem is not TimeZoneInfo zone) return;
        var label = string.IsNullOrWhiteSpace(CustomLabel.Text) ? ShortName(zone) : CustomLabel.Text.Trim();
        Add(label, zone.Id);
        CustomLabel.Text = "";
    }

    private static string ShortName(TimeZoneInfo zone)
    {
        // "(UTC+09:00) Osaka, Sapporo, Tokyo" → "Osaka"
        var name = zone.DisplayName;
        int paren = name.IndexOf(')');
        if (paren >= 0) name = name[(paren + 1)..];
        return name.Split(',')[0].Trim();
    }

    private void Add(string label, string zoneId)
    {
        _settings.Cities.Add(new WorldCity { Label = label, TimeZoneId = zoneId });
        _settings.NotifyCitiesChanged();
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WorldCity city) return;
        _settings.Cities.Remove(city);
        _settings.NotifyCitiesChanged();
    }

    private void OnUpClick(object sender, RoutedEventArgs e) => Move(sender, -1);

    private void OnDownClick(object sender, RoutedEventArgs e) => Move(sender, +1);

    private void Move(object sender, int delta)
    {
        if ((sender as FrameworkElement)?.DataContext is not WorldCity city) return;
        int from = _settings.Cities.IndexOf(city);
        int to = Math.Clamp(from + delta, 0, _settings.Cities.Count - 1);
        if (from == to) return;
        _settings.Cities.Move(from, to);
        _settings.NotifyCitiesChanged();
    }

    private void OnLabelLostFocus(object sender, RoutedEventArgs e) => _settings.NotifyCitiesChanged();
}
