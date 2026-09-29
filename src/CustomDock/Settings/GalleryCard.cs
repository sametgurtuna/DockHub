using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Widgets;
using CustomDock.Widgets.Web;

namespace CustomDock.Settings;

/// <summary>
/// A card of the widget gallery: name, description, layouts, width class, how many are on the dock and an Add button,
/// all at one fixed width. Widgets get a live preview, but only while the card is in view (<see cref="ShowPreview"/>,
/// <see cref="HidePreview"/>), so the gallery never runs more of them than it shows. The preview area can be dragged
/// onto the dock. Community widgets (not installed) get an Install button instead.
/// </summary>
internal sealed class GalleryCard : Border
{
    /// <summary>Wide enough for the widest web widget (260 + card padding) next to the stage padding.</summary>
    public const double CardWidth = 320;
    private const double StageHeight = 96;

    private readonly WidgetDescriptor? _descriptor;
    private readonly IWidgetHost? _host;
    private readonly DockItem? _previewItem;
    private readonly Border? _stage;
    private readonly TextBlock? _widthBadge;
    private readonly TextBlock? _countBadge;
    private readonly Button _primary;
    private WidgetBase? _preview;
    private string? _variant;
    private bool _compact;

    public GalleryEntry Entry { get; }

    /// <summary>True for widget cards, which show a live preview while in view.</summary>
    public bool HasStage => _stage is not null;

    public bool IsPreviewShown => _preview is not null;

    /// <summary>The arrow keys on a focused card: the gallery picks the card to go to.</summary>
    public event Action<GalleryCard, FocusNavigationDirection>? NavigationRequested;

    /// <summary>A built-in or installed web widget.</summary>
    /// <param name="host">Host of the live preview; null for the compact card of the dock's "+" picker (no preview).</param>
    /// <param name="add">Adds the widget with the chosen layout to the dock.</param>
    public GalleryCard(WidgetDescriptor descriptor, IWidgetHost? host, Action<WidgetDescriptor, string?> add)
    {
        Entry = GalleryEntry.From(descriptor);
        _descriptor = descriptor;
        _host = host;
        _variant = descriptor.DefaultVariant;

        _primary = new Button { Content = L.T("Add"), MinWidth = 76, Padding = new Thickness(14, 4, 14, 4), VerticalAlignment = VerticalAlignment.Bottom };
        _primary.SetResourceReference(StyleProperty, "AccentButton");
        AutomationProperties.SetName(_primary, L.T("Add {0} to the dock", descriptor.Name));
        _primary.Click += (_, _) =>
        {
            add(descriptor, _variant);
            Confirm();
        };

        if (host is null)
        {
            BuildCompact(descriptor);
            return;
        }

        _previewItem = new DockItem { Id = "gallery-" + descriptor.Id, Kind = DockItemKind.Widget, Widget = descriptor.Id, Variant = _variant };

        _stage = new Border
        {
            Height = StageHeight,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 10, 6, 10),
            Cursor = Cursors.SizeAll,
            ToolTip = L.T("Drag onto the dock to add it there"),
        };
        _stage.SetResourceReference(BackgroundProperty, "GalleryBackdropBrush");
        ShowPlaceholder();
        // The whole card drags (a web widget's preview is a window of its own that keeps the mouse).
        DockDragHelper.Attach(this, () => DockDragHelper.StringData(DockDragHelper.NewWidgetFormat,
            NewWidgetDrag.Encode(descriptor.Id, _variant)));

        var badges = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        _widthBadge = Badge("");
        _countBadge = Badge("");
        badges.Children.Add(_widthBadge);
        badges.Children.Add(_countBadge);
        if (descriptor.Category == WidgetCategories.Web
            && WebWidgetCatalog.Installed.FirstOrDefault(m => m.WidgetId == descriptor.Id) is { } manifest)
            badges.Children.Add(Badge(GalleryFilter.PermissionSummary(manifest.Permissions,
                key => manifest.Settings.FirstOrDefault(s => s.Key == key)?.Label ?? key)));

