using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CustomDock.Controls;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Services;
using CustomDock.Shell;
using ManagedShell.WindowsTasks;

namespace CustomDock.Dock;

/// <summary>
/// Dock'taki uygulama düğmesi: sabitlenmiş öğe ve/veya çalışan pencere grubu.
/// Göstergeler: çalışıyor noktası, etkin pencere çizgisi, dikkat (yanıp sönme), ilerleme çubuğu, rozet.
/// </summary>
public sealed class AppButton : Grid
{
    private const double IconSize = 30;

    private readonly Border _hover;
    private readonly Image _icon;
    private readonly Image _overlay;
    private readonly Border _indicator;
    private readonly Grid _progressTrack;
    private readonly Border _progressFill;
    private readonly ScaleTransform _pressScale = new();
    private AppGroup? _group;

    public AppButton(DockItem? item, AppGroup? group)
    {
        Item = item;
        Width = 44;
        Height = 46;
        Margin = new Thickness(1, 0, 1, 0);
        Background = Brushes.Transparent;
        Focusable = false;
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
        Children.Add(_progressTrack);
        Children.Add(_indicator);

        MouseEnter += (_, _) => { Motion.Fade(_hover, 1, 120); AnimatePress(HoverScale); };
        MouseLeave += (_, _) => { Motion.Fade(_hover, 0, 220); AnimatePress(1); };
        MouseLeftButtonDown += (_, _) => AnimatePress(0.86);
        Loaded += OnFirstLoaded;
        MouseLeftButtonUp += OnLeftUp;
        MouseDown += OnMiddleDown;
        ContextMenu = new ContextMenu();
        ContextMenuOpening += OnContextMenuOpening;

        if (item?.Path is { } path)
            _icon.Source = IconFor(path);
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

    private static ImageSource? IconFor(string path)
    {
        // Kısayolun hedefi bir .exe ise onun ikonunu kullan (kısayol oku olmadan, daha net).
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
            ShellIcons.ResolveShortcut(path) is { } target &&
            target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
        {
            return ShellIcons.GetIcon(target, 96) ?? ShellIcons.GetIcon(path, 96);
        }
        return ShellIcons.GetIcon(path, 96);
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(Refresh);

    public void Refresh()
    {
        var group = _group;
        bool running = group is { WindowCount: > 0 };

        if (Item is null || _icon.Source is null)
            _icon.Source = group?.Icon ?? _icon.Source;

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

        var tip = Title;
        if (group is { WindowCount: > 1 })
            tip += "\n" + string.Join("\n", group.Windows.Take(8).Select(w => "• " + Trim(w.Title, 60)));
        else if (group?.Windows.FirstOrDefault() is { } window && !string.IsNullOrWhiteSpace(window.Title) && window.Title != Title)
            tip += "\n" + Trim(window.Title, 80);
        ToolTip = tip;
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

    /// <summary>Dock'a yeni gelen düğme küçükten büyüyerek belirir.</summary>
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

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
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
                var item = DockMenu.Item(title, null, w.BringToFront);
                item.FontWeight = w.State == ApplicationWindow.WindowState.Active ? FontWeights.SemiBold : FontWeights.Normal;
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(DockMenu.Separator());
        menu.Items.Add(DockMenu.Item(windows.Count > 0 ? "Yeni pencere" : "Aç", "\uE8A7", StartNewInstance, LaunchPath is not null));

        if (LaunchPath is { } path && !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            string? exe = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : ShellIcons.ResolveShortcut(path);
            if (exe is not null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                menu.Items.Add(DockMenu.Item("Yönetici olarak çalıştır", "\uE7EF", () => AppLauncher.RunAsAdmin(path)));
            menu.Items.Add(DockMenu.Item("Dosya konumunu aç", "\uE8B7", () => AppLauncher.OpenLocation(path)));
        }

        menu.Items.Add(DockMenu.Separator());
        if (Item is not null)
        {
            menu.Items.Add(DockMenu.Item("Sabitlemeyi kaldır", "\uE77A", () => AppServices.ConfigService.RemoveItem(Item.Id)));
        }
        else if (_group is not null && AppLauncher.PinnablePath(_group) is { } pinPath)
        {
            menu.Items.Add(DockMenu.Item(AppInfo.PinLabel, "\uE718", () =>
                AppServices.ConfigService.AddItem(DockItem.App(pinPath, _group.Title), DockItemsIndex.EndOfApps())));
        }

        if (windows.Count > 0)
        {
            menu.Items.Add(DockMenu.Separator());
            menu.Items.Add(DockMenu.Item(windows.Count > 1 ? $"Tüm pencereleri kapat ({windows.Count})" : "Pencereyi kapat", "\uE711",
                () => { foreach (var w in windows.ToList()) w.Close(); }));
            menu.Items.Add(DockMenu.Item(windows.Count > 1 ? "İşlemleri sonlandır" : "İşlemi sonlandır", "",
                () => { foreach (var w in windows.ToList()) KillProcess(w); }));
        }
    }

    /// <summary>Pencerenin ait olduğu işlemi doğrudan (kapanmayı beklemeden) sonlandırır; Görev Yöneticisi'ndeki "Görevi sonlandır" ile aynı.</summary>
    private static void KillProcess(ApplicationWindow window)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(window.Handle, out uint pid);
            if (pid == 0) return;
            using var process = Process.GetProcessById((int)pid);
            process.Kill(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "İşlem sonlandırılamadı");
        }
    }

    public void Detach()
    {
        if (_group is not null) _group.PropertyChanged -= OnGroupChanged;
    }
}

/// <summary>Yeni sabitlenen uygulamaların ekleneceği konum: son uygulama öğesinin hemen arkası.</summary>
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
