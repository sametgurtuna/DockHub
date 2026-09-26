using System.Diagnostics;
using System.Windows;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Widgets.Web;
using Microsoft.Win32;

namespace CustomDock.Settings;

/// <summary>Settings › Widget gallery: installing HTML/JavaScript widgets (.dockwidget packages).</summary>
public partial class SettingsWindow
{
    private void OnInstallWebWidgetClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L.T("Install a widget"),
            Filter = "DockHub widget (*.dockwidget;*.zip)|*.dockwidget;*.zip",
        };
        if (dialog.ShowDialog(this) != true) return;

        var manifest = WebWidgetCatalog.Inspect(dialog.FileName, out _, out var error);
        if (manifest is null)
        {
            ConfirmDialog.Show(L.T("Can't install this widget"), error ?? "", "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
            return;
        }

        // Tell the user what the widget will be able to reach before installing it.
        var permissions = new List<string>();
        if (manifest.Permissions.Network.Count > 0)
            permissions.Add(L.T("Internet access to: {0}", string.Join(", ", manifest.Permissions.Network)));
        if (manifest.Permissions.Notifications) permissions.Add(L.T("Show notifications"));
        if (permissions.Count == 0) permissions.Add(L.T("No internet access, no notifications"));
        string message = $"{manifest.Description}\n\n{L.T("Version {0}", manifest.Version)}" +
                         (manifest.Author is { } author ? $" · {author}" : "") +
                         $"\n\n{L.T("This widget can use:")}\n• " + string.Join("\n• ", permissions);

        if (ConfirmDialog.Show(L.T("Install “{0}”?", manifest.Name), message, "", this,
                new DialogButton("cancel", L.T("Cancel"), IsCancel: true),
                new DialogButton("install", L.T("Install"), DialogButtonKind.Primary)) != "install")
            return;

        try
        {
            WebWidgetCatalog.Install(manifest);
            RebuildGallery();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Widget install failed");
            ConfirmDialog.Show(L.T("Can't install this widget"), ex.Message, "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
        }
    }

    private void OnOpenWidgetsFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(WebWidgetCatalog.WidgetsDir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{WebWidgetCatalog.WidgetsDir}\"") { UseShellExecute = true });
    }

    private void RebuildGallery()
    {
        foreach (var preview in _previews) preview.Detach();
        _previews.Clear();
        GalleryPanel.Children.Clear();
        BuildGallery();
    }
}
