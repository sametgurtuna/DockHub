using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CustomDock.Core;

public enum UiLanguage { System, English, Turkish }

/// <summary>
/// Interface translation, gettext style: the English text is the key and <c>Resources/Strings_tr.json</c> holds the
/// Turkish text. Text in XAML is translated as each element loads (class handlers), so templates, popups and
/// widgets are covered without markup changes; code uses <see cref="T(string)"/>. Missing entries stay English.
/// </summary>
public static class L
{
    private static Dictionary<string, string> s_strings = new();
    private static bool s_hooked;

    /// <summary>Two-letter code of the active interface language ("en" or "tr").</summary>
    public static string Code { get; private set; } = "en";

    public static bool IsTranslated => s_strings.Count > 0;

    /// <summary>Loads the language chosen in settings (System follows the Windows display language).</summary>
    public static void Initialize(UiLanguage language)
    {
        string code = language switch
        {
            UiLanguage.English => "en",
            UiLanguage.Turkish => "tr",
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en",
        };
        Code = code;
        s_strings = code == "en" ? new() : LoadStrings(code);
        if (code != "en")
        {
            var culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
        if (IsTranslated) HookElements();
    }

    private static Dictionary<string, string> LoadStrings(string code)
    {
        try
        {
            using var stream = typeof(L).Assembly.GetManifestResourceStream($"CustomDock.Resources.Strings_{code}.json");
            if (stream is null) return new();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to load {code} strings");
            return new();
        }
    }

    /// <summary>Translated text, or the English text itself when there is no translation.</summary>
    public static string T(string english)
        => s_strings.Count > 0 && english.Length > 0 && s_strings.TryGetValue(english, out var translated) ? translated : english;

    /// <summary>Translated format string, e.g. <c>T("{0} items", count)</c>.</summary>
    public static string T(string englishFormat, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, T(englishFormat), args);

    // ------------------------------------------------------------------ XAML text

    private static void HookElements()
    {
        if (s_hooked) return;
        s_hooked = true;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnLoaded), true);
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnMenuOpened), true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject element) Translate(element);
    }

    private static void OnMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu) TranslateMenu(menu);
    }

    private static void TranslateMenu(ItemsControl menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            TranslateProperty(item, HeaderedItemsControl.HeaderProperty);
            TranslateMenu(item);
        }
    }

    /// <summary>Translates the static text of one element (not bound values).</summary>
    public static void Translate(DependencyObject element)
    {
        switch (element)
        {
            case TextBlock text:
                if (text.Inlines.Count <= 1) TranslateProperty(text, TextBlock.TextProperty);
                break;
            case Controls.SettingRow row:
                TranslateProperty(row, Controls.SettingRow.HeaderProperty);
                TranslateProperty(row, Controls.SettingRow.DescriptionProperty);
                break;
            case HeaderedItemsControl headered:
                TranslateProperty(headered, HeaderedItemsControl.HeaderProperty);
                break;
            case ContentControl content when content is not Window:
                TranslateProperty(content, ContentControl.ContentProperty);
                break;
            case Window window:
                TranslateProperty(window, Window.TitleProperty);
                break;
        }
        if (element is FrameworkElement fe) TranslateProperty(fe, FrameworkElement.ToolTipProperty);
    }

    private static void TranslateProperty(DependencyObject element, DependencyProperty property)
    {
        // Only local, literal values: bound and templated values come from data that is translated at the source.
        if (element.ReadLocalValue(property) is not string text || text.Length == 0) return;
        if (BindingOperations.IsDataBound(element, property)) return;
        string translated = T(text);
        if (!ReferenceEquals(translated, text)) element.SetCurrentValue(property, translated);
    }
}
