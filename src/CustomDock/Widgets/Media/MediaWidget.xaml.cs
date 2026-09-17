using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

public sealed class MediaSettings : ObservableObject
{
    private bool _hideWhenIdle;

    /// <summary>Hiçbir medya oturumu yokken widget'ı dock'tan gizler.</summary>
    public bool HideWhenIdle { get => _hideWhenIdle; set => Set(ref _hideWhenIdle, value); }
}

/// <summary>Windows SMTC üzerinden şu an çalan medya ve oynatma kontrolleri.</summary>
public partial class MediaWidget : WidgetBase
{
    private MediaSettings _settings = new();
    private bool _tickSubscribed;

    public MediaWidget()
    {
        InitializeComponent();
        FullTrack.SizeChanged += (_, _) => UpdateProgress();
    }

    private static MediaService Media => AppServices.Media;

    protected override async void OnAttached()
    {
        _settings = GetSettings<MediaSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        Media.Changed += Render;
        await Media.EnsureStartedAsync();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        Media.Changed -= Render;
        SetTickSubscription(false);
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_full, Layout_compact, Layout_mini);
        CardPadding = Variant == "mini" ? new Thickness(7, 0, 7, 0) : new Thickness(8, 0, 10, 0);
        Render();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void Render()
    {
        var state = Media.Current;
        Visibility = _settings.HideWhenIdle && !state.HasSession && !IsPreview ? Visibility.Collapsed : Visibility.Visible;

        string title = !state.HasSession ? "Şu an çalan yok" : string.IsNullOrWhiteSpace(state.Title) ? "Bilinmeyen parça" : state.Title;
        string artist = !state.HasSession ? "Bir oynatıcı başlatın" : string.IsNullOrWhiteSpace(state.Artist) ? state.SourceApp : state.Artist;
        FullTitle.Text = CompactTitle.Text = title;
        FullArtist.Text = CompactArtist.Text = artist;

        ToolTip = !state.HasSession
            ? "Spotify, YouTube Music veya SMTC destekleyen bir oynatıcıda müzik başlatın."
            : string.Join("\n", new[]
            {
                state.Title,
                state.Artist,
                string.IsNullOrWhiteSpace(state.Album) ? null : $"Albüm: {state.Album}",
                string.IsNullOrWhiteSpace(state.SourceApp) ? null : $"Kaynak: {state.SourceApp}",
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

        SetArt(FullArt, FullArtGlyph, state.Thumbnail);
        SetArt(CompactArt, CompactArtGlyph, state.Thumbnail);

        string playGlyph = state.IsPlaying ? "\uE769" : "\uE768";
        FullPlay.Content = CompactPlay.Content = Layout_mini.Content = playGlyph;
        bool canPlay = state.HasSession && state.CanPlayPause;
        FullPlay.IsEnabled = CompactPlay.IsEnabled = Layout_mini.IsEnabled = canPlay;
        FullPrev.IsEnabled = state.HasSession && state.CanPrevious;
        FullNext.IsEnabled = state.HasSession && state.CanNext;

        bool hasTimeline = state.HasSession && state.Duration > TimeSpan.FromSeconds(1);
        FullDuration.Text = hasTimeline ? TimerFormat.Format(state.Duration) : "";
        FullPosition.Text = hasTimeline ? "0:00" : "";
        SetTickSubscription(hasTimeline && state.IsPlaying && Variant == "full");
        UpdateProgress();
        RefreshCompact();
    }

    private Border? _compactArt;

    protected override void UpdateCompact(CompactTile tile)
    {
        var state = Media.Current;
        if (state.Thumbnail is not null)
        {
            _compactArt ??= new Border { CornerRadius = new CornerRadius(6) };
            _compactArt.Background = new ImageBrush(state.Thumbnail) { Stretch = Stretch.UniformToFill };
            tile.SetVisual(_compactArt);
        }
        else
        {
            tile.ShowGlyph(Descriptor.Icon, state.IsPlaying ? Descriptor.AccentKey : "TextSecondaryBrush");
        }
        tile.Text = null;
    }

    private static void SetArt(Border border, TextBlock glyph, ImageSource? thumbnail)
    {
        if (thumbnail is not null)
        {
            border.Background = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill };
            glyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            border.SetResourceReference(Border.BackgroundProperty, "SubtleFillBrush");
            glyph.Visibility = Visibility.Visible;
        }
    }

    private void SetTickSubscription(bool subscribe)
    {
        if (subscribe == _tickSubscribed) return;
        _tickSubscribed = subscribe;
        if (subscribe) AppServices.Clock.SecondTick += OnSecondTick;
        else AppServices.Clock.SecondTick -= OnSecondTick;
    }

    private void OnSecondTick(object? sender, DateTime e) => UpdateProgress();

    private void UpdateProgress()
    {
        var state = Media.Current;
        if (!state.HasSession || state.Duration <= TimeSpan.Zero)
        {
            FullFill.Width = 0;
            return;
        }
        var position = state.EstimatedPosition;
        FullPosition.Text = TimerFormat.Format(position);
        double fraction = Math.Clamp(position.TotalSeconds / state.Duration.TotalSeconds, 0, 1);
        FullFill.Width = FullTrack.ActualWidth * fraction;
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e) => await Media.PlayPauseAsync();

    private async void OnNextClick(object sender, RoutedEventArgs e) => await Media.NextAsync();

    private async void OnPreviousClick(object sender, RoutedEventArgs e) => await Media.PreviousAsync();

    public override void AddContextMenuItems(ItemCollection items)
    {
        var state = Media.Current;
        items.Add(DockMenu.Item(state.IsPlaying ? "Duraklat" : "Oynat", state.IsPlaying ? "\uE769" : "\uE768",
            () => _ = Media.PlayPauseAsync(), state.HasSession));
        items.Add(DockMenu.Item("Sonraki", "\uE893", () => _ = Media.NextAsync(), state.CanNext));
        items.Add(DockMenu.Item("Önceki", "\uE892", () => _ = Media.PreviousAsync(), state.CanPrevious));
        items.Add(DockMenu.Check("Çalan yokken gizle", _settings.HideWhenIdle, () => _settings.HideWhenIdle = !_settings.HideWhenIdle));
    }
}
