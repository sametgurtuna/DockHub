using System.Windows;
using System.Windows.Controls;
using CustomDock.Core;
using CustomDock.Dock;
using Microsoft.Win32;

namespace CustomDock.Settings;

/// <summary>Settings › Appearance: text size, theme files and saved layouts.</summary>
public partial class SettingsWindow
{
    private static readonly double[] TextScaleChoices = { 0, 1, 1.15, 1.3, 1.5 };
    private bool _loadingTextScale;

    private void LoadTextScale()
    {
        _loadingTextScale = true;
        TextScaleCombo.Items.Clear();
        foreach (var value in TextScaleChoices)
        {
            string label = value == 0
                ? L.T("System ({0})", TextScale.SystemFactor.ToString("P0", System.Globalization.CultureInfo.CurrentCulture))
                : value.ToString("P0", System.Globalization.CultureInfo.CurrentCulture);
            TextScaleCombo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        }
        int index = Array.FindIndex(TextScaleChoices, v => Math.Abs(v - _config.TextScale) < 0.001);
        TextScaleCombo.SelectedIndex = index < 0 ? 0 : index;
        _loadingTextScale = false;

        TextScale.Changed -= ApplyTextScale;
        TextScale.Changed += ApplyTextScale;
        Closed += (_, _) => TextScale.Changed -= ApplyTextScale;
        ApplyTextScale();
    }

    private void ApplyTextScale()
    {
        TextScale.Apply(RootGrid);
        // Larger text reads better with a larger dock, which the text size setting doesn't change by itself.
        TextSizeRow.Description = TextScale.Factor >= 1.25 && _config.Size == DockSize.Small
            ? L.T("Text in settings, widget panels and menus. With larger text, a Medium or Large dock is easier to read.")
            : L.T("Text in settings, widget panels and menus. System follows the Windows text size setting.");
    }

    private void OnTextScaleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTextScale || TextScaleCombo.SelectedItem is not ComboBoxItem { Tag: double value }) return;
        _config.TextScale = value;
    }

    // ------------------------------------------------------------------ Theme files

    private void OnExportThemeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = L.T("Export theme"),
            FileName = L.T("My dock") + ThemeFile.Extension,
            Filter = $"{L.T("DockHub theme")} (*{ThemeFile.Extension})|*{ThemeFile.Extension}",
            DefaultExt = ThemeFile.Extension,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ThemeFile.Export(_config, dialog.FileName);
            ConfirmDialog.Show(L.T("Theme exported"), L.T("Saved to {0}", dialog.FileName), "", this,
                new DialogButton("ok", L.T("OK"), DialogButtonKind.Primary));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Theme export failed");
            ConfirmDialog.Show(L.T("Export failed"), ex.Message, "", this, new DialogButton("ok", L.T("OK"), DialogButtonKind.Primary));
        }
    }

    private void OnImportThemeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L.T("Import theme"),
            Filter = $"{L.T("DockHub theme")} (*{ThemeFile.Extension})|*{ThemeFile.Extension}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;
        if (ThemeFile.Import(AppServices.ConfigService, dialog.FileName) is { } error)
            ConfirmDialog.Show(L.T("Can't use this theme"), error, "", this, new DialogButton("ok", L.T("OK"), DialogButtonKind.Primary));
    }

    // ------------------------------------------------------------------ Saved layouts

    private void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        LayoutPresets.SaveCurrent(NewPresetName.Text, AppServices.ConfigService);
        NewPresetName.Clear();
        LoadPresets();
    }

    // ------------------------------------------------------------------ Top bar

    private sealed record BackdropChoice(BackdropKind? Kind, string Label)
    {
        public override string ToString() => Label;
    }

    private bool _loadingTopBar;

    /// <summary>The bar's backdrop: the dock's, or one of its own.</summary>
    private void LoadTopBar()
    {
        _loadingTopBar = true;
        var choices = new List<BackdropChoice>
        {
            new(null, L.T("Same as the dock")),
            new(BackdropKind.Blur, L.T("Blur")),
            new(BackdropKind.Acrylic, L.T("Acrylic")),
            new(BackdropKind.Transparent, L.T("Transparent")),
            new(BackdropKind.Solid, L.T("Solid")),
        };
        TopBarBackdropCombo.ItemsSource = choices;
        TopBarBackdropCombo.SelectedItem = choices.FirstOrDefault(c => c.Kind == _config.TopBar.Backdrop) ?? choices[0];
        _loadingTopBar = false;
    }

    private void OnTopBarBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTopBar || TopBarBackdropCombo.SelectedItem is not BackdropChoice choice) return;
        _config.TopBar.Backdrop = choice.Kind;
    }
}
