using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CustomDock.Core;
using ManagedShell.WindowsTasks;

namespace CustomDock.Dock;

public sealed partial class WindowPreviewWindow
{
    // ------------------------------------------------------------------ Media buttons

    private static Services.MediaAppSession? FindMedia(ApplicationWindow? window)
    {
        if (window is null) return null;
        _ = AppServices.Media.EnsureStartedAsync();
        string? exe = null, aumid = null;
        try { exe = Path.GetFileName(window.WinFileName); } catch { /* ignore */ }
        try { aumid = window.AppUserModelID; } catch { /* ignore */ }
        return AppServices.Media.FindSession(exe, aumid);
    }

    /// <summary>
    /// Shows the buttons under the window that plays: the only window, or the browser window whose title contains the
    /// track (a YouTube tab's title is the video's), otherwise the first one.
    /// </summary>
    private async Task ShowMediaButtonsAsync(Services.MediaAppSession media, List<(Grid Row, ApplicationWindow Window)> cards, int version)
    {
        var target = cards[0];
        if (cards.Count > 1)
        {
            string title = await media.GetTitleAsync();
            if (version != _cardsVersion) return;
            if (title.Length > 0)
            {
                foreach (var card in cards)
                {
                    if ((card.Window.Title ?? "").Contains(title, StringComparison.CurrentCultureIgnoreCase))
                    {
                        target = card;
                        break;
                    }
                }
            }
        }
        BuildMediaRow(target.Row, media);
    }

    private static void BuildMediaRow(Grid row, Services.MediaAppSession media)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var style = Application.Current.TryFindResource("DockButton") as Style;
        Button Make(string glyph, string tip, Func<Task> action, bool enabled)
        {
            var button = new Button
            {
                Content = glyph,
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                FontSize = 12,
                Width = 30,
                Height = 26,
                Padding = new Thickness(0),
                Style = style,
                ToolTip = L.T(tip),
                IsEnabled = enabled,
                Margin = new Thickness(4, 0, 4, 0),
            };
            button.Click += async (_, e) =>
            {
                e.Handled = true;
                await action();
            };
            return button;
        }

        var play = Make(media.IsPlaying ? "\uE769" : "\uE768", "Play or pause", media.PlayPauseAsync, true);
        play.Click += (_, _) => play.Content = (string)play.Content == "\uE769" ? "\uE768" : "\uE769";
        panel.Children.Add(Make("\uE892", "Previous", media.PreviousAsync, media.CanPrevious));
        panel.Children.Add(play);
        panel.Children.Add(Make("\uE893", "Next", media.NextAsync, media.CanNext));
        row.Children.Add(panel);
        row.Visibility = Visibility.Visible;
    }
}
