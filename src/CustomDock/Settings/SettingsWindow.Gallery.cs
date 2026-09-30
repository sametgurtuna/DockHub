using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Widgets;
using CustomDock.Widgets.Web;

namespace CustomDock.Settings;

/// <summary>Mock dock host environment for gallery previews.</summary>
internal sealed class PreviewHost : IWidgetHost
{
    public PreviewHost(Window window) => Window = window;
    public DockEdge Edge => DockEdge.Bottom;
    public bool IsVertical => false;
    public Window Window { get; }
    public bool IsPreview => true;
    public void BeginInteraction() { }
    public void EndInteraction() { }
    public void ActivateForInput() { }
}

/// <summary>
/// Settings › Widget gallery: one card per widget in category sections, a search box and category chips. Only the cards
/// in view run a live preview.
/// </summary>
public partial class SettingsWindow
{
    /// <summary>Cards this far outside the visible area already start their preview, so scrolling doesn't show icons.</summary>
    private const double PreviewMargin = 160;

    private sealed record GallerySection(string Category, FrameworkElement Header, WrapPanel Cards);

    private readonly List<GalleryCard> _galleryCards = new();
    private readonly List<GallerySection> _gallerySections = new();
    private string? _galleryCategory;
    private bool _galleryPreviewsQueued;
    private bool _galleryScrollHooked;
    private int _livePreviews = -1;
    private DispatcherTimer? _gallerySearchTimer;

    private FrameworkElement CreatePreviewCard(WidgetBase widget)
    {
        var card = new WidgetCard { Content = widget, HoverEnabled = false, Margin = new Thickness(0) };
        void Apply()
        {
            if (widget.CardBackground is { } background) card.Background = background;
            else card.SetResourceReference(BackgroundProperty, "CardBrush");
            card.Padding = widget.CardPadding;
        }
        widget.CardAppearanceChanged += Apply;
        Apply();
        return card;
    }

    private void BuildGallery()
    {
        ReleaseGalleryPreviews();
        _galleryCards.Clear();
        _gallerySections.Clear();
        GalleryPanel.Children.Clear();

        foreach (var category in WidgetCategories.Ordered)
        {
            var descriptors = WidgetRegistry.All.Where(d => d.Category == category).ToList();
            if (descriptors.Count == 0) continue;
            var section = AddGallerySection(category, L.T(category), null);
            foreach (var descriptor in descriptors) AddGalleryCard(section, new GalleryCard(descriptor, _previewHost, AddFromGallery));
        }
        AddGallerySection(GalleryFilter.Community, L.T("Community web widgets"),
            L.T("From DockHub's widget list on GitHub, updated daily. Anyone can add a widget to it with a pull request."));

        if (!_galleryScrollHooked)
        {
            _galleryScrollHooked = true;
            PageScroller.ScrollChanged += (_, _) => QueueGalleryPreviews();
        }
        BuildGalleryChips();
        ApplyGalleryFilter();
    }

    private GallerySection AddGallerySection(string category, string title, string? description)
    {
        var header = new StackPanel { Margin = new Thickness(2, 18, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = (FontFamily)FindResource("DisplayFont"),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
        });
        if (description is not null)
        {
            var text = new TextBlock { Text = description };
            text.SetResourceReference(StyleProperty, "SettingDescription");
            header.Children.Add(text);
        }
        var cards = new WrapPanel();
        GalleryPanel.Children.Add(header);
        GalleryPanel.Children.Add(cards);
        var section = new GallerySection(category, header, cards);
        _gallerySections.Add(section);
        return section;
    }

    private void AddGalleryCard(GallerySection section, GalleryCard card)
    {
        card.NavigationRequested += NavigateGallery;
        _galleryCards.Add(card);
        section.Cards.Children.Add(card);
    }

    private static void AddFromGallery(WidgetDescriptor descriptor, string? variant)
        => AppServices.ConfigService.AddItem(DockItem.ForWidget(descriptor.Id, variant));

