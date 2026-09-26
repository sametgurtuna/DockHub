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
/// A sleek, macOS Stacks-style folder dock item.
/// Closed: 38x38 frosted glass tile with an accent border; shows 2x2 child icons or a folder glyph when empty.
/// Open: A fluid glass popup with header, item count, staggered app grid, color picker, and quick actions.
/// </summary>
public sealed class GroupItemView : Grid
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

    // ------------------------------------------------------------------ Popup handling

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        AnimatePress(IsMouseOver ? 1.08 : 1);
        if (DockDragHelper.JustDragged) return;
        e.Handled = true;

        if (_fanPopup?.IsOpen == true || (_fanPopup is not null && PopupAnimationHelper.IsClosing(_fanPopup)))
        {
            CloseFan();
        }
        else if (DateTime.UtcNow - _fanClosedAt > TimeSpan.FromMilliseconds(180) && (_fanPopup is null || !PopupAnimationHelper.IsClosing(_fanPopup)))
        {
            OpenFan();
        }
    }

    public void OpenFan()
    {
        try
        {
            if (_fanPopup is { IsOpen: true })
            {
                _fanPopup.IsOpen = false;
            }

            var children = _item.Children ?? new List<DockItem>();
            var edge = _host.Edge;
            string folderName = string.IsNullOrWhiteSpace(_item.GroupName) ? "Folder" : _item.GroupName!;

            var accentBrush = Application.Current.TryFindResource(_item.GroupAccent ?? "AccentBlueBrush") as Brush
                              ?? Brushes.DodgerBlue;
            var textPrimary = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.White;
            var textSecondary = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
            var textTertiary = Application.Current.TryFindResource("TextTertiaryBrush") as Brush ?? Brushes.DarkGray;

            // Container frame
            var frame = new Border
            {
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(8),
                MinWidth = 200,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 5,
                    Opacity = 0.5,
                },
            };
            frame.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            frame.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");

            var mainStack = new StackPanel();

            // 1. Header (Folder icon, title, item count pill, rename & close buttons)
            var headerGrid = new Grid { Margin = new Thickness(2, 0, 2, 10) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var folderGlyph = new TextBlock
            {
                Text = "\uE8B7",
                FontFamily = GetIconFont(),
                FontSize = 16,
                Foreground = accentBrush,
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var titleText = new TextBlock
            {
                Text = folderName,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = textPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 180,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var countPill = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            countPill.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");
            var countText = new TextBlock
            {
                Text = $"{children.Count} item{(children.Count == 1 ? "" : "s")}",
                FontSize = 10.5,
                Foreground = textSecondary,
            };
            countPill.Child = countText;

            titleRow.Children.Add(folderGlyph);
            titleRow.Children.Add(titleText);
            titleRow.Children.Add(countPill);
            Grid.SetColumn(titleRow, 0);
            headerGrid.Children.Add(titleRow);

            // Action icons on header: Rename & Close
            var headerActions = new StackPanel { Orientation = Orientation.Horizontal };

            var renameBtn = new Button
            {
                Content = "\uE8AC",
                FontFamily = GetIconFont(),
                FontSize = 12,
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                ToolTip = "Rename folder",
                Background = Brushes.Transparent,
                Foreground = textSecondary,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
            };
            renameBtn.Click += (_, _) =>
            {
                CloseFan();
                PromptRename();
            };
            headerActions.Children.Add(renameBtn);

            var closeBtn = new Button
            {
                Content = "\uE711",
                FontFamily = GetIconFont(),
                FontSize = 11,
                Width = 24,
                Height = 24,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(0),
                ToolTip = "Close",
                Background = Brushes.Transparent,
                Foreground = textSecondary,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
            };
            closeBtn.Click += (_, _) => CloseFan();
            headerActions.Children.Add(closeBtn);

            Grid.SetColumn(headerActions, 1);
            headerGrid.Children.Add(headerActions);
            mainStack.Children.Add(headerGrid);

            // Divider
            var divider = new Border
            {
                Height = 1,
                Margin = new Thickness(0, 0, 0, 10),
                Opacity = 0.4,
            };
            divider.SetResourceReference(Border.BackgroundProperty, "SurfaceBorderBrush");
            mainStack.Children.Add(divider);

            // 2. Content Area
            if (children.Count == 0)
            {
                var emptyPanel = new StackPanel
                {
                    Margin = new Thickness(8, 12, 8, 14),
                    HorizontalAlignment = HorizontalAlignment.Center,
                };

                var emptyMsg = new TextBlock
                {
                    Text = "This folder is empty",
                    FontSize = 13,
                    FontWeight = FontWeights.Medium,
                    Foreground = textPrimary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4),
                };
                var emptyHint = new TextBlock
                {
                    Text = "Drag apps or widgets onto this folder to group them.",
                    FontSize = 11,
                    Foreground = textSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 14),
                };

                var addAppBtn = new Button
                {
                    Content = "+ Add application…",
                    Height = 32,
                    Padding = new Thickness(16, 0, 16, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Background = accentBrush,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                };
                addAppBtn.Click += (_, _) =>
                {
                    CloseFan();
                    App.Instance.ShowAppPicker();
                };

                emptyPanel.Children.Add(emptyMsg);
                emptyPanel.Children.Add(emptyHint);
                emptyPanel.Children.Add(addAppBtn);
                mainStack.Children.Add(emptyPanel);
            }
            else
            {
                int cols = Math.Clamp(children.Count, 2, 5);
                var itemsPanel = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    MaxWidth = cols * 64 + 10,
                };

                foreach (var child in children)
                {
                    var btn = CreateChildButton(child);
                    itemsPanel.Children.Add(btn);
                }

                mainStack.Children.Add(itemsPanel);
            }

            // 3. Footer Bar with Quick Colors & Manage
            var footerDivider = new Border
            {
                Height = 1,
                Margin = new Thickness(0, 10, 0, 8),
                Opacity = 0.35,
            };
            footerDivider.SetResourceReference(Border.BackgroundProperty, "SurfaceBorderBrush");
            mainStack.Children.Add(footerDivider);

            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Color dots
            var colorDots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var (cName, cKey, cVal) in AccentColors)
            {
                bool isSelected = (_item.GroupAccent ?? "AccentBlueBrush") == cKey;
                var dotBorder = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 0, 5, 0),
                    Background = new SolidColorBrush(cVal),
                    BorderBrush = isSelected ? textPrimary : Brushes.Transparent,
                    BorderThickness = new Thickness(isSelected ? 2 : 0),
                    ToolTip = cName,
                    Cursor = Cursors.Hand,
                };
                string targetKey = cKey;
                dotBorder.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    _item.GroupAccent = targetKey;
                    RefreshAppearance();
                    AppServices.ConfigService.ScheduleSave();
                    AppServices.Config.NotifyItemsChanged();
                    OpenFan(); // Refresh open popup with new color
                };
                colorDots.Children.Add(dotBorder);
            }
            Grid.SetColumn(colorDots, 0);
            footerGrid.Children.Add(colorDots);

            // Add App button
            var addMoreBtn = new Button
            {
                Content = "+ Add App",
                FontSize = 10.5,
                Padding = new Thickness(8, 3, 8, 3),
                Background = Brushes.Transparent,
                Foreground = textSecondary,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
            };
            addMoreBtn.Click += (_, _) =>
            {
                CloseFan();
                App.Instance.ShowAppPicker();
            };
            Grid.SetColumn(addMoreBtn, 1);
            footerGrid.Children.Add(addMoreBtn);

            mainStack.Children.Add(footerGrid);
            frame.Child = mainStack;

            _fanPopup = new Popup
            {
                Child = frame,
                AllowsTransparency = true,
                StaysOpen = true,
                PopupAnimation = PopupAnimation.None,
                PlacementTarget = this,
            };
            _fanPopup.Closed += (_, _) =>
            {
                _fanClosedAt = DateTime.UtcNow;
                if (_fanInteraction)
                {
                    _fanInteraction = false;
                    _host.EndInteraction();
                }
            };

            PopupPlacement.PlacePopup(_fanPopup, this, edge, gap: 4);
            _fanInteraction = true;
            _host.BeginInteraction();
            GlobalPopupDismissHook.RegisterPopup(_fanPopup);
            PopupAnimationHelper.AnimateOpen(_fanPopup, edge, this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open folder fan popup");
        }
    }

    private FrameworkElement CreateChildButton(DockItem child)
    {
        var icon = new Image
        {
            Width = 32,
            Height = 32,
            Stretch = Stretch.Uniform,
            Source = GetChildIcon(child),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        var iconContainer = new Grid
        {
            Width = 46,
            Height = 46,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var hover = new Border
        {
            CornerRadius = new CornerRadius(10),
            Opacity = 0,
        };
        hover.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");

        iconContainer.Children.Add(hover);
        iconContainer.Children.Add(icon);

        string title = child.Kind == DockItemKind.App
            ? (!string.IsNullOrWhiteSpace(child.Name) ? child.Name! : Path.GetFileNameWithoutExtension(child.Path ?? ""))
            : (WidgetRegistry.Find(child.Widget)?.Name ?? child.Widget ?? "Widget");

        var textSecondary = Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
        var label = new TextBlock
        {
            Text = title,
            FontSize = 10,
            Foreground = textSecondary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 58,
            Margin = new Thickness(0, 3, 0, 0),
        };

        var stack = new StackPanel
        {
            Width = 60,
            Children = { iconContainer, label },
        };

        var wrapper = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(2, 4, 2, 4),
            Background = Brushes.Transparent,
            Child = stack,
            Margin = new Thickness(2),
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
        if (_fanPopup is { IsOpen: true } && !PopupAnimationHelper.IsClosing(_fanPopup))
            PopupAnimationHelper.ClosePopup(_fanPopup, _host.Edge, this);
    }

    // ------------------------------------------------------------------ Rename & Context Menu

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
        menu.Items.Add(DockMenu.Submenu("Folder color", "\uE790", AccentColors.Select(ac =>
            DockMenu.Check(ac.Name, (_item.GroupAccent ?? "AccentBlueBrush") == ac.BrushKey, () =>
            {
                _item.GroupAccent = ac.BrushKey;
                RefreshAppearance();
                AppServices.ConfigService.ScheduleSave();
                AppServices.Config.NotifyItemsChanged();
            }))));

        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item("Add application…", "\uE710", () => App.Instance.ShowAppPicker(_item.Id)));

        var children = _item.Children ?? new List<DockItem>();
        if (children.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            menu.Items.Add(DockMenu.Item("Ungroup all", "\uE8C8", () => AppServices.ConfigService.UngroupAll(_item.Id)));
        }

        menu.Items.Add(DockMenu.Item("Remove folder…", "\uE77A", () => ConfirmRemoveFolder(_item, Window.GetWindow(this))));
    }

    /// <summary>
    /// Removing a folder with items asks what to do with them; moving them back to the dock is the default,
    /// so a stray click never deletes apps and widgets (with their settings) at once.
    /// </summary>
    public static void ConfirmRemoveFolder(DockItem folder, Window? owner)
    {
        var service = AppServices.ConfigService;
        int count = folder.Children?.Count ?? 0;
        if (count == 0)
        {
            service.RemoveItem(folder.Id);
            return;
        }

        string? choice = ConfirmDialog.Show(
            $"Remove “{folder.GroupName ?? "Folder"}”",
            count == 1 ? "This folder contains 1 item." : $"This folder contains {count} items.",
            "", owner,
            new DialogButton("cancel", "Cancel", IsCancel: true),
            new DialogButton("delete", "Delete all", DialogButtonKind.Danger),
            new DialogButton("move", "Move items to dock", DialogButtonKind.Primary));

        switch (choice)
        {
            case "move":
                service.UngroupAll(folder.Id);
                break;
            case "delete":
                service.RemoveItem(folder.Id);
                break;
        }
    }

    // ------------------------------------------------------------------ Drag and Drop handling

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
                 AppLauncher.PinItem(group) is { } pinItem)
        {
            e.Handled = true;
            config.AddToGroup(_item.Id, pinItem);
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
