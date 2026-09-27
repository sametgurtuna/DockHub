using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Services;
using CustomDock.Shell;
using CustomDock.Widgets;

namespace CustomDock.Dock;

/// <summary>
/// A sleek, macOS Stacks-style folder dock item.
/// Closed: 38x38 frosted glass tile with an accent border; shows 2x2 child icons or a folder glyph when empty.
/// Open: A fluid glass popup with header, item count, staggered app grid, color picker, and quick actions.
/// </summary>
public sealed partial class GroupItemView : Grid
{
    private const double GroupWidth = 44;
    private const double GroupHeight = 46;
    private const double TileSize = 38;
    private const double MiniIconSize = 13;
    private const int MaxPreviewIcons = 4;

    private readonly DockItem _item;
    private readonly IWidgetHost _host;
    private readonly Border _hover;
    private readonly Border _tileBorder;
    private readonly Border _runningIndicator;

    /// <summary>Folder apps behave like dock buttons: switch to the running app, or start it.</summary>
    private static void ActivateChild(DockItem child)
    {
        var group = App.Instance.Shell?.RunningApps.Find(AppKeys.ForItem(child));
        AppLauncher.Activate(child, group);
    }

    /// <summary>An app inside the folder has open windows.</summary>
    public void SetRunning(bool running) => _runningIndicator.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    private readonly Grid _tileContent;
    private readonly TextBlock _emptyFolderGlyph;
    private readonly Grid _previewGrid;
    private readonly Image[] _previewIcons = new Image[MaxPreviewIcons];
    private readonly ScaleTransform _pressScale = new();

    private Popup? _fanPopup;
    private bool _fanInteraction;
    private DateTime _fanClosedAt;

    private static readonly (string Name, string BrushKey, Color Color)[] AccentColors =
    {
        ("Blue", "AccentBlueBrush", Color.FromRgb(0, 120, 215)),
        ("Green", "AccentGreenBrush", Color.FromRgb(16, 124, 65)),
        ("Orange", "AccentOrangeBrush", Color.FromRgb(202, 80, 16)),
        ("Red", "AccentRedBrush", Color.FromRgb(232, 17, 35)),
        ("Purple", "AccentPurpleBrush", Color.FromRgb(136, 23, 152)),
        ("Cyan", "AccentCyanBrush", Color.FromRgb(0, 153, 188)),
        ("Pink", "AccentPinkBrush", Color.FromRgb(234, 0, 94)),
        ("Yellow", "AccentYellowBrush", Color.FromRgb(255, 185, 0)),
    };

    public GroupItemView(DockItem item, IWidgetHost host)
    {
        _item = item;
        _host = host;
        Width = GroupWidth;
        Height = GroupHeight;
        Margin = new Thickness(1, 0, 1, 0);
        Background = Brushes.Transparent;
        Focusable = false;
        AllowDrop = true;
        Cursor = Cursors.Hand;
        ToolTipService.SetInitialShowDelay(this, 350);

        // Hover highlight overlay
        _hover = new Border
        {
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(1, 2, 1, 2),
            Opacity = 0,
        };
        _hover.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");

        // 38x38 Frosted Glass Tile
        _tileBorder = new Border
        {
            Width = TileSize,
            Height = TileSize,
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1.2),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _pressScale,
        };

        _tileContent = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // 1. Empty folder glyph
        _emptyFolderGlyph = new TextBlock
        {
            Text = "\uE8B7",
            FontFamily = GetIconFont(),
            FontSize = 21,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Visible,
        };
        _tileContent.Children.Add(_emptyFolderGlyph);

        // 2. 2x2 Preview grid
        _previewGrid = new Grid
        {
            Width = 28,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _previewGrid.RowDefinitions.Add(new RowDefinition());
        _previewGrid.RowDefinitions.Add(new RowDefinition());
        _previewGrid.ColumnDefinitions.Add(new ColumnDefinition());
        _previewGrid.ColumnDefinitions.Add(new ColumnDefinition());

        for (int i = 0; i < MaxPreviewIcons; i++)
        {
            _previewIcons[i] = new Image
            {
                Width = MiniIconSize,
                Height = MiniIconSize,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0.5),
                Visibility = Visibility.Collapsed,
            };
            RenderOptions.SetBitmapScalingMode(_previewIcons[i], BitmapScalingMode.HighQuality);
            Grid.SetRow(_previewIcons[i], i / 2);
            Grid.SetColumn(_previewIcons[i], i % 2);
            _previewGrid.Children.Add(_previewIcons[i]);
        }
        _tileContent.Children.Add(_previewGrid);

