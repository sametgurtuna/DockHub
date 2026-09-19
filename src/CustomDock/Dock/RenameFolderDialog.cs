using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CustomDock.Dock;

/// <summary>Sleek, solid dialog for renaming a dock group folder.</summary>
public sealed class RenameFolderDialog : Window
{
    private readonly TextBox _textBox;

    public RenameFolderDialog(string currentName, Window? owner = null)
    {
        Title = "Rename Folder";
        Width = 380;
        Height = 170;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ShowInTaskbar = false;

        // Solid window background (never transparent)
        var winBg = Application.Current.TryFindResource("WindowBackgroundBrush") as Brush
                    ?? new SolidColorBrush(Color.FromRgb(30, 30, 34));
        Background = winBg;

        var textPrimary = Application.Current.TryFindResource("TextPrimaryBrush") as Brush
                          ?? Brushes.White;
        var textSecondary = Application.Current.TryFindResource("TextSecondaryBrush") as Brush
                            ?? new SolidColorBrush(Color.FromRgb(170, 170, 175));
        var surfaceBg = Application.Current.TryFindResource("SurfaceBrush") as Brush
                        ?? new SolidColorBrush(Color.FromRgb(42, 42, 48));
        var surfaceBorder = Application.Current.TryFindResource("SurfaceBorderBrush") as Brush
                            ?? new SolidColorBrush(Color.FromRgb(65, 65, 75));
        var accentBlue = Application.Current.TryFindResource("AccentBlueBrush") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(0, 120, 215));

        if (owner is not null)
            Owner = owner;

        var rootGrid = new Grid { Margin = new Thickness(20, 16, 20, 16) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Title
        var titleBlock = new TextBlock
        {
            Text = "Rename Folder",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = textPrimary,
            Margin = new Thickness(0, 0, 0, 12),
        };
        Grid.SetRow(titleBlock, 0);
        rootGrid.Children.Add(titleBlock);

        // Input
        _textBox = new TextBox
        {
            Text = currentName,
            FontSize = 13.5,
            Padding = new Thickness(8, 6, 8, 6),
            Background = surfaceBg,
            Foreground = textPrimary,
            BorderBrush = surfaceBorder,
            BorderThickness = new Thickness(1),
            Height = 34,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        };
        Grid.SetRow(_textBox, 1);
        rootGrid.Children.Add(_textBox);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var btnCancel = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0),
            Background = surfaceBg,
            Foreground = textPrimary,
            BorderBrush = surfaceBorder,
            BorderThickness = new Thickness(1),
            IsCancel = true,
            Cursor = Cursors.Hand,
        };
        btnCancel.Click += (_, _) => { DialogResult = false; Close(); };

        var btnOk = new Button
        {
            Content = "Save",
            Width = 80,
            Height = 30,
            Background = accentBlue,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            IsDefault = true,
            Cursor = Cursors.Hand,
        };
        btnOk.Click += (_, _) => { DialogResult = true; Close(); };

        buttonPanel.Children.Add(btnCancel);
        buttonPanel.Children.Add(btnOk);
        Grid.SetRow(buttonPanel, 2);
        rootGrid.Children.Add(buttonPanel);

        Content = rootGrid;

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
