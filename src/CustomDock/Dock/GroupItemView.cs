using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Services;
using CustomDock.Shell;
using CustomDock.Widgets;

namespace CustomDock.Dock;

/// <summary>
/// A folder-like group item on the dock. Shows a 2x2 grid of child icons when closed.
/// Clicking opens a fan popup where children appear one by one with stagger animation.
/// </summary>
public sealed class GroupItemView : Grid
{
    private const double IconSize = 14;
    private const double GroupSize = 44;
    private const double GroupHeight = 46;
    private const double ChildButtonSize = 42;
    private const int MaxPreviewIcons = 4;

    private readonly DockItem _item;
    private readonly IWidgetHost _host;
    private readonly Border _hover;
    private readonly Grid _iconGrid;
    private readonly TextBlock _label;
    private readonly Image[] _previewIcons = new Image[MaxPreviewIcons];
    private readonly ScaleTransform _pressScale = new();

    private Popup? _fanPopup;
    private bool _fanInteraction;
    private DateTime _fanClosedAt;

    public GroupItemView(DockItem item, IWidgetHost host)
    {
        _item = item;
        _host = host;
        Width = GroupSize;
        Height = GroupHeight;
        Margin = new Thickness(1, 0, 1, 0);
        Background = Brushes.Transparent;
        Focusable = false;
        AllowDrop = true;
        ToolTipService.SetInitialShowDelay(this, 450);
        ToolTip = item.GroupName ?? "Group";

        // Hover background
        _hover = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(1, 3, 1, 3), Opacity = 0 };
        _hover.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");

        // 2x2 icon grid preview
        _iconGrid = new Grid
        {
            Width = 30, Height = 30,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _pressScale,
        };
        _iconGrid.RowDefinitions.Add(new RowDefinition());
        _iconGrid.RowDefinitions.Add(new RowDefinition());
        _iconGrid.ColumnDefinitions.Add(new ColumnDefinition());
        _iconGrid.ColumnDefinitions.Add(new ColumnDefinition());

