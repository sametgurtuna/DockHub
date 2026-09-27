using System.ComponentModel;
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

namespace CustomDock.Dock;

/// <summary>
/// Application button on the dock: pinned item and/or running window group.
/// Indicators: running dot, active window bar, attention (flashing), progress bar, badge, live preview.
/// </summary>
public sealed partial class AppButton : Grid
{
    private const double IconSize = 30;

    private readonly Border _hover;
    private readonly Image _icon;
    private readonly Image _overlay;
    private readonly Border _badgeBorder;
    private readonly TextBlock _badgeText;
    private readonly Border _indicator;
    private readonly Grid _progressTrack;
    private readonly Border _progressFill;
    private readonly ScaleTransform _pressScale = new();
    private readonly TranslateTransform _launchOffset = new();
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
            RenderTransform = new TransformGroup { Children = { _pressScale, _launchOffset } },
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

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(Refresh);

    public void Refresh()
    {
        var group = _group;
        bool running = group is { WindowCount: > 0 };
        if (_launching && (group?.WindowCount ?? 0) > _launchBaseline) EndLaunchFeedback();

        if (Item is null || _icon.Source is null)
            _icon.Source = group?.Icon ?? _icon.Source;
        else if (_iconIsFallback && group?.Icon is { } groupIcon && !ReferenceEquals(groupIcon, ShellIcons.GetDefaultAppIcon()))
            _icon.Source = groupIcon; // Pinned icon unavailable: use the running window's icon meanwhile.

        _overlay.Source = group?.OverlayIcon;
        _overlay.Visibility = group?.OverlayIcon is null ? Visibility.Collapsed : Visibility.Visible;

        var style = AppServices.Config.RunningIndicator;
        if (!running || style == RunningIndicatorStyle.Off)
        {
            _indicator.Visibility = Visibility.Collapsed;
            if (_dots is not null) _dots.Visibility = Visibility.Collapsed;
        }
        else
        {
            string brush = group!.IsFlashing ? "AccentOrangeBrush" : group.IsActive ? "ActiveIndicatorBrush" : "IndicatorBrush";
            if (style == RunningIndicatorStyle.Dots)
            {
                // One dot per window (up to three), macOS style.
                _indicator.Visibility = Visibility.Collapsed;
                EnsureDots();
                _dots!.Visibility = Visibility.Visible;
                for (int i = 0; i < _dots.Children.Count; i++)
                {
                    var dot = (System.Windows.Shapes.Ellipse)_dots.Children[i];
                    dot.Visibility = i < Math.Min(group.WindowCount, 3) ? Visibility.Visible : Visibility.Collapsed;
                    dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
                }
            }
            else
            {
                if (_dots is not null) _dots.Visibility = Visibility.Collapsed;
                _indicator.Visibility = Visibility.Visible;
                _indicator.SetResourceReference(Border.BackgroundProperty, brush);
                double width = group.IsActive || group.IsFlashing ? 14 : group.WindowCount > 1 ? 9 : 5;
                _indicator.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            }
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
        System.Windows.Automation.AutomationProperties.SetName(this, Title);
        System.Windows.Automation.AutomationProperties.SetHelpText(this, group is not { WindowCount: > 0 } ? L.T("Not running")
            : group.IsFlashing ? L.T("Needs attention")
            : group.WindowCount == 1 ? L.T("Running") : L.T("Running, {0} windows", group.WindowCount));
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

        var (badgeText, hasDot) = AppButtonBadge.FromTitles(group.Windows.Select(w => w.Title));

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
            BeginLaunchFeedback();
            AppLauncher.Launch(Item, newInstance: true);
            return;
        }
        if (_group is not { WindowCount: > 0 } && Item is not null) BeginLaunchFeedback();
        AppLauncher.Activate(Item, _group);
    }

    // ------------------------------------------------------------------ Launch feedback

    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(12);
    private bool _launching;
    private int _launchBaseline;
    private DispatcherTimer? _launchTimer;

    /// <summary>
    /// The icon bounces (macOS style) until the app's window appears, so a slow app still shows the click landed.
    /// With reduced motion it gently pulses instead; with animations off it just dims.
    /// </summary>
    private void BeginLaunchFeedback()
    {
        _launchBaseline = _group?.WindowCount ?? 0;
        if (_launchTimer is null)
        {
            _launchTimer = new DispatcherTimer { Interval = LaunchTimeout };
            _launchTimer.Tick += (_, _) => EndLaunchFeedback();
        }
        _launchTimer.Stop();
        _launchTimer.Start();
        if (_launching) return;
        _launching = true;

        switch (Motion.Level)
        {
            case MotionLevel.Full:
                var bounce = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                bounce.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                bounce.KeyFrames.Add(new EasingDoubleKeyFrame(-7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)),
                    new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                bounce.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)),
                    new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                bounce.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(760))));
                _launchOffset.BeginAnimation(TranslateTransform.YProperty, bounce);
                break;
            case MotionLevel.Reduced:
                _icon.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(650))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                });
                break;
            default:
                _icon.Opacity = 0.55;
                break;
        }
        System.Windows.Automation.AutomationProperties.SetHelpText(this, L.T("Starting"));
    }

    private void EndLaunchFeedback()
    {
        _launchTimer?.Stop();
        if (!_launching) return;
        _launching = false;
        _launchOffset.BeginAnimation(TranslateTransform.YProperty, null);
        _launchOffset.Y = 0;
        _icon.BeginAnimation(OpacityProperty, null);
        _icon.Opacity = 1;
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
        BeginLaunchFeedback();
        if (Item is not null)
            AppLauncher.Launch(Item, newInstance: true);
        else if (_group is not null && AppLauncher.PinnablePath(_group) is { } path)
            AppLauncher.Launch(path, newInstance: true);
    }

    private string? LaunchPath => Item?.Path ?? (_group is null ? null : AppLauncher.PinnablePath(_group));

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e) => BuildContextMenu();

    public void Detach()
    {
        EndLaunchFeedback();
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
