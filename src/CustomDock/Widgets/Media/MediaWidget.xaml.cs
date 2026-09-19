using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

public sealed class MediaSettings : ObservableObject
{
    private bool _hideWhenIdle;

    /// <summary>Hides widget from the dock when there is no active media session.</summary>
    public bool HideWhenIdle { get => _hideWhenIdle; set => Set(ref _hideWhenIdle, value); }
}

/// <summary>Now playing media and playback controls via Windows SMTC.</summary>
public partial class MediaWidget : WidgetBase
{
    private MediaSettings _settings = new();
    private bool _tickSubscribed;
    private bool _isDraggingSeek;
    private bool _updatingVolumeSlider;

    public MediaWidget()
    {
        InitializeComponent();
        FullTrack.SizeChanged += (_, _) => UpdateProgress();
        MediaPopup.Closed += (_, _) => UpdateTickSubscription();
    }

    private static MediaService Media => AppServices.Media;

    protected override async void OnAttached()
    {
        _settings = GetSettings<MediaSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        Media.Changed += Render;
        AppServices.Audio.VolumeChanged += OnVolumeChanged;
        await Media.EnsureStartedAsync();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        Media.Changed -= Render;
        AppServices.Audio.VolumeChanged -= OnVolumeChanged;
        SetTickSubscription(false);
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_full, Layout_compact, Layout_mini);
        CardPadding = Variant == "mini" ? new Thickness(7, 0, 7, 0) : new Thickness(8, 0, 10, 0);
        Render();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void OnVolumeChanged(object? sender, EventArgs e)
    {
        if (MediaPopup.IsOpen && !_updatingVolumeSlider)
        {
            _updatingVolumeSlider = true;
            try
            {
                PopupVolumeSlider.Value = AppServices.Audio.VolumePercent;
            }
            finally
            {
                _updatingVolumeSlider = false;
            }
        }
    }

    private void Render()
    {
        var state = Media.Current;
        Visibility = _settings.HideWhenIdle && !state.HasSession && !IsPreview ? Visibility.Collapsed : Visibility.Visible;

        string title = !state.HasSession ? "Nothing playing" : string.IsNullOrWhiteSpace(state.Title) ? "Unknown track" : state.Title;
        string artist = !state.HasSession ? "Start a media player" : string.IsNullOrWhiteSpace(state.Artist) ? state.SourceApp : state.Artist;
        FullTitle.Text = CompactTitle.Text = title;
        FullArtist.Text = CompactArtist.Text = artist;

        ToolTip = !state.HasSession
            ? "Play media in Spotify, YouTube Music, or any SMTC-supported player."
            : string.Join("\n", new[]
            {
                state.Title,
                state.Artist,
                string.IsNullOrWhiteSpace(state.Album) ? null : $"Album: {state.Album}",
                string.IsNullOrWhiteSpace(state.SourceApp) ? null : $"Source: {state.SourceApp}",
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
        UpdateTickSubscription();
        UpdateProgress();
        RefreshCompact();

        if (MediaPopup.IsOpen)
            RenderPopup();
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

    public override bool OnCompactClick()
    {
        ToggleMediaPopup();
        return true;
    }

    private void OnWidgetMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DockDragHelper.JustDragged) return;
        if (e.OriginalSource is DependencyObject d && FindAncestor<Button>(d) is not null)
            return;

        ToggleMediaPopup();
        e.Handled = true;
    }

    private void ToggleMediaPopup()
    {
        if (MediaPopup.IsOpen)
        {
            MediaPopup.IsOpen = false;
            return;
        }

        RenderPopup();
        OpenPopup(MediaPopup);
        UpdateTickSubscription();
    }

    private void RenderPopup()
    {
        var state = Media.Current;
        string title = !state.HasSession ? "Nothing playing" : string.IsNullOrWhiteSpace(state.Title) ? "Unknown track" : state.Title;
        string artist = !state.HasSession ? "Start a media player" : string.IsNullOrWhiteSpace(state.Artist) ? state.SourceApp : state.Artist;

        PopupTitle.Text = title;
        PopupArtist.Text = artist;

        if (state.Thumbnail is not null)
        {
            PopupArtImage.Source = state.Thumbnail;
            PopupArtImage.Visibility = Visibility.Visible;
            PopupArtGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            PopupArtImage.Source = null;
            PopupArtImage.Visibility = Visibility.Collapsed;
            PopupArtGlyph.Visibility = Visibility.Visible;
        }

        string playGlyph = state.IsPlaying ? "\uE769" : "\uE768";
        PopupPlay.Content = playGlyph;
        bool canPlay = state.HasSession && state.CanPlayPause;
        PopupPlay.IsEnabled = canPlay;
        PopupPrev.IsEnabled = state.HasSession && state.CanPrevious;
        PopupNext.IsEnabled = state.HasSession && state.CanNext;

        bool hasTimeline = state.HasSession && state.Duration > TimeSpan.FromSeconds(1);
        PopupDuration.Text = hasTimeline ? TimerFormat.Format(state.Duration) : "0:00";
        if (!_isDraggingSeek)
        {
            var position = state.EstimatedPosition;
            PopupPosition.Text = hasTimeline ? TimerFormat.Format(position) : "0:00";
            PopupSeekSlider.Maximum = hasTimeline ? state.Duration.TotalSeconds : 100;
            PopupSeekSlider.Value = hasTimeline ? Math.Clamp(position.TotalSeconds, 0, state.Duration.TotalSeconds) : 0;
            PopupSeekSlider.IsEnabled = hasTimeline;
        }

        _updatingVolumeSlider = true;
        try
        {
            PopupVolumeSlider.Value = AppServices.Audio.VolumePercent;
        }
        finally
        {
            _updatingVolumeSlider = false;
        }

        string footer = "";
        if (!string.IsNullOrWhiteSpace(state.Album))
            footer = state.Album;
        if (!string.IsNullOrWhiteSpace(state.SourceApp))
            footer = string.IsNullOrEmpty(footer) ? state.SourceApp : $"{footer} · {state.SourceApp}";
        PopupFooter.Text = footer;
        PopupFooter.Visibility = string.IsNullOrWhiteSpace(footer) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSeekSliderMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSeek = true;
    }

    private async void OnSeekSliderMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSeek = false;
        if (Media.Current.HasSession && Media.Current.Duration > TimeSpan.Zero)
        {
            var target = TimeSpan.FromSeconds(PopupSeekSlider.Value);
            await Media.SeekAsync(target);
        }
    }

