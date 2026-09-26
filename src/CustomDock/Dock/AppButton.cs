using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Services;
using CustomDock.Shell;
using ManagedShell.WindowsTasks;

namespace CustomDock.Dock;

/// <summary>
/// Application button on the dock: pinned item and/or running window group.
/// Indicators: running dot, active window bar, attention (flashing), progress bar, badge, live preview.
/// </summary>
public sealed class AppButton : Grid
{
    private const double IconSize = 30;

    private static readonly Regex BadgeRx = new(
        @"(?:^|[\(\[])\s*(\d{1,4}\+?)\s*(?:[\)\]]|$)|[\(\[]\s*(\d{1,4}\+?)\s*[\)\]]",
        RegexOptions.Compiled);

    private readonly Border _hover;
    private readonly Image _icon;
    private readonly Image _overlay;
    private readonly Border _badgeBorder;
    private readonly TextBlock _badgeText;
    private readonly Border _indicator;
    private readonly Grid _progressTrack;
    private readonly Border _progressFill;
    private readonly ScaleTransform _pressScale = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _dragActivateTimer;
    private AppGroup? _group;

    public AppButton(DockItem? item, AppGroup? group)
    {
        Item = item;
        Width = 44;
        Height = 46;
        Margin = new Thickness(1, 0, 1, 0);
        Background = Brushes.Transparent;
        Focusable = false;
        AllowDrop = true;
        ToolTipService.SetInitialShowDelay(this, 450);

        _hover = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(1, 3, 1, 3), Opacity = 0 };
        _hover.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");

