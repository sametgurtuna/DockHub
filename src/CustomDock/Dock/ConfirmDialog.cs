using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Controls;

namespace CustomDock.Dock;

public enum DialogButtonKind { Primary, Secondary, Danger }

/// <summary>A button of a <see cref="ConfirmDialog"/>. <paramref name="Id"/> is returned when it's clicked.</summary>
public sealed record DialogButton(string Id, string Text, DialogButtonKind Kind = DialogButtonKind.Secondary, bool IsCancel = false);

/// <summary>Themed confirmation dialog in the style of <see cref="RenameFolderDialog"/>.</summary>
public sealed class ConfirmDialog : Window
{
    private string? _result;

    private ConfirmDialog(string title, string message, string glyph, IReadOnlyList<DialogButton> buttons, Window? owner)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ShowInTaskbar = false;
        Topmost = true;
        if (owner is { IsVisible: true }) Owner = owner;

        Brush Find(string key, Color fallback) => Application.Current.TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);
        Background = Find("WindowBackgroundBrush", Color.FromRgb(30, 30, 34));
        var textPrimary = Find("TextPrimaryBrush", Colors.White);
        var textSecondary = Find("TextSecondaryBrush", Color.FromRgb(170, 170, 175));
        var surface = Find("SurfaceBrush", Color.FromRgb(42, 42, 48));
        var surfaceBorder = Find("SurfaceBorderBrush", Color.FromRgb(65, 65, 75));
        var accent = Find("AccentBlueBrush", Color.FromRgb(0, 120, 215));
        var danger = Find("AccentRedBrush", Color.FromRgb(255, 69, 58));
        var iconFont = Application.Current.TryFindResource("IconFont") as FontFamily ?? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        var root = new StackPanel { Margin = new Thickness(22, 18, 22, 20) };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = glyph, FontFamily = iconFont, FontSize = 18, Margin = new Thickness(0, 0, 10, 0),
            Foreground = buttons.Any(b => b.Kind == DialogButtonKind.Danger) ? danger : accent,
            VerticalAlignment = VerticalAlignment.Center,
        });
        titleRow.Children.Add(new TextBlock
        {
            Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = textPrimary,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 360,
        });
        root.Children.Add(titleRow);
        root.Children.Add(new TextBlock
        {
            Text = message, FontSize = 12.5, Foreground = textSecondary, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 20), LineHeight = 18,
        });

        var buttonPanel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < buttons.Count; i++)
        {
            var spec = buttons[i];
            var button = new Button
            {
                Content = spec.Text,
                MinWidth = 90,
                Height = 32,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                Cursor = Cursors.Hand,
                IsCancel = spec.IsCancel,
                IsDefault = spec.Kind == DialogButtonKind.Primary,
                Background = spec.Kind switch
                {
                    DialogButtonKind.Primary => accent,
                    DialogButtonKind.Danger => danger,
                    _ => surface,
                },
                Foreground = spec.Kind == DialogButtonKind.Secondary ? textPrimary : Brushes.White,
                BorderBrush = surfaceBorder,
                BorderThickness = new Thickness(spec.Kind == DialogButtonKind.Secondary ? 1 : 0),
                FontWeight = spec.Kind == DialogButtonKind.Secondary ? FontWeights.Normal : FontWeights.SemiBold,
            };
            Ui.SetCornerRadius(button, new CornerRadius(6));
            button.Click += (_, _) => { _result = spec.Id; Close(); };
            buttonPanel.Children.Add(button);
        }
        root.Children.Add(buttonPanel);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    /// <summary>Shows the dialog and returns the id of the clicked button, or null if it was dismissed.</summary>
    public static string? Show(string title, string message, string glyph, Window? owner, params DialogButton[] buttons)
    {
        var dialog = new ConfirmDialog(title, message, glyph, buttons, owner);
        dialog.ShowDialog();
        return dialog._result;
    }
}
