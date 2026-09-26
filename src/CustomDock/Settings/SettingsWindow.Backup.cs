using System.Diagnostics;
using System.Windows;
using CustomDock.Core;
using CustomDock.Dock;
using Microsoft.Win32;

namespace CustomDock.Settings;

/// <summary>Settings › General › Backup and restore.</summary>
public partial class SettingsWindow
{
    private void OnExportSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export DockHub settings",
            FileName = $"DockHub-backup-{DateTime.Now:yyyy-MM-dd}.zip",
            Filter = "DockHub backup (*.zip)|*.zip",
            DefaultExt = ".zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            BackupService.Export(dialog.FileName);
            ConfirmDialog.Show("Settings exported", $"Saved to {dialog.FileName}", "", this,
                new DialogButton("ok", "OK", DialogButtonKind.Primary));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export failed");
            ConfirmDialog.Show("Export failed", ex.Message, "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
        }
    }

    private void OnImportSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import DockHub settings",
            Filter = "DockHub backup (*.zip)|*.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;

        if (BackupService.Validate(dialog.FileName) is { } error)
        {
            ConfirmDialog.Show("Can't import this file", error, "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
            return;
        }

        if (ConfirmDialog.Show("Replace your current setup?",
                "Your current settings are saved to the backups folder first. DockHub restarts to load the backup.",
                "", this,
                new DialogButton("cancel", "Cancel", IsCancel: true),
                new DialogButton("import", "Import and restart", DialogButtonKind.Primary)) != "import")
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
            ConfirmDialog.Show("Import failed", ex.Message, "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
        }
    }

    private void OnOpenBackupsClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(BackupService.BackupDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupService.BackupDir}\"") { UseShellExecute = true });
    }
}
