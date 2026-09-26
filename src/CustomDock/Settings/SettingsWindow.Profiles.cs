using System.Windows;
using System.Windows.Controls;
using CustomDock.Controls;
using CustomDock.Core;

namespace CustomDock.Settings;

/// <summary>Settings › General › Profiles.</summary>
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
        }
    }

    private void OnSaveProfileClick(object sender, RoutedEventArgs e)
    {
        AppServices.Profiles.SaveCurrentAs(NewProfileName.Text);
        NewProfileName.Clear();
    }
}
