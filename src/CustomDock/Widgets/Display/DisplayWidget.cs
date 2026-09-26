using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;

namespace CustomDock.Widgets;

/// <summary>Screen brightness (mouse wheel or slider) and the night light state.</summary>
public sealed class DisplayWidget : WidgetBase
{
    public const string Icon = "M12,8 A4,4 0 1 1 11.99,8 Z M12,2 V4 M12,20 V22 M4.9,4.9 L6.3,6.3 M17.7,17.7 L19.1,19.1 M2,12 H4 M20,12 H22 M4.9,19.1 L6.3,17.7 M17.7,6.3 L19.1,4.9";
    private const string SunGlyph = "";
    private const string MoonGlyph = "";

    private readonly Border _iconLayout;
    private readonly TextBlock _iconGlyph;
    private readonly TextBlock _iconBadge;
    private readonly Grid _sliderLayout;
    private readonly Border _barFill;
    private readonly Grid _barTrack;
    private readonly TextBlock _barValue;
    private readonly TextBlock _nightDot;
    private readonly Popup _popup;
    private readonly Slider _slider;
    private readonly TextBlock _sliderValue;
    private readonly Button _nightButton;
    private readonly TextBlock _unsupported;
    private bool _updatingSlider;

    public DisplayWidget()
    {
        Cursor = Cursors.Hand;
        Background = System.Windows.Media.Brushes.Transparent;

        var (disc, glyph) = WidgetUi.IconDisc(SunGlyph, "AccentYellowBrush");
        _iconGlyph = glyph;
        _iconBadge = WidgetUi.Text("MicroText");
        _iconBadge.HorizontalAlignment = HorizontalAlignment.Center;
        _iconBadge.VerticalAlignment = VerticalAlignment.Bottom;
        _iconBadge.Margin = new Thickness(0, 0, 0, 4);
        _iconGlyph.Margin = new Thickness(0, 0, 0, 8);
        _iconLayout = new Border { Name = "Layout_icon", Child = new Grid { Children = { disc, _iconBadge } } };

        var bar = WidgetUi.Bar("", "AccentYellowBrush", "OrangeTrackBrush", 150);
        (_barFill, _barTrack, _barValue) = (bar.Fill, bar.Track, bar.Value);
        bar.Row.ColumnDefinitions[0].Width = new GridLength(36);
        var sun = WidgetUi.Glyph(SunGlyph, 14, "AccentYellowBrush");
        sun.HorizontalAlignment = HorizontalAlignment.Left;
        bar.Row.Children.Add(sun);
        _nightDot = WidgetUi.Glyph(MoonGlyph, 11, "AccentOrangeBrush");
        _nightDot.HorizontalAlignment = HorizontalAlignment.Right;
        _nightDot.Margin = new Thickness(0, 0, 4, 0);
        _nightDot.ToolTip = L.T("Night light is on");
        Grid.SetColumn(_nightDot, 0);
        bar.Row.Children.Add(_nightDot);
        _sliderLayout = new Grid { Name = "Layout_slider", VerticalAlignment = VerticalAlignment.Center, Children = { bar.Row } };

        (_popup, var content) = WidgetUi.PopupShell(300);
        _sliderValue = WidgetUi.Text("TitleText");
        content.Children.Add(WidgetUi.PopupHeader(L.T("Brightness"), _sliderValue));
        _slider = new Slider { Minimum = 0, Maximum = 100, Height = 22, IsMoveToPointEnabled = true, SmallChange = 5, LargeChange = 10, Margin = new Thickness(4, 0, 4, 6) };
        _slider.ValueChanged += (_, e) =>
        {
            _sliderValue.Text = $"{Math.Round(e.NewValue).ToString(CultureInfo.CurrentCulture)}%";
            if (!_updatingSlider) _ = Brightness.SetAsync((int)Math.Round(e.NewValue));
        };
        content.Children.Add(_slider);
        _unsupported = WidgetUi.Text("CaptionText", L.T("This screen doesn't allow changing brightness. External monitors need DDC/CI turned on in their own menu."));
        _unsupported.TextWrapping = TextWrapping.Wrap;
        _unsupported.Margin = new Thickness(4, 0, 4, 8);
        content.Children.Add(_unsupported);
        _nightButton = new Button { Padding = new Thickness(10, 5, 10, 5), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 2, 0, 0) };
        _nightButton.Click += (_, _) => OpenSettings("ms-settings:nightlight");
        var displaySettings = new Button { Content = L.T("Display settings"), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(6, 2, 0, 0) };
        displaySettings.Click += (_, _) => OpenSettings("ms-settings:display");
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _nightButton, displaySettings } });

        Content = new Grid { Background = System.Windows.Media.Brushes.Transparent, Children = { _iconLayout, _sliderLayout, _popup } };
    }

    private static BrightnessService Brightness => AppServices.Brightness;

    protected override void OnAttached()
    {
        if (IsPreview) return;
        Brightness.EnsureStarted();
        Brightness.Changed += Render;
        _popup.Opened += OnPopupOpened;
    }

    protected override void OnDetached()
    {
        if (IsPreview) return;
        Brightness.Changed -= Render;
        _popup.Opened -= OnPopupOpened;
    }

    private void OnPopupOpened(object? sender, EventArgs e) => _ = Brightness.RefreshAsync();

    protected override void OnVariantChanged()
    {
        ShowLayout(_iconLayout, _sliderLayout);
        Render();
    }

    private int? Level => IsPreview ? 70 : Brightness.Level;

    private void Render()
    {
        int? level = Level;
        bool night = !IsPreview && Brightness.NightLight;
        string text = level is { } l ? $"{l.ToString(CultureInfo.CurrentCulture)}%" : "—";

        _iconGlyph.Text = night ? MoonGlyph : SunGlyph;
        _iconGlyph.SetResourceReference(TextBlock.ForegroundProperty, night ? "AccentOrangeBrush" : "AccentYellowBrush");
        _iconBadge.Text = level?.ToString(CultureInfo.CurrentCulture) ?? "";
        WidgetUi.AnimateWidth(_barFill, _barTrack.ActualWidth * (level ?? 0) / 100.0);
        _barValue.Text = text;
        _nightDot.Visibility = night ? Visibility.Visible : Visibility.Collapsed;

        _updatingSlider = true;
        try
        {
            _slider.Value = level ?? 0;
            _slider.IsEnabled = level is not null;
            _sliderValue.Text = text;
        }
        finally
        {
            _updatingSlider = false;
        }
        _unsupported.Visibility = level is null ? Visibility.Visible : Visibility.Collapsed;
        _nightButton.Content = night ? L.T("Night light: on") : L.T("Night light: off");

        var tip = new List<string> { level is null ? L.T("Brightness can't be changed on this screen") : L.T("Brightness: {0}", text) };
        if (night) tip.Add(L.T("Night light is on"));
        if (level is not null) tip.Add(L.T("Scroll to change, click for more"));
        ToolTip = string.Join("\n", tip);
        Opacity = level is null && !IsPreview ? 0.6 : 1;
        RefreshCompact();
    }

    private static void OpenSettings(string uri) => NetworkStatusIconView.OpenSettings(uri);

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (IsPreview || _popup.IsOpen || Brightness.Level is not { } level) return;
        int step = e.Delta > 0 ? 5 : -5;
        _ = Brightness.SetAsync(Math.Clamp((level + step) / 5 * 5, 0, 100));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (DockDragHelper.JustDragged || IsPreview || e.Handled) return;
        OpenPopup(_popup);
        e.Handled = true;
    }

    public override bool OnCompactClick()
    {
        OpenPopup(_popup);
        return true;
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        int? level = Level;
        tile.ShowRing(level ?? 0, 100, "AccentYellowBrush", "OrangeTrackBrush", level?.ToString(CultureInfo.CurrentCulture));
        tile.Text = !IsPreview && Brightness.NightLight ? L.T("Night") : null;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(L.T("Night light settings"), "", () => OpenSettings("ms-settings:nightlight")));
        items.Add(DockMenu.Item(L.T("Display settings"), "", () => OpenSettings("ms-settings:display")));
    }
}
