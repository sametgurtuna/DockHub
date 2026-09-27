using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        InstallWidgetPackage(dialog.FileName);
    }

    /// <summary>Checks a .dockwidget package, shows what it can reach, and installs it when the user agrees.</summary>
    public void InstallWidgetPackage(string packagePath)
    {
        var manifest = WebWidgetCatalog.Inspect(packagePath, out _, out var error);
        if (manifest is null)
        {
            ConfirmDialog.Show(L.T("Can't install this widget"), error ?? "", "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
            return;
        }
        ConfirmAndInstall(manifest);
    }

    /// <summary>Shows what the widget will be able to reach, then installs it. True when installed.</summary>
    private bool ConfirmAndInstall(WebWidgetManifest manifest)
    {
        var permissions = new List<string>();
        if (manifest.Permissions.Network.Count > 0)
            permissions.Add(L.T("Internet access to: {0}", string.Join(", ", manifest.Permissions.Network)));
        if (manifest.Permissions.Notifications) permissions.Add(L.T("Show notifications"));
        if (permissions.Count == 0) permissions.Add(L.T("No internet access, no notifications"));
        string message = $"{manifest.Description}\n\n{L.T("Version {0}", manifest.Version)}" +
                         (manifest.Author is { } author ? $" · {author}" : "") +
                         $"\n\n{L.T("This widget can use:")}\n• " + string.Join("\n• ", permissions);

        if (ConfirmDialog.Show(L.T("Install “{0}”?", manifest.Name), message, "", this,
                new DialogButton("cancel", L.T("Cancel"), IsCancel: true),
                new DialogButton("install", L.T("Install"), DialogButtonKind.Primary)) != "install")
            return false;

        try
        {
            WebWidgetCatalog.Install(manifest);
            RebuildGallery();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Widget install failed");
            ConfirmDialog.Show(L.T("Can't install this widget"), ex.Message, "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
            return false;
        }
    }

    // ------------------------------------------------------------------ Install from link

    private void OnInstallFromLinkClick(object sender, RoutedEventArgs e)
    {
        bool show = WidgetLinkCard.Visibility != Visibility.Visible;
        WidgetLinkCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        if (Clipboard.ContainsText() && Clipboard.GetText().Trim() is { } text && text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && WidgetLinkBox.Text.Length == 0)
            WidgetLinkBox.Text = text;
        WidgetLinkBox.Focus();
        WidgetLinkBox.SelectAll();
    }

    private void OnWidgetLinkKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OnInstallLinkConfirmClick(sender, e);
    }

    private async void OnInstallLinkConfirmClick(object sender, RoutedEventArgs e)
    {
        string link = WidgetLinkBox.Text.Trim();
        if (link.Length == 0) return;
        if (await DownloadAndInstallAsync(new[] { link }, WidgetLinkInstallButton, WidgetLinkStatus))
        {
            WidgetLinkBox.Text = "";
            WidgetLinkCard.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Downloads (showing progress on the button), then asks before installing.</summary>
    private async Task<bool> DownloadAndInstallAsync(IReadOnlyList<string> links, Button button, TextBlock? status)
    {
        object content = button.Content;
        button.IsEnabled = false;
        button.Content = L.T("Downloading…");
        if (status is not null) status.Visibility = Visibility.Collapsed;
        try
        {
            var manifest = await WebWidgetDownloader.DownloadFirstAsync(links);
            return ConfirmAndInstall(manifest);
        }
        catch (Exception ex) when (ex is WebWidgetDownloader.DownloadException or HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Warn($"Widget download failed: {ex.Message}");
            string message = ex is WebWidgetDownloader.DownloadException ? ex.Message : L.T("Couldn't download the widget: {0}", ex.Message);
            if (status is not null)
            {
                status.Text = message;
                status.Visibility = Visibility.Visible;
            }
            else
            {
                ConfirmDialog.Show(L.T("Can't install this widget"), message, "", this, new DialogButton("ok", "OK", DialogButtonKind.Primary));
            }
            return false;
        }
        finally
        {
            button.Content = content;
            button.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------------ Featured

    private void BuildFeaturedWidgets()
    {
        FeaturedWidgetsPanel.Children.Clear();
        foreach (var featured in WebWidgetDownloader.Featured)
        {
            var installed = WebWidgetCatalog.Installed.FirstOrDefault(m => m.Id == featured.Id);
            var button = new Button
            {
                Content = installed is null ? L.T("Install") : L.T("Installed"),
                IsEnabled = installed is null,
                Padding = new Thickness(14, 4, 14, 4),
                MinWidth = 90,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (installed is null) button.SetResourceReference(StyleProperty, "AccentButton");
            button.Click += async (_, _) => await DownloadAndInstallAsync(featured.Links, button, null);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var title = new TextBlock { Text = featured.Name };
            title.SetResourceReference(StyleProperty, "SettingTitle");
            var description = new TextBlock { Text = featured.Description };
            description.SetResourceReference(StyleProperty, "SettingDescription");
            text.Children.Add(title);
            text.Children.Add(description);

            var row = new DockPanel();
            DockPanel.SetDock(button, System.Windows.Controls.Dock.Right);
            row.Children.Add(button);
            row.Children.Add(text);
            var card = new Border { Child = row, Margin = new Thickness(0, 0, 0, 6) };
            card.SetResourceReference(StyleProperty, "SettingCard");
            FeaturedWidgetsPanel.Children.Add(card);
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
        BuildFeaturedWidgets();
    }
}