    /// <summary>The community list's cards (replaced when a fresh list arrives).</summary>
    private void ShowCommunityCards(IReadOnlyList<WidgetIndexEntry> entries)
    {
        if (_gallerySections.FirstOrDefault(s => s.Category == GalleryFilter.Community) is not { } section) return;
        foreach (var old in section.Cards.Children.OfType<GalleryCard>()) _galleryCards.Remove(old);
        section.Cards.Children.Clear();
        foreach (var entry in entries)
        {
            bool installed = WebWidgetCatalog.Installed.Any(m => m.Id == entry.Id);
            AddGalleryCard(section, new GalleryCard(entry, installed, button => DownloadAndInstallAsync(entry.Links, button, null)));
        }
        BuildGalleryChips();
        ApplyGalleryFilter();
    }

    // ------------------------------------------------------------------ Search and categories

    private void BuildGalleryChips()
    {
        GalleryChips.Children.Clear();
        var categories = new List<(string? Key, string Name)> { (null, L.T("All widgets")) };
        foreach (var section in _gallerySections)
        {
            // The community list may still be on its way (or offline): its chip stays, and says so when empty.
            bool community = section.Category == GalleryFilter.Community;
            if (!community && !section.Cards.Children.OfType<GalleryCard>().Any()) continue;
            categories.Add((section.Category, community ? L.T("Community widgets") : L.T(section.Category)));
        }
        if (_galleryCategory is not null && categories.All(c => c.Key != _galleryCategory)) _galleryCategory = null;

        foreach (var (key, name) in categories)
        {
            var chip = new RadioButton { Content = name, GroupName = "GalleryCategory", IsChecked = key == _galleryCategory };
            chip.SetResourceReference(StyleProperty, "GalleryChip");
            chip.Checked += (_, _) =>
            {
                _galleryCategory = key;
                ApplyGalleryFilter();
            };
            GalleryChips.Children.Add(chip);
        }
    }

    private void OnGallerySearchChanged(object sender, TextChangedEventArgs e)
    {
        GallerySearchHint.Visibility = GallerySearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        // Typing quickly filters once, after a short pause.
        _gallerySearchTimer ??= CreateGallerySearchTimer();
        _gallerySearchTimer.Stop();
        _gallerySearchTimer.Start();
    }