        var body = new StackPanel();
        body.Children.Add(_stage);
        body.Children.Add(Title(descriptor.Name));
        body.Children.Add(Description(descriptor.Description));
        if (descriptor.Variants.Count > 1) body.Children.Add(VariantPicker(descriptor));
        body.Children.Add(badges);
        Build(body);
        UpdateBadges();
    }

    /// <summary>A widget from the community list.</summary>
    /// <param name="install">Downloads and installs it, showing progress on the button.</param>
    public GalleryCard(WidgetIndexEntry entry, bool installed, Func<Button, Task> install)
    {
        Entry = new GalleryEntry(entry.Id, entry.Name, entry.Description, GalleryFilter.Community,
            new[] { entry.EnglishName, entry.EnglishDescription, entry.Author ?? "", "community", L.T("Community widgets"), "web" });

        var body = new StackPanel();
        body.Children.Add(Title(entry.Name));
        if (entry.Author is { Length: > 0 } author && author != "DockHub")
        {
            var by = Badge(L.T("By {0}", author));
            by.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(by);
        }
        body.Children.Add(Description(entry.Description));

        _primary = new Button
        {
            Content = installed ? L.T("Installed") : L.T("Install"),
            IsEnabled = !installed,
            MinWidth = 90,
            Padding = new Thickness(14, 4, 14, 4),
        };
        if (!installed) _primary.SetResourceReference(StyleProperty, "AccentButton");
        AutomationProperties.SetName(_primary, installed ? L.T("{0} is installed", entry.Name) : L.T("Install {0}", entry.Name));
        _primary.Click += async (_, _) => await install(_primary);
        Build(body);
    }

    private void Build(StackPanel body)
    {
        Width = CardWidth;
        Margin = new Thickness(0, 0, 12, 12);
        ApplyFrame();

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        DockPanel.SetDock(_primary, System.Windows.Controls.Dock.Right);
        footer.Children.Add(_primary);
        body.Children.Add(footer);
        Child = body;
    }

    /// <summary>The small card of the dock's "+" picker: icon, name, description and Add, no preview.</summary>
    private void BuildCompact(WidgetDescriptor descriptor)
    {
        _compact = true;
        Margin = new Thickness(0, 0, 0, 6);
        ApplyFrame();

        var icon = Icon(descriptor, 18);
        icon.Margin = new Thickness(0, 2, 10, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Title(descriptor.Name);
        title.Margin = new Thickness(0);
        text.Children.Add(title);
        var description = Description(descriptor.Description);
        description.MaxHeight = 2 * 16;
        description.TextTrimming = TextTrimming.WordEllipsis;
        description.ToolTip = descriptor.Description;
        text.Children.Add(description);

        _primary.MinWidth = 0;
        _primary.Padding = new Thickness(10, 3, 10, 3);
        _primary.Margin = new Thickness(10, 0, 0, 0);
        _primary.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel();
        DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);
        DockPanel.SetDock(_primary, System.Windows.Controls.Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(_primary);
        row.Children.Add(text);
        Child = row;
    }

    private void ApplyFrame()
    {
        CornerRadius = new CornerRadius(_compact ? 8 : 12);
        UpdateFocusLook();
        SetResourceReference(BackgroundProperty, "SurfaceBrush");

        // Enter on the card adds (or installs) it; Tab and the arrow keys move between cards.
        Focusable = true;
        FocusVisualStyle = null;
        KeyboardNavigation.SetIsTabStop(this, true);
        AutomationProperties.SetName(this, $"{Entry.Name}. {Entry.Description}");
        GotKeyboardFocus += (_, _) => UpdateFocusLook();
        LostKeyboardFocus += (_, _) => UpdateFocusLook();
        KeyDown += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, this)) return;
            if (e.Key == Key.Enter)
            {
                if (!_primary.IsEnabled) return;
                e.Handled = true;
                _primary.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, _primary));
            }
            else if (NavigationRequested is { } navigate && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                e.Handled = true;
                navigate(this, e.Key switch
                {
                    Key.Left => FocusNavigationDirection.Left,
                    Key.Right => FocusNavigationDirection.Right,
                    Key.Up => FocusNavigationDirection.Up,
                    _ => FocusNavigationDirection.Down,
                });
            }
        };
    }

    /// <summary>Briefly outlines the card in the accent color (the settings search found it).</summary>
    public void Flash()
    {
        var accent = (TryFindResource("AccentBrush") as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        var brush = new SolidColorBrush(accent);
        BorderBrush = brush;
        BorderThickness = new Thickness(2);
        Padding = new Thickness((_compact ? 10 : 12) - 1);
        var fade = new System.Windows.Media.Animation.ColorAnimation(Color.FromArgb(0, accent.R, accent.G, accent.B), TimeSpan.FromMilliseconds(900))
        {
            BeginTime = TimeSpan.FromMilliseconds(900),
        };
        fade.Completed += (_, _) => UpdateFocusLook();
        brush.BeginAnimation(SolidColorBrush.ColorProperty, fade);
    }

    /// <summary>An accent outline while the card has the keyboard focus (the padding keeps the content in place).</summary>
    private void UpdateFocusLook()
    {
        double padding = _compact ? 10 : 12;
        bool focused = IsKeyboardFocused;
        SetResourceReference(BorderBrushProperty, focused ? "AccentBrush" : "SurfaceBorderBrush");
        BorderThickness = new Thickness(focused ? 2 : 1);
        Padding = new Thickness(focused ? padding - 1 : padding);
    }

    private static System.Windows.Shapes.Path Icon(WidgetDescriptor descriptor, double size)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = descriptor.Icon,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, descriptor.AccentKey);
        return icon;
    }

    private static TextBlock Title(string text)
    {
        var title = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) };
        title.SetResourceReference(StyleProperty, "SettingTitle");
        return title;
    }

    private static TextBlock Description(string text)
    {
        var description = new TextBlock { Text = text };
        description.SetResourceReference(StyleProperty, "SettingDescription");
        return description;
    }

    private static TextBlock Badge(string text)
    {
        var badge = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(0, 0, 6, 4),
            TextWrapping = TextWrapping.Wrap,
        };
        badge.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        badge.SetResourceReference(TextBlock.BackgroundProperty, "SubtleFillBrush");
        return badge;
    }

    private FrameworkElement VariantPicker(WidgetDescriptor descriptor)
    {
        var options = new WrapPanel();
        string group = "gallery-" + descriptor.Id;
        foreach (var variant in descriptor.Variants)
        {
            var option = new RadioButton
            {
                Content = variant.Name,
                GroupName = group,
                IsChecked = variant.Id == _variant,
                Padding = new Thickness(9, 3, 9, 3),
                FontSize = 11.5,
            };
            option.SetResourceReference(StyleProperty, "SegmentedItem");
            var id = variant.Id;
            option.Checked += (_, _) => SelectVariant(id);
            options.Children.Add(option);
        }
        var host = new Border { Child = options, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        host.SetResourceReference(StyleProperty, "SegmentedHost");
        host.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(host, L.T("Widget layout"));
        return host;
    }

    /// <summary>The preview switches layout in place; no second widget is made.</summary>
    private void SelectVariant(string variant)
    {
        _variant = variant;
        if (_previewItem is not null) _previewItem.Variant = variant;
        UpdateBadges();
    }

    /// <summary>Width class of the chosen layout and how many of this widget are on the dock.</summary>
    public void UpdateBadges()
    {
        if (_descriptor is null || _widthBadge is null || _countBadge is null) return;
        var width = GalleryFilter.DisplayWidth(_descriptor.Variants.FirstOrDefault(v => v.Id == _variant)?.Width ?? WidgetWidth.Auto);
        _widthBadge.Text = width switch
        {
            WidgetWidth.Compact => L.T("Compact width"),
            WidgetWidth.Wide => L.T("Wide width"),
            _ => L.T("Standard width"),
        };
        int count = GalleryFilter.CountOnDock(AppServices.Config.Items, _descriptor.Id);
        _countBadge.Text = L.T("On the dock ×{0}", count);
        _countBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Confirm()
    {
        _primary.Content = "";
        _primary.SetResourceReference(Control.FontFamilyProperty, "IconFont");
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _primary.Content = L.T("Add");
            _primary.ClearValue(Control.FontFamilyProperty);
        };
        timer.Start();
    }

    // ------------------------------------------------------------------ Live preview

    /// <summary>Creates and starts the preview (the card came into view).</summary>
    public void ShowPreview()
    {
        if (_stage is null || _descriptor is null || _previewItem is null || _host is null || _preview is not null) return;
        try
        {
            var widget = _descriptor.Create(_previewItem);
            var card = new WidgetCard { Content = widget, HoverEnabled = false, Margin = new Thickness(0) };
            void Apply()
            {
                if (widget.CardBackground is { } background) card.Background = background;
                else card.SetResourceReference(Control.BackgroundProperty, "CardBrush");
                card.Padding = widget.CardPadding;
            }
            widget.CardAppearanceChanged += Apply;
            Apply();
            if (_descriptor.Category == WidgetCategories.Web)
            {
                // A web view is a window of its own and can't be scaled; web widgets fit the stage as they are.
                card.HorizontalAlignment = HorizontalAlignment.Center;
                card.VerticalAlignment = VerticalAlignment.Center;
                _stage.Child = card;
            }
            else
            {
                // Wide layouts shrink to fit the card; nothing is cut off, and small ones keep their size.
                _stage.Child = new Viewbox
                {
                    Child = card,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                };
            }
            widget.Attach(_host);
            _preview = widget;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to create gallery preview: {_descriptor.Id}");
            ShowPlaceholder();
        }
    }

    /// <summary>Stops and drops the preview (the card left the view, or the gallery closed).</summary>
    public void HidePreview()
    {
        if (_preview is not { } preview) return;
        _preview = null;
        preview.Detach();
        ShowPlaceholder();
    }

    /// <summary>The widget's icon while no preview runs.</summary>
    private void ShowPlaceholder()
    {
        if (_stage is null || _descriptor is null) return;
        var icon = Icon(_descriptor, 26);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Opacity = 0.7;
        _stage.Child = icon;
    }
}
