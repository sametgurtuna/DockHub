using System.Diagnostics;
using System.Windows;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;
using CustomDock.Shell;
using Microsoft.Win32;

namespace CustomDock.Settings;

/// <summary>Settings › Backup and troubleshooting, and the crash rows of About.</summary>
public partial class SettingsWindow
{
    private void OnExportSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = L.T("Export DockHub settings"),
            FileName = $"DockHub-backup-{DateTime.Now:yyyy-MM-dd}.zip",
            Filter = L.T("DockHub backup") + " (*.zip)|*.zip",
            DefaultExt = ".zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            BackupService.Export(dialog.FileName);
            ConfirmDialog.Show(L.T("Settings exported"), L.T("Saved to {0}", dialog.FileName), "", this,
                new DialogButton("ok", "OK", DialogButtonKind.Primary));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export failed");
            ConfirmDialog.Show(L.T("Export failed"), ex.Message, "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
        }
    }

    private void OnImportSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L.T("Import DockHub settings"),
            Filter = L.T("DockHub backup") + " (*.zip)|*.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;

        if (BackupService.Validate(dialog.FileName) is { } error)
        {
            ConfirmDialog.Show(L.T("Can't import this file"), error, "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
            return;
        }

        if (ConfirmDialog.Show(L.T("Replace your current setup?"),
                L.T("Your current settings are saved to the backups folder first. DockHub restarts to load the backup."),
                "", this,
                new DialogButton("cancel", L.T("Cancel"), IsCancel: true),
                new DialogButton("import", L.T("Import and restart"), DialogButtonKind.Primary)) != "import")
            return;

        try
        {
            BackupService.Import(dialog.FileName);
            AppServices.ConfigService.DisableSaving();
            App.Instance.RestartApplication();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Import failed");
            ConfirmDialog.Show(L.T("Import failed"), ex.Message, "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
        }
    }

    private void OnOpenBackupsClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(BackupService.BackupDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupService.BackupDir}\"") { UseShellExecute = true });
    }

    // ------------------------------------------------------------------ Sync between PCs

    /// <summary>The sync folder, what sync did last, and the buttons to choose a folder or stop.</summary>
    private void LoadSync()
    {
        bool on = _config.SyncFolder is not null;
        SyncStopButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        SyncChooseButton.Content = on ? L.T("Change folder…") : L.T("Choose folder…");
        string? status = AppServices.SyncStarted ? AppServices.Sync.Status : null;
        SyncStatusText.Text = !on
            ? L.T("Off. Nothing is written to any folder.")
            : status is null ? L.T("Syncing through {0}.", _config.SyncFolder!) : L.T("Syncing through {0}.", _config.SyncFolder!) + " " + status;
    }

    private void OnChooseSyncFolderClick(object sender, RoutedEventArgs e)
    {
        // OneDrive, when there is one, is where settings sync usually lives.
        string start = _config.SyncFolder
                       ?? Environment.GetEnvironmentVariable("OneDrive")
                       ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dialog = new OpenFolderDialog
        {
            Title = L.T("Folder to sync DockHub's settings through"),
            InitialDirectory = Directory.Exists(start) ? start : "",
        };
        if (dialog.ShowDialog(this) != true) return;
        _config.SyncFolder = dialog.FolderName;
        LoadSync();
    }

    private void OnStopSyncClick(object sender, RoutedEventArgs e)
    {
        _config.SyncFolder = null;
        LoadSync();
    }

    /// <summary>The "restart after a crash is off" and "last crash" rows, shown only when there is something to say.</summary>
    private void LoadCrashInfo()
    {
        var session = SessionState.Load();
        AutoRestartRow.Visibility = session.AutoRestartOff ? Visibility.Visible : Visibility.Collapsed;
        CopyCrashButton.Content = L.T("Copy details");
        if (session.LastCrash is { } crash)
        {
            LastCrashRow.Description = crash.OneLine();
            LastCrashRow.Visibility = Visibility.Visible;
        }
        else
        {
            LastCrashRow.Visibility = Visibility.Collapsed;
        }
    }

    private void OnEnableAutoRestartClick(object sender, RoutedEventArgs e)
    {
        CrashRecovery.EnableAutoRestart();
        LoadCrashInfo();
    }

    private void OnCopyCrashClick(object sender, RoutedEventArgs e)
    {
        if (SessionState.Load().LastCrash is not { } crash) return;
        try
        {
            Clipboard.SetText(crash.Report());
            CopyCrashButton.Content = L.T("Copied");
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            Log.Warn($"Couldn't copy the crash details: {ex.Message}");
            CopyCrashButton.Content = L.T("Failed");
        }
    }
}
