using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Settings;

/// <summary>The grouped menu (<see cref="SettingsPages"/>) and the Overview page, which opens first.</summary>
public partial class SettingsWindow
{
    private void BuildNavigation()
    {
        NavList.Items.Clear();
        foreach (var group in SettingsPages.Groups)
        {
            if (group.EnglishName is { } heading)
            {
                var header = new ListBoxItem { Content = L.T(heading), IsEnabled = false };
                header.SetResourceReference(StyleProperty, "NavGroupHeader");
                NavList.Items.Add(header);
            }
            else
            {
                // A group without a heading (About) stands a little apart.
                NavList.Items.Add(new ListBoxItem { Height = 10, IsEnabled = false, Focusable = false, IsHitTestVisible = false });
            }

            foreach (var page in group.Pages)
            {
                var glyph = new TextBlock { Text = page.Glyph };
                glyph.SetResourceReference(StyleProperty, "NavGlyph");
                NavList.Items.Add(new ListBoxItem
                {
                    Tag = page.Tag,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { glyph, new TextBlock { Text = L.T(page.EnglishName), Margin = new Thickness(14, 0, 0, 0) } },
                    },
                });
            }
        }
    }

    // ------------------------------------------------------------------ Overview

    private void LoadOverview()
    {
        BuildOverviewActions();

        var version = UpdateService.CurrentVersion;
        if (AppServices.Updates.Available is { } release)
        {
            OverviewVersionRow.Description = L.T("DockHub {0} is available (you have {1}).", release.Version, version);
            OverviewUpdateButton.Content = L.T("Install update");
        }
        else
        {
            OverviewVersionRow.Description = AppServices.Updates.LastChecked is { } last
                ? L.T("You have {0}. Last checked {1}.", version, last.ToString("g", CultureInfo.CurrentCulture))
                : L.T("You have {0}.", version);
            OverviewUpdateButton.Content = L.T("Updates");
        }

        OverviewBackupRow.Description = LatestBackup() is { } backup
            ? L.T("Last saved {0}. DockHub keeps one copy of your settings a day, for a week.", backup.ToString("d", CultureInfo.CurrentCulture))
            : L.T("None yet. DockHub keeps one copy of your settings a day, for a week.");

        var profiles = AppServices.Profiles;
        OverviewProfileRow.Description = profiles.Active is { } active
            ? L.T("{0} ({1} profiles)", active.Name, _config.Profiles.Count)
            : L.T("No profiles yet. Save your setup as one to switch between setups.");
    }

    /// <summary>Date of the newest daily backup, or null.</summary>
    private static DateTime? LatestBackup()
    {
        try
        {
            return BackupService.DailyBackups().FirstOrDefault() is { } file ? File.GetLastWriteTime(file) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void BuildOverviewActions()
    {
        OverviewActions.Children.Clear();
        OverviewActions.Children.Add(ActionTile("", L.T("Add a widget"), () => NavigateTo("gallery")));
        OverviewActions.Children.Add(ActionTile("", L.T("Edit the dock"), DockWindow.EditMainDock));

        var history = AppServices.ConfigService.History;
        var undo = ActionTile("", history.CanUndo && history.Latest is { } last ? L.T("Undo: {0}", last.Description) : L.T("Undo"), () =>
        {
            AppServices.ConfigService.Undo();
            LoadOverview();
        });
        undo.IsEnabled = history.CanUndo;
        OverviewActions.Children.Add(undo);

        bool canSwitch = _config.Profiles.Count > 1;
        OverviewActions.Children.Add(ActionTile("", canSwitch ? L.T("Next profile") : L.T("Profiles"), () =>
        {
            if (!canSwitch)
            {
                NavigateTo("profiles");
                return;
            }
            AppServices.Profiles.SwitchToNext();
            LoadOverview();
        }));
    }

    private Button ActionTile(string glyph, string text, Action run)
    {
        var icon = new TextBlock { Text = glyph, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Left };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        var label = new TextBlock { Text = text, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 36 };
        var tile = new Button
        {
            Width = 176,
            MinHeight = 84,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(14, 12, 14, 12),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            Content = new StackPanel { Children = { icon, label } },
            ToolTip = text,
        };
        tile.Click += (_, _) => run();
        return tile;
    }

    private void OnOverviewUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (AppServices.Updates.Available is not null) OnInstallUpdateClick(sender, e);
        else NavigateTo("about");
    }

    private void OnOverviewBackupClick(object sender, RoutedEventArgs e) => NavigateTo("backup");

    private void OnOverviewProfilesClick(object sender, RoutedEventArgs e) => NavigateTo("profiles");
}