    private DispatcherTimer CreateGallerySearchTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ApplyGalleryFilter();
        };
        return timer;
    }

    private void OnGallerySearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && GallerySearchBox.Text.Length > 0)
        {
            GallerySearchBox.Clear();
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Down)
        {
            // The best match gets the focus; Enter on it adds it.
            _gallerySearchTimer?.Stop();
            ApplyGalleryFilter();
            if (GalleryResults().FirstOrDefault() is { } best)
            {
                best.BringIntoView();
                best.Focus();
                e.Handled = true;
            }
        }
    }

    /// <summary>Cards matching the search and category, best matches first.</summary>
    private List<GalleryCard> GalleryResults()
    {
        var byEntry = new Dictionary<GalleryEntry, GalleryCard>(ReferenceEqualityComparer.Instance);
        foreach (var card in _galleryCards) byEntry[card.Entry] = card;
        return GalleryFilter.Apply(_galleryCards.Select(c => c.Entry), GallerySearchBox.Text, _galleryCategory)
            .Select(entry => byEntry[entry])
            .ToList();
    }

    private void ApplyGalleryFilter()
    {
        var shown = new HashSet<GalleryCard>(GalleryResults());
        foreach (var card in _galleryCards)
            card.Visibility = shown.Contains(card) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var section in _gallerySections)
        {
            bool any = section.Cards.Children.OfType<GalleryCard>().Any(shown.Contains);
            section.Header.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            section.Cards.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }

        string query = GallerySearchBox.Text.Trim();
        GalleryEmpty.Text = query.Length > 0 ? L.T("No widget matches “{0}”.", query) : L.T("Nothing here yet.");
        GalleryEmpty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueGalleryPreviews();
    }

    /// <summary>Arrow keys between cards: left and right within a row, up and down to the nearest card of the next row.</summary>
    private void NavigateGallery(GalleryCard from, FocusNavigationDirection direction)
    {
        var cards = _galleryCards.Where(c => c.IsVisible).ToList();
        int index = cards.IndexOf(from);
        if (index < 0) return;
        GalleryCard? target = null;
        if (direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
        {
            int next = index + (direction == FocusNavigationDirection.Right ? 1 : -1);
            if (next >= 0 && next < cards.Count) target = cards[next];
        }
        else
        {
            Point Origin(GalleryCard card) => card.TranslatePoint(new Point(0, 0), GalleryPanel);
            var start = Origin(from);
            bool down = direction == FocusNavigationDirection.Down;
            target = cards
                .Select(card => (Card: card, At: Origin(card)))
                .Where(c => down ? c.At.Y > start.Y + 1 : c.At.Y < start.Y - 1)
                .OrderBy(c => Math.Abs(c.At.Y - start.Y))
                .ThenBy(c => Math.Abs(c.At.X - start.X))
                .Select(c => c.Card)
                .FirstOrDefault();
        }
        if (target is null) return;
        target.BringIntoView();
        target.Focus();
    }

    /// <summary>
    /// Settings search: shows the widget's card (search and category cleared) and outlines it. The focus stays in the
    /// search results, which can still be browsed with the arrow keys.
    /// </summary>
    private void ShowGalleryCard(string widgetId)
    {
        NavigateTo("gallery");
        if (GallerySearchBox.Text.Length > 0) GallerySearchBox.Clear();
        _gallerySearchTimer?.Stop();
        if (GalleryChips.Children.OfType<RadioButton>().FirstOrDefault() is { } all) all.IsChecked = true;
        _galleryCategory = null;
        ApplyGalleryFilter();
        if (_galleryCards.FirstOrDefault(c => string.Equals(c.Entry.Id, widgetId, StringComparison.OrdinalIgnoreCase)) is not { } card) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            card.BringIntoView();
            card.Flash();
        });
    }

    // ------------------------------------------------------------------ Live previews

    private void QueueGalleryPreviews()
    {
        if (_galleryPreviewsQueued || _galleryCards.Count == 0) return;
        _galleryPreviewsQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _galleryPreviewsQueued = false;
            UpdateGalleryPreviews();
        });
    }

    /// <summary>Starts the previews of the cards in (or near) view and stops all others.</summary>
    private void UpdateGalleryPreviews()
    {
        bool pageShown = GalleryPage.IsVisible && WindowState != WindowState.Minimized;
        var viewport = new Rect(0, -PreviewMargin, PageScroller.ViewportWidth, PageScroller.ViewportHeight + 2 * PreviewMargin);
        int live = 0;
        foreach (var card in _galleryCards.Where(c => c.HasStage))
        {
            if (pageShown && card.IsVisible && InView(card, viewport)) card.ShowPreview();
            else card.HidePreview();
            if (card.IsPreviewShown) live++;
        }
        if (live == _livePreviews) return;
        _livePreviews = live;
        Log.Debug($"Widget gallery: {live} live preview(s) of {_galleryCards.Count(c => c.HasStage)} widgets");
    }

    private bool InView(FrameworkElement card, Rect viewport)
    {
        try
        {
            return card.TransformToAncestor(PageScroller).TransformBounds(new Rect(card.RenderSize)).IntersectsWith(viewport);
        }
        catch (InvalidOperationException)
        {
            return false; // not in the page's tree (yet)
        }
    }

    private void ReleaseGalleryPreviews()
    {
        foreach (var card in _galleryCards) card.HidePreview();
        _livePreviews = -1;
    }

    /// <summary>The "on the dock ×N" badges follow the dock.</summary>
    private void RefreshGalleryBadges()
    {
        foreach (var card in _galleryCards) card.UpdateBadges();
    }
}
