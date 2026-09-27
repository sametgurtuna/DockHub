using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Native;
using ManagedShell.WindowsTasks;

namespace CustomDock.Dock;

public sealed partial class WindowPreviewWindow
{
    private void RebuildCards(List<ApplicationWindow> windows)
    {
        EndPeek();
        UnregisterAllThumbnails();
        _previewItems.Clear();
        _cardsPanel.Children.Clear();
        int version = ++_cardsVersion;

        // Apps playing media (Spotify, a YouTube tab) get play/pause buttons under their preview.
        var media = FindMedia(windows.FirstOrDefault());
        var mediaCards = new List<(Grid Row, ApplicationWindow Window)>();

        // Show at most 8 windows side by side
        foreach (var window in windows.Take(8))
        {
            var w = window;

            var card = new Border
            {
                Width = CardWidth,
                Height = media is null ? CardHeight : CardHeight + MediaRowHeight,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(4),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };

            var cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CardHeight - 28) });
            if (media is not null)
            {
                cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(MediaRowHeight) });
                var mediaRow = new Grid { Visibility = Visibility.Collapsed };
                Grid.SetRow(mediaRow, 2);
                cardGrid.Children.Add(mediaRow);
                mediaCards.Add((mediaRow, w));
            }

            // 1. Header row (Icon + Title + Close button)
            var headerGrid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });

            var iconImg = new Image
            {
                Width = 16,
                Height = 16,
                Source = w.Icon ?? ShellIcons.GetWindowIcon(w.Handle),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            Grid.SetColumn(iconImg, 0);

            var titleText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(w.Title) ? L.T("Window") : w.Title,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0),
            };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            Grid.SetColumn(titleText, 1);

            // Close button (✕)
            var closeBtn = new Button
            {
                Content = "\uE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 9,
                Width = 20,
                Height = 20,
                Style = Application.Current.TryFindResource("DockButton") as Style,
                ToolTip = L.T("Close"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0),
            };
            closeBtn.Click += (s, e) =>
            {
                e.Handled = true;
                CloseWindowFromPreview(w);
            };
            Grid.SetColumn(closeBtn, 2);

            headerGrid.Children.Add(iconImg);
            headerGrid.Children.Add(titleText);
            headerGrid.Children.Add(closeBtn);
            Grid.SetRow(headerGrid, 0);

            // 2. Live Preview Host (DWM Thumbnail Host)
            var thumbHost = new Border
            {
                Width = ThumbWidth,
                Height = ThumbHeight,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(4, 0, 4, 4),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(thumbHost, 1);

            cardGrid.Children.Add(headerGrid);
            cardGrid.Children.Add(thumbHost);
            card.Child = cardGrid;

            // Hover effect
            card.MouseEnter += (_, _) =>
            {
                card.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");
                StartPeek(w);
            };
            card.MouseLeave += (_, _) =>
            {
                card.Background = Brushes.Transparent;
                _peekTimer.Stop();
                _peekTarget = null;
                EndPeek();
            };

            // Bring window to front on click
            card.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                EndPeek();
                if (w.IsMinimized) w.Restore();
                w.BringToFront();
                HidePreview();
            };

            // Middle click closes the window, like the Windows taskbar. Releasing outside the card cancels.
            card.MouseDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                e.Handled = true;
                card.CaptureMouse();
            };
            card.MouseUp += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                e.Handled = true;
                bool captured = card.IsMouseCaptured;
                card.ReleaseMouseCapture();
                var position = e.GetPosition(card);
                bool inside = position.X >= 0 && position.Y >= 0 && position.X <= card.ActualWidth && position.Y <= card.ActualHeight;
                if (captured && inside) CloseWindowFromPreview(w);
            };
            card.ToolTip = L.T("Click to switch · Middle-click to close · Scroll to cycle windows");

            _previewItems.Add((thumbHost, w));
            _cardsPanel.Children.Add(card);
        }

        if (media is not null && mediaCards.Count > 0)
            _ = ShowMediaButtonsAsync(media, mediaCards, version);
    }
}
