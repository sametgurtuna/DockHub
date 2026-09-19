using System.IO;
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
/// A folder-like group item on the dock. Shows a stylish folder icon or a 2x2 grid of child icons.
/// Clicking opens a popup where children appear with stagger animation.
/// </summary>
public sealed class GroupItemView : Grid
{
    private const double IconSize = 13;
    private const double GroupSize = 44;
    private const double GroupHeight = 46;
    private const double ChildButtonSize = 44;
    private const int MaxPreviewIcons = 4;

    private readonly DockItem _item;
    private readonly IWidgetHost _host;
    private readonly Border _hover;
    private readonly Border _cardBorder;
    private readonly Grid _iconContainer;
    private readonly Grid _iconGrid;
    private readonly TextBlock _fallbackIcon;
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

        // Hover background
        _hover = new Border
        {
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(1, 2, 1, 2),
            Opacity = 0,
        };
        _hover.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");

        // Subtle folder card border
        _cardBorder = new Border
        {
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(2, 3, 2, 4),
            BorderThickness = new Thickness(1),
            Opacity = 0.85,
        };

        // Icon container that scales on press
        _iconContainer = new Grid
        {
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _pressScale,
        };

        // 1. Fallback folder icon (when empty)
        _fallbackIcon = new TextBlock
        {
            Text = "\uE8B7", // Folder glyph
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Visible,
        };
        _iconContainer.Children.Add(_fallbackIcon);

        // 2. 2x2 grid preview (when children exist)
        _iconGrid = new Grid
        {
            Width = 28,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _iconGrid.RowDefinitions.Add(new RowDefinition());
        _iconGrid.RowDefinitions.Add(new RowDefinition());
        _iconGrid.ColumnDefinitions.Add(new ColumnDefinition());
        _iconGrid.ColumnDefinitions.Add(new ColumnDefinition());

        for (int i = 0; i < MaxPreviewIcons; i++)
        {
            _previewIcons[i] = new Image
            {
                Width = IconSize,
                Height = IconSize,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0.5),
                Visibility = Visibility.Collapsed,
            };
            RenderOptions.SetBitmapScalingMode(_previewIcons[i], BitmapScalingMode.HighQuality);
            Grid.SetRow(_previewIcons[i], i / 2);
            Grid.SetColumn(_previewIcons[i], i % 2);
            _iconGrid.Children.Add(_previewIcons[i]);
        }
        _iconContainer.Children.Add(_iconGrid);