        for (int i = 0; i < MaxPreviewIcons; i++)
        {
            _previewIcons[i] = new Image
            {
                Width = IconSize, Height = IconSize,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0.5),
                Visibility = Visibility.Collapsed,
            };
            RenderOptions.SetBitmapScalingMode(_previewIcons[i], BitmapScalingMode.HighQuality);
            Grid.SetRow(_previewIcons[i], i / 2);
            Grid.SetColumn(_previewIcons[i], i % 2);
            _iconGrid.Children.Add(_previewIcons[i]);
        }

        // Fallback: group icon when no children
        var fallbackIcon = new TextBlock
        {
            Text = "\uE8B7", // Folder glyph
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        fallbackIcon.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");

        // Label
        _label = new TextBlock
        {
            Text = item.GroupName ?? "",
            FontSize = 8.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 1),
            MaxWidth = GroupSize - 4,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _label.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");

        Children.Add(_hover);
        Children.Add(_iconGrid);
        Children.Add(_label);

        MouseEnter += (_, _) => { Motion.Fade(_hover, 1, 120); AnimatePress(1.08); };
        MouseLeave += (_, _) => { Motion.Fade(_hover, 0, 220); AnimatePress(1); };
        MouseLeftButtonDown += (_, _) => AnimatePress(0.86);
        MouseLeftButtonUp += OnLeftUp;
        Loaded += OnFirstLoaded;

        ContextMenu = new ContextMenu();
        ContextMenuOpening += OnContextMenuOpening;

        // Accept drops (add item to group)
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        RefreshIcons();
    }

    public DockItem Item => _item;

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        Motion.Appear(this);
    }

    private void AnimatePress(double scale)
    {
        IEasingFunction easing = scale < 1
            ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
        Motion.Scale(_pressScale, scale, scale < 1 ? 90 : 260, easing);
    }

    /// <summary>Refreshes the 2x2 icon preview from the group's children.</summary>
    public void RefreshIcons()
    {
        var children = _item.Children ?? new List<DockItem>();
        ToolTip = _item.GroupName ?? "Group";
        _label.Text = _item.GroupName ?? "";

        for (int i = 0; i < MaxPreviewIcons; i++)
        {
            if (i < children.Count)
            {
                var child = children[i];
                _previewIcons[i].Source = GetChildIcon(child);
                _previewIcons[i].Visibility = _previewIcons[i].Source is not null ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                _previewIcons[i].Visibility = Visibility.Collapsed;
            }
        }
    }

    private static ImageSource? GetChildIcon(DockItem child)
    {
        if (child.Kind == DockItemKind.App && child.Path is not null)
            return ShellIcons.GetIcon(child.Path, 48);
        if (child.Kind == DockItemKind.Widget && WidgetRegistry.Find(child.Widget) is { } descriptor)
        {
            // Render the widget icon path geometry into a small image
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
        return null;
    }

    // ------------------------------------------------------------------ Fan popup

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        AnimatePress(IsMouseOver ? 1.08 : 1);
        if (DockDragHelper.JustDragged) return;
        e.Handled = true;

        if (_fanPopup?.IsOpen == true)
            CloseFan();
        else if (DateTime.UtcNow - _fanClosedAt > TimeSpan.FromMilliseconds(250))
            OpenFan();
    }

    private void OpenFan()
    {
        var children = _item.Children;
        if (children is null || children.Count == 0) return;

        var edge = _host.Edge;
        bool vertical = edge is DockEdge.Left or DockEdge.Right;

        // Build the fan content: a WrapPanel with child buttons
        var panel = new WrapPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            MaxWidth = vertical ? double.PositiveInfinity : Math.Min(children.Count, 6) * (ChildButtonSize + 6) + 12,
            MaxHeight = vertical ? Math.Min(children.Count, 6) * (ChildButtonSize + 6) + 12 : double.PositiveInfinity,
        };

        var fanOffset = FanOffset(edge);
        int index = 0;
        foreach (var child in children)
        {
            var btn = CreateChildButton(child);
            panel.Children.Add(btn);
            // Apply stagger animation after layout
            int idx = index++;
            btn.Loaded += (_, _) => Motion.FanOut(btn, fanOffset, idx, staggerMs: 50, durationMs: 300);
        }

        var frame = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            Child = panel,
            Margin = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.35 },
        };
        frame.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");

        // Add a header label
        var header = new TextBlock
        {
            Text = _item.GroupName ?? "Group",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 2, 6, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        header.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(panel);
        frame.Child = stack;

        _fanPopup = new Popup
        {
            Child = frame,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.None,
            PlacementTarget = this,
        };
        _fanPopup.Closed += (_, _) =>
        {
            _fanClosedAt = DateTime.UtcNow;
            if (_fanInteraction) { _fanInteraction = false; _host.EndInteraction(); }
        };

        PopupPlacement.PlacePopup(_fanPopup, this, edge, gap: 6);
        Motion.PopIn(frame, PopupPlacement.EnterOffset(edge));
        _fanInteraction = true;
        _host.BeginInteraction();
        GlobalPopupDismissHook.RegisterPopup(_fanPopup);
        _fanPopup.IsOpen = true;
    }

    private static Vector FanOffset(DockEdge edge) => edge switch
    {
        DockEdge.Top => new Vector(0, -20),
        DockEdge.Left => new Vector(-20, 0),
        DockEdge.Right => new Vector(20, 0),
        _ => new Vector(0, 20),
    };

    private FrameworkElement CreateChildButton(DockItem child)
    {
        var icon = new Image
        {
            Width = 28, Height = 28,
            Stretch = Stretch.Uniform,
            Source = GetChildIcon(child),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        var hover = new Border
        {
            CornerRadius = new CornerRadius(8),
            Opacity = 0,
            Child = icon,
        };
        hover.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");

        string title = child.Kind == DockItemKind.App
            ? (!string.IsNullOrWhiteSpace(child.Name) ? child.Name! : System.IO.Path.GetFileNameWithoutExtension(child.Path ?? ""))
            : (WidgetRegistry.Find(child.Widget)?.Name ?? child.Widget ?? "Widget");

        var label = new TextBlock
        {
            Text = title,
            FontSize = 9,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = ChildButtonSize + 8,
        };
        label.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");

        var stack = new StackPanel
        {
            Width = ChildButtonSize + 8,
            Children = { hover, label },
            Margin = new Thickness(3),
        };

        var wrapper = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
            Width = ChildButtonSize + 16,
            Height = ChildButtonSize + 24,
            Background = Brushes.Transparent,
            Child = stack,
        };
        wrapper.ToolTip = title;

        wrapper.MouseEnter += (_, _) => Motion.Fade(hover, 1, 120);
        wrapper.MouseLeave += (_, _) => Motion.Fade(hover, 0, 220);
        wrapper.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseFan();
            if (child.Kind == DockItemKind.App && child.Path is not null)
                AppLauncher.Launch(child);
            else if (child.Kind == DockItemKind.Widget)
            {
                // Open widget settings
                WidgetItemView.RequestSettings(child);
            }
        };

        return wrapper;
    }

    private void CloseFan()
    {
        if (_fanPopup is { IsOpen: true }) _fanPopup.IsOpen = false;
    }

    // ------------------------------------------------------------------ Context menu

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        CloseFan();
        var menu = ContextMenu;
        menu.Items.Clear();
        menu.Items.Add(DockMenu.Header(_item.GroupName ?? "Group"));

        // List children
        var children = _item.Children ?? new List<DockItem>();
        if (children.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            foreach (var child in children)
            {
                string title = child.Kind == DockItemKind.App
                    ? (!string.IsNullOrWhiteSpace(child.Name) ? child.Name! : System.IO.Path.GetFileNameWithoutExtension(child.Path ?? ""))
                    : (WidgetRegistry.Find(child.Widget)?.Name ?? "Widget");
                var c = child;
                menu.Items.Add(DockMenu.Item(title, null, () =>
                {
                    if (c.Kind == DockItemKind.App && c.Path is not null)
                        AppLauncher.Launch(c);
                }));
            }
        }

        menu.Items.Add(DockMenu.Separator());

        // Color submenu
        var accents = new[] { "AccentBlueBrush", "AccentGreenBrush", "AccentOrangeBrush", "AccentRedBrush",
            "AccentPurpleBrush", "AccentCyanBrush", "AccentPinkBrush", "AccentYellowBrush" };
        var colorNames = new[] { "Blue", "Green", "Orange", "Red", "Purple", "Cyan", "Pink", "Yellow" };
        menu.Items.Add(DockMenu.Submenu("Group color", "\uE790", accents.Select((a, i) =>
            DockMenu.Check(colorNames[i], _item.GroupAccent == a, () =>
            {
                _item.GroupAccent = a;
                AppServices.ConfigService.ScheduleSave();
            }))));

        menu.Items.Add(DockMenu.Item("Ungroup all", "\uE8C8", () => AppServices.ConfigService.UngroupAll(_item.Id)));
        menu.Items.Add(DockMenu.Item("Remove group", "\uE77A", () => AppServices.ConfigService.RemoveItem(_item.Id)));
    }

    // ------------------------------------------------------------------ Drop handling (add items to group)

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DockDragHelper.ItemFormat) || e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            Motion.Fade(_hover, 1, 100);
            AnimatePress(1.08);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DockDragHelper.ItemFormat) || e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        Motion.Fade(_hover, 0, 160);
        AnimatePress(1);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        Motion.Fade(_hover, 0, 160);
        AnimatePress(1);

        if (e.Data.GetData(DockDragHelper.ItemFormat) is string itemId)
        {
            e.Handled = true;
            // Don't add self to self
            if (itemId == _item.Id) return;
            var config = AppServices.ConfigService;
            var existing = config.FindItem(itemId);
            if (existing is null || existing.Kind == DockItemKind.Group) return;

            // Remove from current position and add to this group
            config.RemoveItem(itemId);
            config.AddToGroup(_item.Id, existing);
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            e.Handled = true;
            foreach (var file in files.Where(f => !string.IsNullOrWhiteSpace(f)))
                AppServices.ConfigService.AddToGroup(_item.Id, DockItem.App(file));
        }
    }

    public void Detach()
    {
        CloseFan();
    }
}
