using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using CustomDock.Controls;

namespace CustomDock.Widgets.Web;

/// <summary>Builds the settings rows a web widget declares in its manifest.</summary>
public static class WebWidgetSettingsView
{
    public static FrameworkElement Create(WebWidgetManifest manifest, WebWidgetSettings settings)
    {
        var panel = new StackPanel();
        foreach (var field in manifest.Settings)
        {
            var f = field;
            JsonNode? current = settings.Values[f.Key] ?? (f.Default is { } d ? JsonNode.Parse(d.GetRawText()) : null);
            FrameworkElement editor;
            switch (f.Type)
            {
                case "toggle":
                    var toggle = new CheckBox { IsChecked = current?.GetValueKind() == JsonValueKind.True };
                    toggle.SetResourceReference(FrameworkElement.StyleProperty, "ToggleSwitch");
                    toggle.Click += (_, _) => settings.SetValue(f.Key, toggle.IsChecked == true);
                    editor = toggle;
                    break;
                case "number":
                    var number = new TextBox { Width = 100, Text = current?.ToString() ?? "" };
                    number.LostFocus += (_, _) =>
                    {
                        if (!double.TryParse(number.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)) return;
                        if (f.Min is { } min) value = Math.Max(min, value);
                        if (f.Max is { } max) value = Math.Min(max, value);
                        number.Text = value.ToString(CultureInfo.CurrentCulture);
                        settings.SetValue(f.Key, value);
                    };
                    editor = number;
                    break;
                case "choice" when f.Options is { Count: > 0 }:
                    var choice = new ComboBox { Width = 180, ItemsSource = f.Options, SelectedItem = current?.ToString() };
                    choice.SelectionChanged += (_, _) => settings.SetValue(f.Key, choice.SelectedItem as string);
                    editor = choice;
                    break;
                default:
                    var text = new TextBox { Width = 240, Text = current?.ToString() ?? "" };
                    text.LostFocus += (_, _) => settings.SetValue(f.Key, text.Text);
                    editor = text;
                    break;
            }
            panel.Children.Add(new SettingRow { Header = f.Label, Description = f.Description, Content = editor });
        }
        return panel;
    }
}