        // Label
        _label = new TextBlock
        {
            FontSize = 8.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(1, 0, 1, 1),
            MaxWidth = GroupSize - 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _label.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");

        Children.Add(_cardBorder);
        Children.Add(_hover);
        Children.Add(_iconContainer);
        Children.Add(_label);

        MouseEnter += (_, _) => { Motion.Fade(_hover, 1, 120); AnimatePress(1.08); };
        MouseLeave += (_, _) => { Motion.Fade(_hover, 0, 220); AnimatePress(1); };
        MouseLeftButtonDown += (_, _) => AnimatePress(0.86);
        MouseLeftButtonUp += OnLeftUp;
        Loaded += OnFirstLoaded;

        ContextMenu = new ContextMenu();
        ContextMenuOpening += OnContextMenuOpening;

        // Accept drag drops
        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        RefreshAppearance();
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

    /// <summary>Updates color, folder icon, child icons, and label.</summary>
    public void RefreshAppearance()
    {
        var children = _item.Children ?? new List<DockItem>();
        string name = string.IsNullOrWhiteSpace(_item.GroupName) ? "Folder" : _item.GroupName!;
        ToolTip = $"{name} ({children.Count} items)";
        _label.Text = name;

        // Apply accent color
        var accentBrush = Application.Current.TryFindResource(_item.GroupAccent ?? "AccentBlueBrush") as Brush
                          ?? Brushes.DodgerBlue;

        _fallbackIcon.Foreground = accentBrush;

        if (accentBrush is SolidColorBrush scb)
        {
            _cardBorder.Background = new SolidColorBrush(Color.FromArgb(28, scb.Color.R, scb.Color.G, scb.Color.B));
            _cardBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(70, scb.Color.R, scb.Color.G, scb.Color.B));
        }
        else
        {
            _cardBorder.Background = new SolidColorBrush(Color.FromArgb(28, 76, 194, 255));
            _cardBorder.BorderBrush = accentBrush;
        }

        if (children.Count == 0)
        {
            _fallbackIcon.Visibility = Visibility.Visible;
            _iconGrid.Visibility = Visibility.Collapsed;
        }
        else
        {
            _fallbackIcon.Visibility = Visibility.Collapsed;
            _iconGrid.Visibility = Visibility.Visible;

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
        if (child.Kind == DockItemKind.App && child.Path is not null)
            return ShellIcons.GetIcon(child.Path, 48);
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
        else if (DateTime.UtcNow - _fanClosedAt > TimeSpan.FromMilliseconds(200))
            OpenFan();
    }

    private void OpenFan()
    {
        CloseFan();

        var children = _item.Children ?? new List<DockItem>();
        var edge = _host.Edge;
        bool vertical = edge is DockEdge.Left or DockEdge.Right;

        var accentBrush = Application.Current.TryFindResource(_item.GroupAccent ?? "AccentBlueBrush") as Brush
                          ?? Brushes.DodgerBlue;

        // Container frame
        var frame = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8),
            MinWidth = 160,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.4 },
        };
        frame.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");

        var mainStack = new StackPanel();

        // Header
        var headerGrid = new Grid { Margin = new Thickness(2, 0, 2, 8) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var folderGlyph = new TextBlock
        {
            Text = "\uE8B7",
            FontSize = 14,
            Foreground = accentBrush,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var titleText = new TextBlock
        {
            Text = _item.GroupName ?? "Folder",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleText.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");

        var countBadge = new TextBlock
        {
            Text = $" ({children.Count})",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        countBadge.SetResourceReference(TextElement.ForegroundProperty, "TextTertiaryBrush");

        titleRow.Children.Add(folderGlyph);
        titleRow.Children.Add(titleText);
        titleRow.Children.Add(countBadge);
        Grid.SetColumn(titleRow, 0);
        headerGrid.Children.Add(titleRow);

        var editBtn = new Button
        {
            Content = "\uE8AC", // Edit pencil
            FontFamily = (FontFamily)FindResource("SegoeIcons"),
            FontSize = 10,
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            ToolTip = "Rename folder",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        editBtn.SetResourceReference(Button.ForegroundProperty, "TextTertiaryBrush");
        editBtn.Click += (_, _) =>
        {
            CloseFan();
            PromptRename();
        };
        Grid.SetColumn(editBtn, 1);
        headerGrid.Children.Add(editBtn);

        mainStack.Children.Add(headerGrid);

        if (children.Count == 0)
        {
            // Empty state view
            var emptyPanel = new StackPanel
            {
                Margin = new Thickness(4, 6, 4, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var emptyMsg = new TextBlock
            {
                Text = "This folder is empty",
                FontSize = 11.5,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 2),
            };
            emptyMsg.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");

            var emptyHint = new TextBlock
            {
                Text = "Drag apps or widgets here to group them",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10),
            };
            emptyHint.SetResourceReference(TextElement.ForegroundProperty, "TextTertiaryBrush");

            var addBtn = new Button
            {
                Content = "+ Add Application",
                Height = 28,
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 11,
            };
            addBtn.SetResourceReference(Button.BackgroundProperty, "AccentBlueBrush");
            addBtn.SetResourceReference(Button.ForegroundProperty, "TextOnColorBrush");
            addBtn.Click += (_, _) =>
            {
                CloseFan();
                App.Instance.ShowAppPicker();
            };

            emptyPanel.Children.Add(emptyMsg);
            emptyPanel.Children.Add(emptyHint);
            emptyPanel.Children.Add(addBtn);
            mainStack.Children.Add(emptyPanel);
        }
        else
        {
            // Grid of children
            int colCount = Math.Clamp(children.Count, 2, 5);
            var panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                MaxWidth = colCount * (ChildButtonSize + 16) + 8,
            };

            var fanOffset = FanOffset(edge);
            int index = 0;
            foreach (var child in children)
            {
                var btn = CreateChildButton(child);
                panel.Children.Add(btn);
                int idx = index++;
                btn.Loaded += (_, _) => Motion.FanOut(btn, fanOffset, idx, staggerMs: 40, durationMs: 260);
            }

            mainStack.Children.Add(panel);
        }

        frame.Child = mainStack;

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

        PopupPlacement.PlacePopup(_fanPopup, this, edge, gap: 4);
        Motion.PopIn(frame, PopupPlacement.EnterOffset(edge));
        _fanInteraction = true;
        _host.BeginInteraction();
        GlobalPopupDismissHook.RegisterPopup(_fanPopup);
        _fanPopup.IsOpen = true;
    }

    private static Vector FanOffset(DockEdge edge) => edge switch
    {
        DockEdge.Top => new Vector(0, -16),
        DockEdge.Left => new Vector(-16, 0),
        DockEdge.Right => new Vector(16, 0),
        _ => new Vector(0, 16),
    };

    private FrameworkElement CreateChildButton(DockItem child)
    {
        var icon = new Image
        {
            Width = 28,
            Height = 28,
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
            Width = 36,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        hover.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");

        string title = child.Kind == DockItemKind.App
            ? (!string.IsNullOrWhiteSpace(child.Name) ? child.Name! : Path.GetFileNameWithoutExtension(child.Path ?? ""))
            : (WidgetRegistry.Find(child.Widget)?.Name ?? child.Widget ?? "Widget");

        var label = new TextBlock
        {
            Text = title,
            FontSize = 9.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = ChildButtonSize + 12,
            Margin = new Thickness(0, 2, 0, 0),
        };
        label.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");

        var stack = new StackPanel
        {
            Width = ChildButtonSize + 12,
            Children = { hover, label },
        };

        var wrapper = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(2),
            Background = Brushes.Transparent,
            Child = stack,
            Margin = new Thickness(3),
            ToolTip = title,
            Cursor = Cursors.Hand,
        };

        wrapper.MouseEnter += (_, _) => Motion.Fade(hover, 1, 100);
        wrapper.MouseLeave += (_, _) => Motion.Fade(hover, 0, 180);

        // Click to launch
        wrapper.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseFan();
            if (child.Kind == DockItemKind.App && child.Path is not null)
                AppLauncher.Launch(child);
            else if (child.Kind == DockItemKind.Widget)
                WidgetItemView.RequestSettings(child);
        };

        // Context menu on child
        var itemMenu = new ContextMenu();
        itemMenu.Items.Add(DockMenu.Item("Open", "\uE768", () =>
        {
            CloseFan();
            if (child.Kind == DockItemKind.App && child.Path is not null)
                AppLauncher.Launch(child);
            else if (child.Kind == DockItemKind.Widget)
                WidgetItemView.RequestSettings(child);
        }));
        itemMenu.Items.Add(DockMenu.Item("Remove from folder", "\uE711", () =>
        {
            CloseFan();
            AppServices.ConfigService.RemoveItem(child.Id);
            RefreshAppearance();
        }));
        itemMenu.Items.Add(DockMenu.Item("Move to dock", "\uE8C8", () =>
        {
            CloseFan();
            AppServices.ConfigService.MoveItem(child.Id, int.MaxValue);
            RefreshAppearance();
        }));
        wrapper.ContextMenu = itemMenu;

        return wrapper;
    }

    public void CloseFan()
    {
        if (_fanPopup is { IsOpen: true })
            _fanPopup.IsOpen = false;
    }

    // ------------------------------------------------------------------ Rename & Color

    public void PromptRename()
    {
        string current = _item.GroupName ?? "Folder";
        string? newName = RenameFolderDialog.Prompt(current, Window.GetWindow(this));
        if (!string.IsNullOrWhiteSpace(newName) && newName != current)
        {
            _item.GroupName = newName;
            RefreshAppearance();
            AppServices.ConfigService.ScheduleSave();
            AppServices.Config.NotifyItemsChanged();
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        CloseFan();
        var menu = ContextMenu;
        menu.Items.Clear();
        menu.Items.Add(DockMenu.Header(_item.GroupName ?? "Folder"));

        menu.Items.Add(DockMenu.Item("Open folder", "\uE8B7", OpenFan));
        menu.Items.Add(DockMenu.Item("Rename folder…", "\uE8AC", PromptRename));

        // Color submenu
        var accents = new[] { "AccentBlueBrush", "AccentGreenBrush", "AccentOrangeBrush", "AccentRedBrush",
            "AccentPurpleBrush", "AccentCyanBrush", "AccentPinkBrush", "AccentYellowBrush" };
        var colorNames = new[] { "Blue", "Green", "Orange", "Red", "Purple", "Cyan", "Pink", "Yellow" };
        menu.Items.Add(DockMenu.Submenu("Folder color", "\uE790", accents.Select((a, i) =>
            DockMenu.Check(colorNames[i], (_item.GroupAccent ?? "AccentBlueBrush") == a, () =>
            {
                _item.GroupAccent = a;
                RefreshAppearance();
                AppServices.ConfigService.ScheduleSave();
                AppServices.Config.NotifyItemsChanged();
            }))));

        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("Add application…", "\uE710", () => App.Instance.ShowAppPicker()));

        var children = _item.Children ?? new List<DockItem>();
        if (children.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            menu.Items.Add(DockMenu.Item("Ungroup all", "\uE8C8", () => AppServices.ConfigService.UngroupAll(_item.Id)));
        }

        menu.Items.Add(DockMenu.Item("Remove folder", "\uE77A", () => AppServices.ConfigService.RemoveItem(_item.Id)));
    }

    // ------------------------------------------------------------------ Drop handling (add items to group)

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DockDragHelper.ItemFormat) ||
            e.Data.GetDataPresent(DockDragHelper.RunningAppFormat) ||
            e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            Motion.Fade(_hover, 1, 100);
            AnimatePress(1.12);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DockDragHelper.ItemFormat) ||
            e.Data.GetDataPresent(DockDragHelper.RunningAppFormat) ||
            e.Data.GetDataPresent(DataFormats.FileDrop))
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

        var config = AppServices.ConfigService;

        if (e.Data.GetData(DockDragHelper.ItemFormat) is string itemId)
        {
            e.Handled = true;
            if (itemId == _item.Id) return;
            var existing = config.FindItem(itemId);
            if (existing is null || existing.Kind == DockItemKind.Group) return;

            config.RemoveItem(itemId);
            config.AddToGroup(_item.Id, existing);
            RefreshAppearance();
        }
        else if (e.Data.GetData(DockDragHelper.RunningAppFormat) is string key &&
                 App.Instance.Shell?.RunningApps.Find(key) is { } group &&
                 AppLauncher.PinnablePath(group) is { } path)
        {
            e.Handled = true;
            config.AddToGroup(_item.Id, DockItem.App(path, group.Title));
            RefreshAppearance();
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            e.Handled = true;
            foreach (var file in files.Where(f => !string.IsNullOrWhiteSpace(f)))
                config.AddToGroup(_item.Id, DockItem.App(file));
            RefreshAppearance();
        }
    }

    public void Detach()
    {
        CloseFan();
    }
}
