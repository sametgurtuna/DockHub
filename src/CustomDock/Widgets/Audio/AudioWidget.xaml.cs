using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

public partial class AudioWidget : WidgetBase
{
    private const string HeadphoneIconPath = "M12,3 A9,9 0 0 0 3,12 V18 A3,3 0 0 0 6,21 H7 A2,2 0 0 0 9,19 V15 A2,2 0 0 0 7,13 H5 V12 A7,7 0 0 1 19,12 V13 H17 A2,2 0 0 0 15,15 V19 A2,2 0 0 0 17,21 H18 A3,3 0 0 0 21,18 V12 A9,9 0 0 0 12,3 Z";
    private const string SpeakerIconPath = "M11,4 L6,8 H3 A1,1 0 0 0 2,9 V15 A1,1 0 0 0 3,16 H6 L11,20 A1,1 0 0 0 12.5,19.2 V4.8 A1,1 0 0 0 11,4 Z M16,8 A5,5 0 0 1 16,16 M19,5 A9,9 0 0 1 19,19";
    private const string MuteIconPath = "M11,4 L6,8 H3 A1,1 0 0 0 2,9 V15 A1,1 0 0 0 3,16 H6 L11,20 A1,1 0 0 0 12.5,19.2 V4.8 A1,1 0 0 0 11,4 Z M16,10 L20,14 M20,10 L16,14";

    private static readonly Geometry HeadphoneGeometry = Geometry.Parse(HeadphoneIconPath);
    private static readonly Geometry SpeakerGeometry = Geometry.Parse(SpeakerIconPath);
    private static readonly Geometry MuteGeometry = Geometry.Parse(MuteIconPath);

    private DateTime _mixerClosedAt = DateTime.MinValue;
    private ScrollViewer? _appsScroller;

    static AudioWidget()
    {
        HeadphoneGeometry.Freeze();
        SpeakerGeometry.Freeze();
        MuteGeometry.Freeze();
    }

    public AudioWidget()
    {
        InitializeComponent();
        MixerPopup.Closed += (_, _) => _mixerClosedAt = DateTime.UtcNow;
    }

    protected override void OnAttached()
    {
        AppServices.Audio.VolumeChanged += OnAudioChanged;
        AppServices.Audio.DevicesChanged += OnAudioChanged;
        Render();
    }

