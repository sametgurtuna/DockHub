using System.Windows;
using System.Windows.Controls;
using CustomDock.Controls;
using CustomDock.Core;

namespace CustomDock.Settings;

/// <summary>Settings › Profiles.</summary>
public partial class SettingsWindow
{
    private void LoadProfiles()
    {
        AppServices.Profiles.Changed -= RefreshProfiles;
        AppServices.Profiles.Changed += RefreshProfiles;
        Closed += (_, _) => AppServices.Profiles.Changed -= RefreshProfiles;
        RefreshProfiles();
    }

    private void RefreshProfiles()
    {
        ProfileRows.Children.Clear();
        var service = AppServices.Profiles;
        foreach (var profile in service.Profiles)
        {
            var p = profile;
            bool active = p.Id == _config.ActiveProfileId;

            var displays = new ComboBox { Width = 150, Margin = new Thickness(0, 0, 8, 0), ToolTip = L.T("Switch to this profile automatically when this many displays are connected.") };
            displays.Items.Add(new ComboBoxItem { Content = L.T("No automatic switch"), Tag = 0 });
            for (int n = 1; n <= 4; n++)
                displays.Items.Add(new ComboBoxItem { Content = n == 1 ? L.T("With 1 display") : L.T("With {0} displays", n), Tag = n });
            displays.SelectedIndex = p.AutoDisplayCount ?? 0;
            displays.SelectionChanged += (_, _) =>
            {
                if (displays.SelectedItem is ComboBoxItem { Tag: int count })
                    service.SetAutoDisplayCount(p.Id, count == 0 ? null : count);
            };

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Children = { displays } };
            if (active)
            {
                actions.Children.Add(new TextBlock { Text = L.T("Active"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
            }
            else
            {
                var switchButton = new Button { Content = L.T("Switch"), Margin = new Thickness(0, 0, 8, 0) };
                switchButton.Click += (_, _) => service.SwitchTo(p.Id);
                var delete = new Button { Content = L.T("Delete") };
                delete.Click += (_, _) => service.Delete(p.Id);
                actions.Children.Add(switchButton);
                actions.Children.Add(delete);
            }

            ProfileRows.Children.Add(new SettingRow
            {
                Glyph = active ? "" : "",
                Header = p.Name,
                Description = active ? L.T("The setup you are using now.") : null,
                Content = actions,
            });
            if (service.Profiles.Count > 1) ProfileRows.Children.Add(BuildRuleEditor(p));
        }
    }

    /// <summary>"Switch in while an app runs" and "switch in during these hours" for one profile.</summary>
    private FrameworkElement BuildRuleEditor(DockProfile profile)
    {
        var service = AppServices.Profiles;
        TextBlock Label(string text, double left = 0) => new()
        {
            Text = L.T(text),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(left, 0, 8, 0),
        };

        var app = new TextBox
        {
            Width = 150, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, Text = profile.AutoApp ?? "",
            ToolTip = L.T("Executable name, for example steam or cs2. The profile stays while any of its windows is open."),
        };
        app.LostFocus += (_, _) => service.SetAutoApp(profile.Id, app.Text);

        var from = new TextBox { Width = 64, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, Text = profile.AutoTimeFrom ?? "", ToolTip = "08:30" };
        var to = new TextBox { Width = 64, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center, Text = profile.AutoTimeTo ?? "", ToolTip = "17:30" };
        var weekdays = new CheckBox
        {
            Content = L.T("Weekdays only"), IsChecked = profile.AutoWeekdaysOnly,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
        };
        void SaveTime() => service.SetAutoTime(profile.Id, from.Text, to.Text, weekdays.IsChecked == true);
        from.LostFocus += (_, _) => SaveTime();
        to.LostFocus += (_, _) => SaveTime();
        weekdays.Click += (_, _) => SaveTime();

        var appRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Children = { Label("While this app is running:"), app } };
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { Label("Between"), from, Label("and", 8), to, weekdays } };
        var hint = new TextBlock
        {
            Text = L.T("When the app closes or the time ends, DockHub goes back to the profile you had. An app rule wins over a time rule."),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Margin = new Thickness(0, 8, 0, 0),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");

        var card = new Border
        {
            Padding = new Thickness(52, 10, 16, 12),
            Margin = new Thickness(0, -2, 0, 6),
            CornerRadius = new CornerRadius(0, 0, 6, 6),
            Child = new StackPanel { Children = { appRow, timeRow, hint } },
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        return card;
    }

    private void OnSaveProfileClick(object sender, RoutedEventArgs e)
    {
        AppServices.Profiles.SaveCurrentAs(NewProfileName.Text);
        NewProfileName.Clear();
    }
}
