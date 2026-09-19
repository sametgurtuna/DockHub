using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Controls;

namespace CustomDock.Dock;

/// <summary>Sleek, modern dialog for renaming a dock group folder.</summary>
public sealed class RenameFolderDialog : Window
{
    private readonly TextBox _textBox;

    public RenameFolderDialog(string currentName, Window? owner = null)
    {
        Title = "Rename Folder";
        Width = 410;
        SizeToContent = SizeToContent.Height;
        MinHeight = 195;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ShowInTaskbar = false;
        Topmost = true;

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

        if (owner is not null && owner.IsVisible)
            Owner = owner;

        var rootGrid = new Grid { Margin = new Thickness(22, 18, 22, 20) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header: Folder icon + Title + Subtitle
        var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var iconFont = Application.Current.TryFindResource("IconFont") as FontFamily
                       ?? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
        var folderIcon = new TextBlock
        {
            Text = "\uE8B7",
            FontFamily = iconFont,
            FontSize = 18,
            Foreground = accentBlue,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var titleBlock = new TextBlock
        {
            Text = "Rename Folder",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = textPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleRow.Children.Add(folderIcon);
        titleRow.Children.Add(titleBlock);

        var subtitleBlock = new TextBlock
        {
            Text = "Choose a new name for this folder group on your dock.",
            FontSize = 11.5,
            Foreground = textSecondary,
            Margin = new Thickness(0, 5, 0, 0),
        };
        headerStack.Children.Add(titleRow);
        headerStack.Children.Add(subtitleBlock);
        Grid.SetRow(headerStack, 0);
        rootGrid.Children.Add(headerStack);

        // Input
        _textBox = new TextBox
        {
            Text = currentName,
            FontSize = 13.5,
            Padding = new Thickness(10, 7, 10, 7),
            Background = surfaceBg,
            Foreground = textPrimary,
            BorderBrush = surfaceBorder,
            BorderThickness = new Thickness(1),
            Height = 36,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20),
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
            Width = 85,
            Height = 32,
            Margin = new Thickness(0, 0, 10, 0),
            Background = surfaceBg,
            Foreground = textPrimary,
            BorderBrush = surfaceBorder,
            BorderThickness = new Thickness(1),
            IsCancel = true,
            Cursor = Cursors.Hand,
        };
        Ui.SetCornerRadius(btnCancel, new CornerRadius(6));
        btnCancel.Click += (_, _) => { DialogResult = false; Close(); };

        var btnOk = new Button
        {
            Content = "Save",
            Width = 85,
            Height = 32,
            Background = accentBlue,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            IsDefault = true,
            Cursor = Cursors.Hand,
        };
        Ui.SetCornerRadius(btnOk, new CornerRadius(6));
        btnOk.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_textBox.Text))
                _textBox.Text = "Folder";
            DialogResult = true;
            Close();
        };

        buttonPanel.Children.Add(btnCancel);
        buttonPanel.Children.Add(btnOk);
        Grid.SetRow(buttonPanel, 2);
        rootGrid.Children.Add(buttonPanel);

        Content = rootGrid;

        Loaded += (_, _) =>
        {
            _textBox.Focus();
            _textBox.CaretIndex = _textBox.Text.Length;
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