    protected override void OnDetached()
    {
        AppServices.Audio.VolumeChanged -= OnAudioChanged;
        AppServices.Audio.DevicesChanged -= OnAudioChanged;
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_compact, Layout_slider);
        Render();
    }

    private void OnAudioChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        var dev = AppServices.Audio.DefaultDevice;
        bool muted = AppServices.Audio.IsMuted;
        int vol = AppServices.Audio.VolumePercent;
        string name = dev?.Name ?? "Audio Device";

        string shortName = name;
        int paren = shortName.IndexOf('(');
        if (paren > 3) shortName = shortName[..paren].Trim();

        Geometry icon = muted ? MuteGeometry : (dev?.IsHeadphone == true ? HeadphoneGeometry : SpeakerGeometry);
        string volText = muted ? "Muted" : $"{vol}%";

        CompactIcon.Data = icon;
        CompactDeviceName.Text = shortName;
        CompactVolumeText.Text = volText;

        SliderIcon.Data = icon;
        SliderDeviceName.Text = shortName;
        SliderVolumeText.Text = volText;

        if (Layout_slider.Visibility == Visibility.Visible && SliderBarTrack.ActualWidth > 0)
        {
            double targetWidth = muted ? 0 : SliderBarTrack.ActualWidth * (vol / 100.0);
            AnimateBar(SliderBar, targetWidth);
        }

        ToolTip = $"{name}\nVolume: {volText}";
        RefreshCompact();
    }

    private void AnimateBar(FrameworkElement bar, double width)
    {
        if (!IsVisible || double.IsNaN(bar.Width))
        {
            bar.Width = Math.Max(0, width);
            return;
        }
        bar.BeginAnimation(WidthProperty, new DoubleAnimation(Math.Max(0, width), TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    // ------------------------------------------------------------------ Interaction

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (MixerPopup.IsOpen)
        {
            // While mixer window is open, mouse wheel scrolls mixer content, doesn't change dock volume
            return;
        }
        float step = (float)Math.Round(e.Delta / 120.0 * 0.02, 3);
        if (Math.Abs(step) < 0.01f) step = e.Delta > 0 ? 0.01f : -0.01f;
        AppServices.Audio.StepVolume(step);
        e.Handled = true;
    }

    private void OnMixerBorderPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_appsScroller is not null)
        {
            _appsScroller.ScrollToVerticalOffset(_appsScroller.VerticalOffset - (e.Delta / 2.0));
        }
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (DockDragHelper.JustDragged) return;
        ToggleMixer();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle)
        {
            AppServices.Audio.ToggleMute();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ Audio Mixer Popup (EarTrumpet style)

    private void ToggleMixer()
    {
        if (MixerPopup.IsOpen || Dock.PopupAnimationHelper.IsClosing(MixerPopup))
        {
            ClosePopup(MixerPopup);
            return;
        }
        // Prevent reopen if newly closed by click (toggle feel)
        if (DateTime.UtcNow - _mixerClosedAt < TimeSpan.FromMilliseconds(250))
            return;

        BuildMixerUI();
        OpenPopup(MixerPopup);
    }


    private void BuildMixerUI()
    {
        MixerPanel.Children.Clear();

        var dev = AppServices.Audio.DefaultDevice;
        bool masterMuted = AppServices.Audio.IsMuted;
        int masterVol = AppServices.Audio.VolumePercent;
        string devName = dev?.Name ?? "Master Audio Output";

        // 1. Header Bar
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        var titleText = new TextBlock
        {
            Text = "Volume Mixer",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleText.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        headerGrid.Children.Add(titleText);
        MixerPanel.Children.Add(headerGrid);

        // 2. Master Device Card (Master Volume Card)
        var masterCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 10),
            Margin = new Thickness(0, 0, 0, 10),
        };
        masterCard.SetResourceReference(Border.BackgroundProperty, "SurfaceLightBrush");

        var masterStack = new StackPanel();

        // Master device top row: Icon + Name + Percent
        var masterTop = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        masterTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        masterTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        masterTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var masterMuteBtn = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
        };
        masterMuteBtn.SetResourceReference(Border.BackgroundProperty, masterMuted ? "SubtleFillHoverBrush" : "Transparent");
        var masterMuteIcon = new TextBlock
        {
            Text = masterMuted ? "\uE74F" : "\uE767",
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        masterMuteIcon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        masterMuteIcon.SetResourceReference(TextBlock.ForegroundProperty, masterMuted ? "AccentRedBrush" : "AccentCyanBrush");
        masterMuteBtn.Child = masterMuteIcon;
        masterMuteBtn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            AppServices.Audio.ToggleMute();
            BuildMixerUI();
        };
        Grid.SetColumn(masterMuteBtn, 0);
        masterTop.Children.Add(masterMuteBtn);

        var devLabel = new TextBlock
        {
            Text = devName,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        devLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        devLabel.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        Grid.SetColumn(devLabel, 1);
        masterTop.Children.Add(devLabel);

        var masterVolLabel = new TextBlock
        {
            Text = masterMuted ? "Muted" : $"{masterVol}%",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        masterVolLabel.SetResourceReference(TextBlock.ForegroundProperty, masterMuted ? "AccentRedBrush" : "AccentCyanBrush");
        Grid.SetColumn(masterVolLabel, 2);
        masterTop.Children.Add(masterVolLabel);

        masterStack.Children.Add(masterTop);

        // Master device bottom row: Master Slider
        var masterSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = masterVol,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center,
            IsMoveToPointEnabled = true,
        };
        masterSlider.ValueChanged += (_, e) =>
        {
            AppServices.Audio.Volume = (float)(e.NewValue / 100.0);
            masterVolLabel.Text = $"{Math.Round(e.NewValue)}%";
        };
        masterStack.Children.Add(masterSlider);
        masterCard.Child = masterStack;
        MixerPanel.Children.Add(masterCard);

        // 3. Apps Header
        var sessions = AppServices.Audio.GetAudioSessions();
        var appsHeader = new Grid { Margin = new Thickness(2, 2, 2, 6) };
        appsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        appsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var appsLabel = new TextBlock
        {
            Text = "App Volume",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
        };
        appsLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Grid.SetColumn(appsLabel, 0);
        appsHeader.Children.Add(appsLabel);

        if (sessions.Count > 0)
        {
            var countBadge = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
            };
            countBadge.SetResourceReference(Border.BackgroundProperty, "SurfaceLightBrush");
            var countText = new TextBlock
            {
                Text = sessions.Count.ToString(),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
            };
            countText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            countBadge.Child = countText;
            Grid.SetColumn(countBadge, 1);
            appsHeader.Children.Add(countBadge);
        }
        MixerPanel.Children.Add(appsHeader);

        // 4. App Audio Sessions List
        if (sessions.Count == 0)
        {
            _appsScroller = null;
            var emptyBorder = new Border
            {
                Padding = new Thickness(10, 14, 10, 14),
                CornerRadius = new CornerRadius(8),
            };
            emptyBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceLightBrush");
            var emptyText = new TextBlock
            {
                Text = "No apps currently playing audio",
                FontSize = 11.5,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            emptyText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            emptyBorder.Child = emptyText;
            MixerPanel.Children.Add(emptyBorder);
        }
        else
        {
            var scroller = new ScrollViewer
            {
                MaxHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            _appsScroller = scroller;
            var appsList = new StackPanel();
            foreach (var session in sessions)
            {
                appsList.Children.Add(CreateAppSessionRow(session));
            }
            scroller.Content = appsList;
            MixerPanel.Children.Add(scroller);
        }
    }

    private FrameworkElement CreateAppSessionRow(AudioService.AudioSessionInfo session)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 2, 0, 3),
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceLightBrush");

        var root = new StackPanel();

        // Top row: Badge + App Name + Mute Button + Percent
        var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });

        // Badge
        string displayName = session.DisplayName;
        int dotIndex = displayName.LastIndexOf('.');
        if (dotIndex > 0 && dotIndex < displayName.Length - 1)
            displayName = displayName[(dotIndex + 1)..];

        string initial = string.IsNullOrEmpty(displayName) ? "?" : displayName[..1].ToUpperInvariant();
        var badge = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
            Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        badge.SetResourceReference(Border.BackgroundProperty, session.IsSystemSounds ? "AccentBlueBrush" : "CardBorderBrush");

        var badgeText = new TextBlock
        {
            Text = session.IsSystemSounds ? "\uE7F5" : initial,
            FontSize = session.IsSystemSounds ? 11 : 10.5,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (session.IsSystemSounds)
            badgeText.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        badgeText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        badge.Child = badgeText;
        Grid.SetColumn(badge, 0);
        topGrid.Children.Add(badge);

        // App Name
        var nameBlock = new TextBlock
        {
            Text = displayName,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        nameBlock.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        Grid.SetColumn(nameBlock, 1);
        topGrid.Children.Add(nameBlock);

        // Percent Text
        var volText = new TextBlock
        {
            Text = session.IsMuted ? "Muted" : $"{Math.Round(session.Volume * 100)}%",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        volText.SetResourceReference(TextBlock.ForegroundProperty, session.IsMuted ? "AccentRedBrush" : "TextSecondaryBrush");
        Grid.SetColumn(volText, 3);
        topGrid.Children.Add(volText);

        // Mute Icon Button
        var muteBtn = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var muteIcon = new TextBlock
        {
            Text = session.IsMuted ? "\uE74F" : "\uE767",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        muteIcon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        muteIcon.SetResourceReference(TextBlock.ForegroundProperty, session.IsMuted ? "AccentRedBrush" : "TextSecondaryBrush");
        muteBtn.Child = muteIcon;
        muteBtn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            bool newMuted = !session.IsMuted;
            AppServices.Audio.SetSessionMute(session, newMuted);
            muteIcon.Text = newMuted ? "\uE74F" : "\uE767";
            muteIcon.SetResourceReference(TextBlock.ForegroundProperty, newMuted ? "AccentRedBrush" : "TextSecondaryBrush");
            volText.Text = newMuted ? "Muted" : $"{Math.Round(session.Volume * 100)}%";
            volText.SetResourceReference(TextBlock.ForegroundProperty, newMuted ? "AccentRedBrush" : "TextSecondaryBrush");
        };
        Grid.SetColumn(muteBtn, 2);
        topGrid.Children.Add(muteBtn);

        root.Children.Add(topGrid);

        // Bottom row: Slider
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Round(session.Volume * 100),
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            IsMoveToPointEnabled = true,
            Tag = session,
        };
        slider.ValueChanged += (s, e) =>
        {
            if (s is Slider { Tag: AudioService.AudioSessionInfo sess })
            {
                AppServices.Audio.SetSessionVolume(sess, (float)(e.NewValue / 100.0));
                volText.Text = $"{Math.Round(e.NewValue)}%";
                volText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                muteIcon.Text = "\uE767";
                muteIcon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            }
        };
        root.Children.Add(slider);

        card.Child = root;
        return card;
    }

    // ------------------------------------------------------------------ Compact and Context menu

    protected override void UpdateCompact(CompactTile tile)
    {
        var dev = AppServices.Audio.DefaultDevice;
        bool muted = AppServices.Audio.IsMuted;
        Geometry icon = muted ? MuteGeometry : (dev?.IsHeadphone == true ? HeadphoneGeometry : SpeakerGeometry);
        tile.ShowGlyph(icon, muted ? "AccentRedBrush" : "AccentCyanBrush");
        tile.Text = muted ? "Muted" : $"{AppServices.Audio.VolumePercent}%";
    }

    public override bool OnCompactClick()
    {
        ToggleMixer();
        return true;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        var devices = AppServices.Audio.Devices;
        if (devices.Count > 0)
        {
            items.Add(DockMenu.Header("Output Device"));
            foreach (var dev in devices)
            {
                items.Add(DockMenu.Check(dev.Name, dev.IsDefault, () => AppServices.Audio.SetDefaultDevice(dev.Id)));
            }
            items.Add(DockMenu.Separator());
        }

        bool muted = AppServices.Audio.IsMuted;
        items.Add(DockMenu.Item(muted ? "Unmute" : "Mute", muted ? "\uE74F" : "\uE767", () => AppServices.Audio.ToggleMute()));
    }
}