        _tileBorder.Child = _tileContent;

        Children.Add(_hover);
        Children.Add(_tileBorder);
        _runningIndicator = new Border
        {
            Height = 3,
            Width = 5,
            CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _runningIndicator.SetResourceReference(Border.BackgroundProperty, "IndicatorBrush");
        Children.Add(_runningIndicator);

        MouseEnter += (_, _) => { Motion.Fade(_hover, 1, 120); AnimatePress(1.08); WindowPreviewWindow.Instance.HidePreview(); };
        MouseLeave += (_, _) => { Motion.Fade(_hover, 0, 220); AnimatePress(1); };
        MouseLeftButtonDown += (_, _) => AnimatePress(0.88);
        MouseLeftButtonUp += OnLeftUp;
        Loaded += OnFirstLoaded;

        ContextMenu = new ContextMenu();
        ContextMenuOpening += OnContextMenuOpening;

        // Drag and drop into group
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        RefreshAppearance();
    }

    public DockItem Item => _item;

    private static FontFamily GetIconFont()
    {
        return Application.Current.TryFindResource("IconFont") as FontFamily
               ?? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        Motion.Appear(this);
    }

    private void AnimatePress(double scale)
    {
        IEasingFunction easing = scale < 1
            ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        Motion.Scale(_pressScale, scale, scale < 1 ? 80 : 220, easing);
    }

    /// <summary>Updates tile color, folder glyph, 2x2 icons, and tooltip.</summary>
    public void RefreshAppearance()
    {
        var children = _item.Children ?? new List<DockItem>();
        string name = string.IsNullOrWhiteSpace(_item.GroupName) ? "Folder" : _item.GroupName!;
        ToolTip = $"{name} · {children.Count} item{(children.Count == 1 ? "" : "s")}";

        // Accent color lookup
        var accentBrush = Application.Current.TryFindResource(_item.GroupAccent ?? "AccentBlueBrush") as Brush
                          ?? Brushes.DodgerBlue;
        Color accentColor = accentBrush is SolidColorBrush scb ? scb.Color : Color.FromRgb(0, 120, 215);

        _emptyFolderGlyph.Foreground = accentBrush;

        // Frosted glass background + accent border
        _tileBorder.Background = new SolidColorBrush(Color.FromArgb(42, accentColor.R, accentColor.G, accentColor.B));
        _tileBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(130, accentColor.R, accentColor.G, accentColor.B));
        System.Windows.Automation.AutomationProperties.SetName(this, L.T("Folder: {0}, {1} items", _item.GroupName ?? L.T("Folder"), children.Count));

        if (children.Count == 0)
        {
            _emptyFolderGlyph.Visibility = Visibility.Visible;
            _previewGrid.Visibility = Visibility.Collapsed;
        }
        else
        {
            _emptyFolderGlyph.Visibility = Visibility.Collapsed;
            _previewGrid.Visibility = Visibility.Visible;

            for (int i = 0; i < MaxPreviewIcons; i++)
            {
                if (i < children.Count)
                {
                    _previewIcons[i].Source = GetChildIcon(children[i]);
                    _previewIcons[i].Visibility = _previewIcons[i].Source is not null ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    _previewIcons[i].Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    public void RefreshIcons() => RefreshAppearance();

    private static ImageSource? GetChildIcon(DockItem child)
    {
        try
        {
            if (child.Kind == DockItemKind.App && child.Path is not null)
            {
                // Never leave a gap in the folder: missing icons show the generic app icon until they load.
                var icon = AppIcons.For(child, 48, out bool isFallback);
                if (isFallback) AppIcons.Invalidate(child);
                return icon;
            }
            if (child.Kind == DockItemKind.Widget && WidgetRegistry.Find(child.Widget) is { } descriptor)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    var brush = Application.Current.TryFindResource(descriptor.AccentKey) as Brush ?? Brushes.Gray;
                    var pen = new Pen(brush, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    dc.DrawGeometry(null, pen, descriptor.Icon);
                }
                var bitmap = new RenderTargetBitmap(24, 24, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get child icon");
        }
        return null;
    }

    public void Detach()
    {
        CloseFan();
    }
}
