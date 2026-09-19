using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace CustomDock.Core;

/// <summary>Swaps Dark/Light theme resource dictionaries at runtime and applies the Windows accent color.</summary>
public static class ThemeManager
{
    private static ThemePreference _preference = ThemePreference.Dark;
    private static bool _listening;
    private static UISettings? _uiSettings;

    public static bool IsDark { get; private set; } = true;

    public static event Action? ThemeChanged;

    public static void Apply(ThemePreference preference)
    {
        _preference = preference;
        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        bool dark = preference switch
        {
            ThemePreference.Light => false,
            ThemePreference.System => !SystemUsesLightTheme(),
            _ => true,
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source is { } s && (s.OriginalString.EndsWith("Dark.xaml") || s.OriginalString.EndsWith("Light.xaml")));

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/DockHub;component/Themes/{(dark ? "Dark" : "Light")}.xaml"),
        };
        ApplyAccent(dictionary, dark);

        if (existing is null)
            dictionaries.Insert(0, dictionary);
        else
            dictionaries[dictionaries.IndexOf(existing)] = dictionary;

        IsDark = dark;
        ThemeChanged?.Invoke();
    }

    /// <summary>Applies the Windows accent color (lighter tint in dark theme, darker tint in light theme).</summary>
    private static void ApplyAccent(ResourceDictionary dictionary, bool dark)
    {
        try
        {
            _uiSettings ??= new UISettings();
            var c = _uiSettings.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.AccentDark1);
            var accent = Color.FromArgb(255, c.R, c.G, c.B);
            dictionary["AccentBrush"] = Frozen(accent);
            dictionary["SelectionBrush"] = Frozen(Color.FromArgb(0x33, c.R, c.G, c.B));
            dictionary["ActiveIndicatorBrush"] = Frozen(accent);
            // Black text on light accent, white text on dark accent
            double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
            dictionary["OnAccentBrush"] = Frozen(luminance > 0.55 ? Colors.Black : Colors.White);
        }
        catch
        {
            dictionary["ActiveIndicatorBrush"] = dictionary["AccentBrush"];
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
            return;
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(_preference));
    }
}