    private void OnPopupVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVolumeSlider) return;
        AppServices.Audio.VolumePercent = (int)Math.Round(e.NewValue);
    }

    private void OnPopupCloseClick(object sender, RoutedEventArgs e)
    {
        MediaPopup.IsOpen = false;
    }

    private void OnPopupMixerClick(object sender, RoutedEventArgs e)
    {
        AppLauncher.Launch("ms-settings:sound", newInstance: false);
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
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

    private void UpdateTickSubscription()
    {
        var state = Media.Current;
        bool hasTimeline = state.HasSession && state.Duration > TimeSpan.FromSeconds(1);
        bool shouldTick = hasTimeline && (state.IsPlaying || MediaPopup.IsOpen) && (Variant == "full" || MediaPopup.IsOpen);
        SetTickSubscription(shouldTick);
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
            if (MediaPopup.IsOpen && !_isDraggingSeek)
            {
                PopupPosition.Text = "0:00";
                PopupSeekSlider.Value = 0;
            }
            return;
        }
        var position = state.EstimatedPosition;
        string posText = TimerFormat.Format(position);
        FullPosition.Text = posText;
        double fraction = Math.Clamp(position.TotalSeconds / state.Duration.TotalSeconds, 0, 1);
        FullFill.Width = FullTrack.ActualWidth * fraction;

        if (MediaPopup.IsOpen && !_isDraggingSeek)
        {
            PopupPosition.Text = posText;
            PopupSeekSlider.Maximum = state.Duration.TotalSeconds;
            PopupSeekSlider.Value = position.TotalSeconds;
        }
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e) => await Media.PlayPauseAsync();

    private async void OnNextClick(object sender, RoutedEventArgs e) => await Media.NextAsync();

    private async void OnPreviousClick(object sender, RoutedEventArgs e) => await Media.PreviousAsync();

    public override void AddContextMenuItems(ItemCollection items)
    {
        var state = Media.Current;
        items.Add(DockMenu.Item(state.IsPlaying ? "Pause" : "Play", state.IsPlaying ? "\uE769" : "\uE768",
            () => _ = Media.PlayPauseAsync(), state.HasSession));
        items.Add(DockMenu.Item("Next", "\uE893", () => _ = Media.NextAsync(), state.CanNext));
        items.Add(DockMenu.Item("Previous", "\uE892", () => _ = Media.PreviousAsync(), state.CanPrevious));
        items.Add(DockMenu.Check("Hide when idle", _settings.HideWhenIdle, () => _settings.HideWhenIdle = !_settings.HideWhenIdle));
    }
}
