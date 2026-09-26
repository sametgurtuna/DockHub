using System.Diagnostics;
using System.Windows;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Settings;

/// <summary>Settings › About › Updates.</summary>
public partial class SettingsWindow
{
    private void LoadUpdates()
    {
        ShowUpdate(AppServices.Updates.Available);
        UpdateStatusRow.Description = AppServices.Updates.LastChecked is { } last
            ? $"You have {UpdateService.CurrentVersion}. Last checked {last:g}."
            : $"You have {UpdateService.CurrentVersion}.";
    }

    private void ShowUpdate(ReleaseInfo? release)
    {
        UpdateCard.Visibility = release is null ? Visibility.Collapsed : Visibility.Visible;
        if (release is null) return;
        UpdateTitle.Text = L.T("DockHub {0} is available", release.Version);
        var notes = release.Notes.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).Take(8);
        UpdateNotes.Text = string.Join("\n", notes);
        InstallUpdateButton.IsEnabled = release.InstallerUrl is not null;
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = L.T("Checking…");
        try
        {
            var release = await AppServices.Updates.CheckAsync(userInitiated: true);
            ShowUpdate(release);
            UpdateStatusRow.Description = release is null
                ? $"DockHub {UpdateService.CurrentVersion} is the latest version."
                : $"You have {UpdateService.CurrentVersion}.";
        }
        catch (Exception ex)
        {
            UpdateStatusRow.Description = L.T("Couldn't reach GitHub: {0}", ex.Message);
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButton.Content = L.T("Check now");
        }
    }

    private async void OnInstallUpdateClick(object sender, RoutedEventArgs e)
    {
        if (AppServices.Updates.Available is not { } release) return;
        if (ConfirmDialog.Show($"Install DockHub {release.Version}?",
                "DockHub closes (your Windows taskbar comes back briefly), updates and starts again. Your settings are kept.",
                "", this,
                new DialogButton("cancel", "Cancel", IsCancel: true),
                new DialogButton("install", "Install", DialogButtonKind.Primary)) != "install")
            return;

        InstallUpdateButton.IsEnabled = false;
        var progress = new Progress<double>(p => UpdateProgress.Text = L.T("Downloading… {0}", p.ToString("P0", System.Globalization.CultureInfo.CurrentCulture)));
        try
        {
            await AppServices.Updates.DownloadAndInstallAsync(release, progress);
            UpdateProgress.Text = L.T("Installing…");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update failed");
            UpdateProgress.Text = "";
            InstallUpdateButton.IsEnabled = true;
            ConfirmDialog.Show("Update failed", ex.Message, "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
        }
    }

    private void OnShowWelcomeClick(object sender, RoutedEventArgs e) => App.Instance.ShowWelcome();

    private void OnReleaseNotesClick(object sender, RoutedEventArgs e)
    {
        string url = AppServices.Updates.Available?.PageUrl ?? "https://github.com/sametgurtuna/DockHub/releases";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
