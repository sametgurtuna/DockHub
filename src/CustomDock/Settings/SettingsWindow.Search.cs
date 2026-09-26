using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CustomDock.Controls;
using CustomDock.Widgets;

namespace CustomDock.Settings;

/// <summary>Search box in the sidebar: finds setting rows on every page and widgets in the gallery.</summary>
public partial class SettingsWindow
{
    public sealed record SearchHit(string Header, string Location, string Page, FrameworkElement? Target, string Haystack);

    private static readonly Dictionary<string, string> PageNames = new()
    {
        ["general"] = "General", ["taskbar"] = "Taskbar", ["appearance"] = "Appearance",
        ["items"] = "Dock items", ["gallery"] = "Widget gallery", ["about"] = "About",
    };

    /// <summary>Extra words people might search for.</summary>
    private static readonly Dictionary<string, string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Auto-hide"] = "hide automatically gizle",
        ["Theme"] = "dark light mode tema karanlık",
        ["Backdrop"] = "blur acrylic glass transparent bulanık",
        ["Size"] = "big small large boyut",
        ["Screen edge"] = "position top left right bottom konum",
        ["Replace taskbar"] = "windows taskbar görev çubuğu",
        ["Start with Windows"] = "autostart startup başlangıç",
        ["Win + number keys open dock apps"] = "hotkey shortcut keyboard kısayol",
        ["Export settings"] = "backup yedek",
        ["Check for updates"] = "update version güncelleme",
    };

    private List<SearchHit>? _searchIndex;

    private List<SearchHit> BuildSearchIndex()
    {
        var hits = new List<SearchHit>();
        foreach (var (key, page) in _pages)
        {
            foreach (var row in FindRows(page))
            {
                if (string.IsNullOrWhiteSpace(row.Header)) continue;
                Keywords.TryGetValue(row.Header, out var extra);
                hits.Add(new SearchHit(row.Header, PageNames[key], key, row, $"{row.Header} {row.Description} {extra}"));
            }
        }
        foreach (var widget in WidgetRegistry.All)
            hits.Add(new SearchHit(widget.Name, "Widget gallery", "gallery", null, $"{widget.Name} {widget.Description} {widget.Category} widget"));
        return hits;
    }

    private static IEnumerable<SettingRow> FindRows(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is SettingRow row) yield return row;
            foreach (var nested in FindRows(child)) yield return nested;
        }
    }

    private void OnSettingsSearchChanged(object sender, TextChangedEventArgs e)
    {
        string query = SettingsSearchBox.Text.Trim();
        SettingsSearchHint.Visibility = SettingsSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (query.Length == 0)
        {
            SearchResults.Visibility = Visibility.Collapsed;
            NavList.Visibility = Visibility.Visible;
            return;
        }

        _searchIndex ??= BuildSearchIndex();
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = _searchIndex
            .Where(h => words.All(w => h.Haystack.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(h => h.Header.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
            .Take(30)
            .ToList();
        SearchResults.ItemsSource = results.Count > 0
            ? results
            : new[] { new SearchHit("No matches", "Try another word", "", null, "") };
        SearchResults.Visibility = Visibility.Visible;
        NavList.Visibility = Visibility.Collapsed;
    }

    private void OnSettingsSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) SettingsSearchBox.Clear();
        else if (e.Key is Key.Down or Key.Enter && SearchResults.Items.Count > 0)
        {
            SearchResults.SelectedIndex = 0;
            if (e.Key == Key.Down) (SearchResults.ItemContainerGenerator.ContainerFromIndex(0) as UIElement)?.Focus();
            e.Handled = true;
        }
    }

    private void OnSearchResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResults.SelectedItem is not SearchHit { Page.Length: > 0 } hit) return;
        NavigateTo(hit.Page);
        if (hit.Target is not null)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                hit.Target.BringIntoView();
                Highlight(hit.Target);
            });
        }
    }

    /// <summary>Briefly outlines the found row's card with the accent color.</summary>
    private static void Highlight(FrameworkElement row)
    {
        if (VisualTreeHelper.GetChildrenCount(row) == 0 || VisualTreeHelper.GetChild(row, 0) is not Border card) return;
        var accent = (Application.Current.TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        var brush = new SolidColorBrush(accent);
        card.BorderBrush = brush;
        card.BorderThickness = new Thickness(2);
        var fade = new ColorAnimation(Color.FromArgb(0, accent.R, accent.G, accent.B), TimeSpan.FromMilliseconds(900))
        {
            BeginTime = TimeSpan.FromMilliseconds(900),
        };
        fade.Completed += (_, _) =>
        {
            card.ClearValue(Border.BorderBrushProperty);
            card.ClearValue(Border.BorderThicknessProperty);
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, fade);
    }
}
