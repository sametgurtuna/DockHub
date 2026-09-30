using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Widgets;

namespace CustomDock.Dock;

public sealed partial class GroupItemView
{
    // ------------------------------------------------------------------ Popup handling

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        AnimatePress(IsMouseOver ? 1.08 : 1);
        if (DockDragHelper.JustDragged || DockWindow.IsEditingAt(this)) return;
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
                    App.Instance.ShowAppPicker(_item.Id);
                };

                emptyPanel.Children.Add(emptyMsg);
                emptyPanel.Children.Add(emptyHint);
                emptyPanel.Children.Add(addAppBtn);
                mainStack.Children.Add(emptyPanel);
            }
            else
            {
                // Large folders become a scrollable grid with a search box.
                int cols = Math.Clamp(children.Count, 2, children.Count > 12 ? 6 : 5);
                var itemsPanel = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    MaxWidth = cols * 64 + 10,
                };

                var buttons = new List<(DockItem Child, FrameworkElement Button)>();
                foreach (var child in children)
                {
                    var btn = CreateChildButton(child);
                    buttons.Add((child, btn));
                    itemsPanel.Children.Add(btn);
                }

                if (children.Count > 8)
                {
                    var search = new TextBox
                    {
                        Margin = new Thickness(4, 0, 4, 8),
                        MinHeight = 28,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        ToolTip = L.T("Filter this folder"),
                    };
                    search.TextChanged += (_, _) =>
                    {
                        string query = search.Text.Trim();
                        foreach (var (child, button) in buttons)
                        {
                            string name = child.Kind == DockItemKind.App
                                ? (!string.IsNullOrWhiteSpace(child.Name) ? child.Name! : Path.GetFileNameWithoutExtension(child.Path ?? ""))
                                : WidgetRegistry.Find(child.Widget)?.Name ?? "";
                            button.Visibility = query.Length == 0 || name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                                ? Visibility.Visible : Visibility.Collapsed;
                        }
                    };
                    mainStack.Children.Add(search);
                    // The dock doesn't take keyboard input by default; let it while the search box is used.
                    search.PreviewMouseLeftButtonDown += (_, _) => (Window.GetWindow(this) as DockWindow)?.ActivateForInput();
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                    {
                        (Window.GetWindow(this) as DockWindow)?.ActivateForInput();
                        search.Focus();
                    });
                }

                mainStack.Children.Add(new ScrollViewer
                {
                    Content = itemsPanel,
                    MaxHeight = 4 * 84,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Focusable = false,
                });
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
                ActivateChild(child);
            else if (child.Kind == DockItemKind.Widget)
                WidgetItemView.RequestSettings(child);
        };

        // Context menu on child
        var itemMenu = new ContextMenu();
        itemMenu.Items.Add(DockMenu.Item("Open", "\uE768", () =>
        {
            CloseFan();
            if (child.Kind == DockItemKind.App && child.Path is not null)
                ActivateChild(child);
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
}
