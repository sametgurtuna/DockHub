using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CustomDock.Dock;

/// <summary>Simple modal dialog for renaming a dock group folder.</summary>
public sealed class RenameFolderDialog : Window
{
    private readonly TextBox _textBox;

    public RenameFolderDialog(string currentName, Window? owner = null)
    {
        Title = "Rename Folder";
        Width = 360;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;

        if (owner is not null)
            Owner = owner;

        var border = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45 },
        };
        border.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");

        var titleBlock = new TextBlock
        {
            Text = "Rename Folder",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

        _textBox = new TextBox
        {
            Text = currentName,
            FontSize = 13,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 16),
            Height = 32,
        };
        _textBox.SetResourceReference(TextBox.BackgroundProperty, "SurfaceBrush");
        _textBox.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        _textBox.SetResourceReference(TextBox.BorderBrushProperty, "SurfaceBorderBrush");

        var btnCancel = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        btnCancel.Click += (_, _) => { DialogResult = false; Close(); };

        var btnOk = new Button
        {
            Content = "Save",
            Width = 80,
            Height = 30,
            IsDefault = true,
        };
        btnOk.SetResourceReference(Button.BackgroundProperty, "AccentBlueBrush");
        btnOk.SetResourceReference(Button.ForegroundProperty, "TextOnColorBrush");
        btnOk.Click += (_, _) => { DialogResult = true; Close(); };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { btnCancel, btnOk },
        };

        var root = new StackPanel
        {
            Children = { titleBlock, _textBox, buttonPanel },
        };
        border.Child = root;
        Content = border;

        Loaded += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };
    }

    public string ResultName => _textBox.Text.Trim();

    public static string? Prompt(string currentName, Window? owner = null)
    {
        var dlg = new RenameFolderDialog(currentName, owner);
        return dlg.ShowDialog() == true ? dlg.ResultName : null;
    }
}