        _icon = new Image
        {
            Width = IconSize,
            Height = IconSize,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 2),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _pressScale,
        };
        RenderOptions.SetBitmapScalingMode(_icon, BitmapScalingMode.HighQuality);

        _overlay = new Image
        {
            Width = 15,
            Height = 15,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 5, 7),
        };
        RenderOptions.SetBitmapScalingMode(_overlay, BitmapScalingMode.HighQuality);

        // Notification badge (Red pill / circle)
        _badgeBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 59, 48)), // Vibrant notification red
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            MinHeight = 16,
            MinWidth = 16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 1, 0),
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(3.5, 0, 3.5, 0),
        };
        _badgeText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Typography = { NumeralAlignment = FontNumeralAlignment.Tabular },
        };
        _badgeBorder.Child = _badgeText;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            ShowThumbnailPreview();
        };

        _indicator = new Border
        {
            Height = 3,
            Width = 5,
            CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2),
            Visibility = Visibility.Collapsed,
        };

        _progressFill = new Border { CornerRadius = new CornerRadius(1), HorizontalAlignment = HorizontalAlignment.Left };
        _progressTrack = new Grid
        {
            Height = 3,
            Width = 26,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };
        _progressTrack.Children.Add(new Border { CornerRadius = new CornerRadius(1) }.WithResource(Border.BackgroundProperty, "TrackBrush"));
        _progressTrack.Children.Add(_progressFill);

        Children.Add(_hover);
        Children.Add(_icon);
        Children.Add(_overlay);
        Children.Add(_badgeBorder);
        Children.Add(_progressTrack);
        Children.Add(_indicator);

        MouseEnter += (_, _) =>
        {
            Motion.Fade(_hover, 1, 120);
            AnimatePress(HoverScale);
            if (_group is { WindowCount: > 0 })
            {
                if (WindowPreviewWindow.Instance.IsVisible)
                    ShowThumbnailPreview();
                else
                    _previewTimer.Start();
            }
            else
            {
                _previewTimer.Stop();
                WindowPreviewWindow.Instance.HidePreview();
            }
        };
        MouseLeave += (_, _) =>
        {
            Motion.Fade(_hover, 0, 220);
            AnimatePress(1);
            _previewTimer.Stop();
            WindowPreviewWindow.Instance.ScheduleHide(100);
        };
        MouseLeftButtonDown += (_, _) => AnimatePress(0.86);
        Loaded += OnFirstLoaded;
        MouseLeftButtonUp += OnLeftUp;
        MouseDown += OnMiddleDown;
        ContextMenu = new ContextMenu();
        ContextMenuOpening += OnContextMenuOpening;

        _dragActivateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _dragActivateTimer.Tick += (_, _) =>
        {
            _dragActivateTimer.Stop();
            if (_group is { WindowCount: > 0 })
            {
                var win = _group.PrimaryWindow ?? _group.Windows.FirstOrDefault();
                if (win is not null)
                {
                    if (win.IsMinimized) win.Restore();
                    win.BringToFront();
                }
            }
        };

        DragEnter += OnFileDragEnter;
        DragOver += OnFileDragOver;
        DragLeave += OnFileDragLeave;
        Drop += OnFileDrop;

        LoadPinnedIcon();
        Group = group;
    }

    public DockItem? Item { get; }

    public bool IsPinned => Item is not null;

    public AppGroup? Group
    {
        get => _group;
        set
        {
            if (ReferenceEquals(_group, value)) return;
            if (_group is not null) _group.PropertyChanged -= OnGroupChanged;
            _group = value;
            if (_group is not null) _group.PropertyChanged += OnGroupChanged;
            Refresh();
        }
    }

    public string Title
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Item?.Name)) return Item!.Name!;
            if (_group is not null && !string.IsNullOrWhiteSpace(_group.Title)) return _group.Title;
            if (Item?.Path is { } path)
            {
                if (path.StartsWith(AppKeys.AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
                    return path[AppKeys.AppsFolderPrefix.Length..].Split('!')[0].Split('_')[0];
                return Path.GetFileNameWithoutExtension(path);
            }
            return "";
        }
    }

    // ------------------------------------------------------------------ Pinned icon (with retries)

    /// <summary>
    /// Delays between attempts when a pinned app's icon can't be loaded (shell not ready at sign-in,
    /// file being replaced by an updater). Until then the generic app icon is shown instead of an empty button.
    /// </summary>
    private static readonly int[] IconRetryDelaysMs = { 1000, 3000, 10000, 30000, 120000 };
    private DispatcherTimer? _iconRetryTimer;
    private int _iconRetryAttempt;
    private bool _iconIsFallback;

    private void LoadPinnedIcon()
    {
        if (Item is null) return;
        _icon.Source = AppIcons.For(Item, 96, out _iconIsFallback);
        _iconRetryAttempt = 0;
        if (_iconIsFallback) ScheduleIconRetry();
    }

    /// <summary>Retries a missing pinned icon now (after resume, display or Explorer changes).</summary>
    public void RefreshIcon()
    {
        if (Item is null || !_iconIsFallback) return;
        _iconRetryAttempt = 0;
        RetryIcon();
    }

    private void ScheduleIconRetry()
    {
        if (_iconRetryAttempt >= IconRetryDelaysMs.Length)
        {
            Log.Debug($"Icon still missing after {IconRetryDelaysMs.Length} retries: {Item?.Path}");
            return;
        }
        _iconRetryTimer ??= CreateIconRetryTimer();
        _iconRetryTimer.Interval = TimeSpan.FromMilliseconds(IconRetryDelaysMs[_iconRetryAttempt++]);
        _iconRetryTimer.Start();
    }

    private DispatcherTimer CreateIconRetryTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RetryIcon();
        };
        return timer;
    }

    private void RetryIcon()
    {
        _iconRetryTimer?.Stop();
        if (Item is null) return;
        AppIcons.Invalidate(Item);
        if (AppIcons.TryFor(Item, 96) is { } image)
        {
            _icon.Source = image;
            _iconIsFallback = false;
            Log.Debug($"Icon recovered: {Item.Path}");
            return;
        }
        ScheduleIconRetry();
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(Refresh);

    public void Refresh()
    {
        var group = _group;
        bool running = group is { WindowCount: > 0 };

        if (Item is null || _icon.Source is null)
            _icon.Source = group?.Icon ?? _icon.Source;
        else if (_iconIsFallback && group?.Icon is { } groupIcon && !ReferenceEquals(groupIcon, ShellIcons.GetDefaultAppIcon()))
            _icon.Source = groupIcon; // Pinned icon unavailable: use the running window's icon meanwhile.

        _overlay.Source = group?.OverlayIcon;
        _overlay.Visibility = group?.OverlayIcon is null ? Visibility.Collapsed : Visibility.Visible;

        if (!running)
        {
            _indicator.Visibility = Visibility.Collapsed;
        }
        else
        {
            _indicator.Visibility = Visibility.Visible;
            string brush = group!.IsFlashing ? "AccentOrangeBrush" : group.IsActive ? "ActiveIndicatorBrush" : "IndicatorBrush";
            _indicator.SetResourceReference(Border.BackgroundProperty, brush);
            double width = group.IsActive || group.IsFlashing ? 14 : group.WindowCount > 1 ? 9 : 5;
            _indicator.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }

        bool progress = group is { HasProgress: true };
        _progressTrack.Visibility = progress ? Visibility.Visible : Visibility.Collapsed;
        if (progress)
        {
            _progressFill.Width = group!.ProgressIndeterminate ? 26 : 26 * group.Progress;
            _progressFill.SetResourceReference(Border.BackgroundProperty,
                group.ProgressError ? "AccentOrangeBrush" : group.ProgressIndeterminate ? "TextSecondaryBrush" : "AccentGreenBrush");
        }

        UpdateBadge(group);

        // Live thumbnail preview already shows open windows; clean tooltip
        ToolTip = group is { WindowCount: > 0 } ? null : Title;
    }

    private void ShowThumbnailPreview()
    {
        if (_group is not { WindowCount: > 0 }) return;
        WindowPreviewWindow.Instance.ShowFor(this, _group, AppServices.Config.Edge);
    }

    private void UpdateBadge(AppGroup? group)
    {
        if (group is null || group.WindowCount == 0)
        {
            _badgeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        string? badgeText = null;
        bool hasDot = false;

        foreach (var window in group.Windows)
        {
            if (string.IsNullOrWhiteSpace(window.Title)) continue;

            var match = BadgeRx.Match(window.Title);
            if (match.Success)
            {
                badgeText = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value;
                break;
            }

            if (window.Title.StartsWith("•") || window.Title.StartsWith("*") || window.Title.Contains(" • ") || window.Title.Contains(" * "))
            {
                hasDot = true;
            }
        }

        if (!string.IsNullOrEmpty(badgeText))
        {
            _badgeText.Text = badgeText;
            _badgeText.Visibility = Visibility.Visible;
            _badgeBorder.MinWidth = 16;
            _badgeBorder.MinHeight = 16;
            _badgeBorder.CornerRadius = new CornerRadius(8);
            _badgeBorder.Padding = new Thickness(3.5, 0, 3.5, 0);
            _badgeBorder.Visibility = Visibility.Visible;
        }
        else if (hasDot || (group.OverlayIcon is not null && _overlay.Source is null))
        {
            _badgeText.Text = "";
            _badgeText.Visibility = Visibility.Collapsed;
            _badgeBorder.MinWidth = 10;
            _badgeBorder.MinHeight = 10;
            _badgeBorder.CornerRadius = new CornerRadius(5);
            _badgeBorder.Padding = new Thickness(0);
            _badgeBorder.Visibility = Visibility.Visible;
        }
        else
        {
            _badgeBorder.Visibility = Visibility.Collapsed;
        }
    }

    private static string Trim(string text, int max) => text.Length > max ? text[..(max - 1)] + "…" : text;

    private const double HoverScale = 1.08;

    private void AnimatePress(double scale)
    {
        IEasingFunction easing = scale < 1
            ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
        Motion.Scale(_pressScale, scale, scale < 1 ? 90 : 260, easing);
    }

    /// <summary>Newly added dock button animates in from small to full size.</summary>
    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        Motion.Appear(this);
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        AnimatePress(IsMouseOver ? HoverScale : 1);
        if (DockDragHelper.JustDragged) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && Item is not null)
        {
            AppLauncher.Launch(Item, newInstance: true);
            return;
        }
        AppLauncher.Activate(Item, _group);
    }

    private void OnMiddleDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        _previewTimer.Stop();
        WindowPreviewWindow.Instance.HidePreview();
        StartNewInstance();
    }

    private void StartNewInstance()
    {
        if (Item is not null)
            AppLauncher.Launch(Item, newInstance: true);
        else if (_group is not null && AppLauncher.PinnablePath(_group) is { } path)
            AppLauncher.Launch(path, newInstance: true);
    }

    private string? LaunchPath => Item?.Path ?? (_group is null ? null : AppLauncher.PinnablePath(_group));

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e) => BuildContextMenu();

    // ------------------------------------------------------------------ Keyboard (Win+number, dock focus)

    /// <summary>Runs what Win+number (with modifiers) does on the Windows taskbar.</summary>
    public void InvokeShortcut(AppShortcutMode mode)
    {
        switch (mode)
        {
            case AppShortcutMode.Activate:
                AppLauncher.Activate(Item, _group);
                break;
            case AppShortcutMode.NewInstance:
                StartNewInstance();
                break;
            case AppShortcutMode.RunAsAdmin:
                if (LaunchPath is { } path && !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    AppLauncher.RunAsAdmin(path);
                break;
            case AppShortcutMode.JumpList:
                OpenContextMenu();
                break;
        }
        AnimatePress(0.86);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => AnimatePress(1));
    }

    /// <summary>Opens the right-click menu from the keyboard.</summary>
    public void OpenContextMenu()
    {
        BuildContextMenu();
        ContextMenu.PlacementTarget = this;
        ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        ContextMenu.IsOpen = true;
    }

    private Border? _numberBadge;

    /// <summary>Shows (or with null hides) the Win+number hint on the button.</summary>
    public void ShowShortcutNumber(string? number)
    {
        if (number is null)
        {
            if (_numberBadge is not null) _numberBadge.Visibility = Visibility.Collapsed;
            return;
        }
        if (_numberBadge is null)
        {
            var text = new TextBlock
            {
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            _numberBadge = new Border
            {
                MinWidth = 16,
                Height = 16,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(2, 2, 0, 0),
                Child = text,
                IsHitTestVisible = false,
            };
            _numberBadge.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
            _numberBadge.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
            _numberBadge.BorderThickness = new Thickness(1);
            Children.Add(_numberBadge);
        }
        ((TextBlock)_numberBadge.Child).Text = number;
        _numberBadge.Visibility = Visibility.Visible;
    }

    private void BuildContextMenu()
    {
        WindowPreviewWindow.Instance.HidePreview();
        _previewTimer.Stop();

        var menu = ContextMenu;
        menu.Items.Clear();
        menu.Items.Add(DockMenu.Header(Title));

        var windows = _group?.Windows ?? new List<ApplicationWindow>();
        if (windows.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            foreach (var window in windows.Take(12))
            {
                var w = window;
                string title = string.IsNullOrWhiteSpace(w.Title) ? Title : Trim(w.Title, 50);
                var item = DockMenu.Item(title, null, () =>
                {
                    if (w.IsMinimized) w.Restore();
                    w.BringToFront();
                });
                item.FontWeight = w.State == ApplicationWindow.WindowState.Active ? FontWeights.SemiBold : FontWeights.Normal;
                menu.Items.Add(item);
            }
        }

        string? resolvedExe = null;
        if (LaunchPath is { } lPath && !lPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            resolvedExe = lPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? lPath : ShellIcons.ResolveShortcut(lPath);

        // --- Jump List (Tasks) ---
        var tasks = JumpListService.GetTasks(resolvedExe);
        if (tasks.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            foreach (var task in tasks)
            {
                menu.Items.Add(DockMenu.Item(task.Title, task.Glyph, task.Action));
            }
        }

        // --- Jump List (Recent Items) ---
        var recentItems = JumpListService.GetRecentItems(resolvedExe);
        if (recentItems.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            foreach (var recent in recentItems)
            {
                var r = recent;
                var item = DockMenu.Item(r.Title, null, () =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(r.Path) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Failed to open recent item: {r.Path}");
                    }
                });
                if (r.Icon is not null)
                {
                    item.Icon = new Image { Source = r.Icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) };
                }
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item(windows.Count > 0 ? "New window" : "Open", "\uE8A7", StartNewInstance, LaunchPath is not null));

        if (resolvedExe is not null && resolvedExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            menu.Items.Add(DockMenu.Item("Run as administrator", "\uE7EF", () => AppLauncher.RunAsAdmin(resolvedExe)));
            menu.Items.Add(DockMenu.Item("Open file location", "\uE8B7", () => AppLauncher.OpenLocation(resolvedExe)));
        }

        menu.Items.Add(DockMenu.Separator());
        if (Item is not null)
        {
            menu.Items.Add(DockMenu.Item("Unpin from dock", "\uE77A", () => AppServices.ConfigService.RemoveItem(Item.Id)));
        }
        else if (_group is not null && AppLauncher.PinItem(_group) is { } pinItem)
        {
            menu.Items.Add(DockMenu.Item(AppInfo.PinLabel, "\uE718", () =>
                AppServices.ConfigService.AddItem(pinItem, DockItemsIndex.EndOfApps())));
        }

        if (windows.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            menu.Items.Add(DockMenu.Item(windows.Count > 1 ? $"Close all windows ({windows.Count})" : "Close window", "\uE711",
                () => { foreach (var w in windows.ToList()) w.Close(); }));
            menu.Items.Add(DockMenu.Item(windows.Count > 1 ? "End processes" : "End process", "\uE9CE",
                () => TerminateWindows(windows.ToList())));
        }
    }

    /// <summary>Immediately and forcefully terminates processes owning the windows (like Task Manager "End Task").</summary>
    private static void TerminateWindows(IReadOnlyList<ApplicationWindow> windows)
    {
        foreach (var w in windows)
        {
            try
            {
                if (w.Handle != IntPtr.Zero)
                    NativeMethods.EndTask(w.Handle, false, true);
            }
            catch { /* ignore */ }
        }

        var pids = new HashSet<uint>();
        foreach (var w in windows)
        {
            uint pid = w.ProcId ?? 0;
            if (pid == 0)
                NativeMethods.GetWindowThreadProcessId(w.Handle, out pid);

            if (pid > 4 && pid != (uint)Environment.ProcessId)
            {
                pids.Add(pid);
            }
        }

        _ = Task.Run(() =>
        {
            foreach (uint pid in pids)
            {
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    if (process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                        continue;

                    process.Kill(true);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Process {pid} could not be terminated directly, trying taskkill: {ex.Message}");
                    try
                    {
                        using var p = Process.Start(new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = $"/F /T /PID {pid}",
                            CreateNoWindow = true,
                            UseShellExecute = false,
                        });
                        p?.WaitForExit(1000);
                    }
                    catch (Exception taskKillEx)
                    {
                        Log.Error(taskKillEx, $"taskkill failed for PID {pid}");
                    }
                }
            }
        });
    }

    private void OnFileDragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        Motion.Fade(_hover, 1, 100);
        AnimatePress(HoverScale);
        _dragActivateTimer.Stop();
        _dragActivateTimer.Start();
    }

    private void OnFileDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnFileDragLeave(object sender, DragEventArgs e)
    {
        _dragActivateTimer.Stop();
        Motion.Fade(_hover, 0, 160);
        AnimatePress(1);
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        _dragActivateTimer.Stop();
        Motion.Fade(_hover, 0, 160);
        AnimatePress(1);

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        e.Handled = true;
        string? targetPath = LaunchPath;
        if (string.IsNullOrWhiteSpace(targetPath)) return;

        string? exeToRun = targetPath;
        if (exeToRun.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            exeToRun = ShellIcons.ResolveShortcut(exeToRun) ?? exeToRun;

        string args = string.Join(" ", files.Select(f => $"\"{f}\""));
        AppLauncher.Launch(exeToRun, args);
    }

    public void Detach()
    {
        _previewTimer.Stop();
        _dragActivateTimer.Stop();
        _iconRetryTimer?.Stop();
        if (_group is not null) _group.PropertyChanged -= OnGroupChanged;
    }
}

/// <summary>Position for newly pinned applications: immediately after the last app item.</summary>
public static class DockItemsIndex
{
    public static int EndOfApps()
    {
        var items = AppServices.Config.Items;
        int last = items.FindLastIndex(i => i.Kind == DockItemKind.App);
        return last < 0 ? 0 : last + 1;
    }
}

internal static class ElementExtensions
{
    public static T WithResource<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }
}
