using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using CustomDock.Services;
using CustomDock.Settings;
using CustomDock.Shell;
using ManagedShell.ShellFolders;

namespace CustomDock.Dock;

/// <summary>One result in the quick launcher.</summary>
public sealed class LauncherResult
{
    public required string Title { get; init; }

    public string Subtitle { get; init; } = "";

    public string? Keywords { get; init; }

    /// <summary>Icon font glyph, used when there is no image.</summary>
    public string Glyph { get; init; } = "";

    public Func<ImageSource?>? Icon { get; init; }

    /// <summary>An emoji shown as the icon (emoji results).</summary>
    public string? Emoji { get; init; }

    public required Action<bool> Run { get; init; }

    /// <summary>Small boost so apps come before settings and files for the same match.</summary>
    public int Weight { get; init; }

    public int Score { get; set; }
}

/// <summary>
/// Spotlight-style quick launcher: apps, open windows, DockHub and Windows settings, DockHub commands, recent files,
/// files from the Windows Search index, quick math, unit and currency conversion, emoji (":heart") and a web search.
/// Opens with a global shortcut (Win+Alt+Space by default) or the dock's search button. Enter runs the selection,
/// Ctrl+Enter runs an app as administrator or shows a file in its folder, Esc closes.
/// </summary>
public sealed class LauncherWindow : Window
{
    private const int MaxResults = 8;
    private static LauncherWindow? s_instance;
    private static List<AppEntry>? s_apps;
    private static DateTime s_appsLoadedAt;
    private static readonly Dictionary<string, int> s_launchCounts = new(StringComparer.OrdinalIgnoreCase);

    private readonly TextBox _query;
    private readonly TextBlock _hint;
    private readonly ListBox _list;
    private List<LauncherResult> _results = new();
    private bool _closing;

    public static void Toggle()
    {
        if (s_instance is { IsVisible: true } open)
        {
            open.CloseLauncher();
            return;
        }
        s_instance = new LauncherWindow();
        s_instance.Show();
        s_instance.Activate();
    }

    private LauncherWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 640;
        Title = L.T("Quick launcher");

        var glyph = new TextBlock
        {
            Text = "",
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18, 0, 12, 0),
        };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        _query = new TextBox
        {
            FontSize = 20,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 18, 0),
        };
        _query.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        _query.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimaryBrush");
        _query.TextChanged += (_, _) => Search();
        _query.PreviewKeyDown += OnQueryKeyDown;

        _hint = new TextBlock
        {
            Text = L.T("Search apps, files and settings, calculate, or type : for emoji"),
            FontSize = 20,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");

        var queryHost = new Grid { Children = { _hint, _query } };
        var header = new DockPanel { Height = 58, LastChildFill = true };
        DockPanel.SetDock(glyph, System.Windows.Controls.Dock.Left);
        header.Children.Add(glyph);
        header.Children.Add(queryHost);

        _list = new ListBox { Margin = new Thickness(8, 0, 8, 8), MaxHeight = 460, Focusable = false };
        _list.SetResourceReference(StyleProperty, "PlainListBox");
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemAt(e.OriginalSource as DependencyObject) is { } index)
            {
                _list.SelectedIndex = index;
                RunSelected(admin: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            }
        };

        var separator = new Border { Height = 1, Margin = new Thickness(0, 0, 0, 6) };
        separator.SetResourceReference(Border.BackgroundProperty, "SeparatorBrush");

        var layout = new StackPanel { Children = { header, separator, _list } };
        var frame = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(16),
            Child = layout,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.4 },
        };
        frame.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "PopupBorderBrush");
        TextScale.Apply(frame);
        Content = frame;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            WindowEffects.MakeToolWindow(hwnd);
            WindowEffects.SetDarkMode(hwnd, ThemeManager.IsDark);
        };
        Loaded += (_, _) =>
        {
            PlaceOnScreen();
            NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
            _query.Focus();
            Keyboard.Focus(_query);
            Search();
            EnsureAppsLoaded();
        };
        Deactivated += (_, _) => CloseLauncher();
        Closed += (_, _) =>
        {
            if (ReferenceEquals(s_instance, this)) s_instance = null;
        };
    }

    private void PlaceOnScreen()
    {
        var monitor = MonitorHelper.GetPreferred(AppServices.Config.MonitorDevice);
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? monitor.DpiScale;
        var work = monitor.WorkArea;
        Left = work.Left / scale + (work.Width / scale - ActualWidth) / 2;
        Top = work.Top / scale + work.Height / scale * 0.18;
    }

    private void CloseLauncher()
    {
        if (_closing) return;
        _closing = true;
        _asyncTimer?.Stop();
        _asyncSearch?.Cancel();
        Close();
    }

    // ------------------------------------------------------------------ Keyboard

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (_query.Text.Length > 0) _query.Clear();
                else CloseLauncher();
                e.Handled = true;
                break;
            case Key.Down:
                Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                RunSelected(admin: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        if (_list.Items.Count == 0) return;
        int index = Math.Clamp(_list.SelectedIndex + delta, 0, _list.Items.Count - 1);
        _list.SelectedIndex = index;
        _list.ScrollIntoView(_list.Items[index]);
    }

    private int? ItemAt(DependencyObject? source)
    {
        for (var d = source; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is ListBoxItem item) return _list.ItemContainerGenerator.IndexFromContainer(item);
        return null;
    }

    private void RunSelected(bool admin)
    {
        int index = _list.SelectedIndex;
        if (index < 0 || index >= _results.Count) return;
        var result = _results[index];
        CloseLauncher();
        s_launchCounts[result.Title] = s_launchCounts.GetValueOrDefault(result.Title) + 1;
        try
        {
            result.Run(admin);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Launcher action failed: {result.Title}");
        }
    }

    // ------------------------------------------------------------------ Results

    /// <summary>Results found right away, in order: the dock's apps and windows (also before anything is typed), then the rest.</summary>
    private static readonly ILauncherSource[] Sources =
    {
        new LauncherSource(DockApps, whenEmpty: true),
        new LauncherSource(OpenWindows, whenEmpty: true),
        new LauncherSource(Apps),
        new LauncherSource(DockHubPages),
        new LauncherSource(WindowsSettings),
        new LauncherSource(Commands),
        new LauncherSource(RecentFiles),
    };

    /// <summary>Results that take a while: asked once typing pauses for <see cref="AsyncDelay"/>.</summary>
    private static readonly IAsyncLauncherSource[] AsyncSources = { new CurrencySource(), new FileSource() };

    private static readonly TimeSpan AsyncDelay = TimeSpan.FromMilliseconds(150);

    private readonly UnitSource _units = new();
    private List<LauncherResult> _found = new();
    private readonly List<LauncherResult> _foundLater = new();
    private LauncherResult? _webSearch;
    private string _searched = "";
    private DispatcherTimer? _asyncTimer;
    private CancellationTokenSource? _asyncSearch;

    private void Search()
    {
        string query = _query.Text.Trim();
        _hint.Visibility = _query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        // The slow sources start over with each keystroke.
        _asyncSearch?.Cancel();
        _asyncSearch = null;
        _asyncTimer?.Stop();
        _foundLater.Clear();
        _searched = query;

        if (EmojiSource.IsEmojiQuery(query))
        {
            _found = EmojiSource.Results(query, EmojiSource.MaxResults).ToList();
            _webSearch = null;
            Render(keepSelection: false);
            return;
        }

        var results = new List<LauncherResult>();
        if (LauncherMath.TryEvaluate(query, out double value))
        {
            string formatted = LauncherMath.Format(value);
            results.Add(new LauncherResult
            {
                Title = "= " + formatted,
                Subtitle = L.T("Press Enter to copy the result"),
                Glyph = "\uE8EF",
                Score = 1000,
                Run = _ => Clipboard.SetText(formatted),
            });
        }
        results.AddRange(_units.Results(query));

        foreach (var source in Sources)
        {
            if (query.Length == 0 && !source.WhenEmpty) continue;
            foreach (var candidate in source.Results(query))
            {
                int score = LauncherMatch.Score(candidate.Title, candidate.Keywords, query);
                if (score == 0) continue;
                candidate.Score = score + candidate.Weight + Math.Min(20, s_launchCounts.GetValueOrDefault(candidate.Title) * 5);
                results.Add(candidate);
            }
        }
        _found = results;

        string q = query;
        _webSearch = query.Length == 0 ? null : new LauncherResult
        {
            Title = L.T("Search the web for “{0}”", q),
            Subtitle = L.T("Opens your browser"),
            Glyph = "\uE774",
            Run = _ => Open("https://www.google.com/search?q=" + Uri.EscapeDataString(q)),
        };
        Render(keepSelection: false);

        if (query.Length == 0) return;
        if (_asyncTimer is null)
        {
            _asyncTimer = new DispatcherTimer { Interval = AsyncDelay };
            _asyncTimer.Tick += (_, _) => StartAsyncSearch();
        }
        _asyncTimer.Start();
    }

    private void StartAsyncSearch()
    {
        _asyncTimer?.Stop();
        if (_closing) return;
        var search = new CancellationTokenSource();
        _asyncSearch = search;
        foreach (var source in AsyncSources) _ = SearchLaterAsync(source, _searched, search.Token);
    }

    /// <summary>Adds a slow source's results when they arrive, unless the text changed meanwhile.</summary>
    private async Task SearchLaterAsync(IAsyncLauncherSource source, string query, CancellationToken token)
    {
        IReadOnlyList<LauncherResult> found;
        try
        {
            found = await source.SearchAsync(query, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Warn($"Launcher source {source.GetType().Name} failed: {ex.Message}");
            return;
        }
        if (token.IsCancellationRequested || _closing || query != _searched || found.Count == 0) return;
        _foundLater.AddRange(found);
        Render(keepSelection: true);
    }

    /// <summary>
    /// Shows the best results (the web search last). While slower results arrive the selection stays on the result it
    /// was on, so Enter never runs something that just moved under it.
    /// </summary>
    private void Render(bool keepSelection)
    {
        var before = _results;
        int selected = _list.SelectedIndex;
        var results = _found.Concat(_foundLater)
            .OrderByDescending(r => r.Score).ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(EmojiSource.IsEmojiQuery(_searched) ? EmojiSource.MaxResults : MaxResults).ToList();
        if (_webSearch is not null) results.Add(_webSearch);

        _results = results;
        _list.Items.Clear();
        foreach (var result in results) _list.Items.Add(BuildRow(result));
        _list.SelectedIndex = keepSelection ? LauncherRanking.SelectionAfter(before, selected, results) : results.Count > 0 ? 0 : -1;
    }

    private static ListBoxItem BuildRow(LauncherResult result)
    {
        FrameworkElement icon;
        var image = result.Icon?.Invoke();
        if (result.Emoji is { } emoji)
        {
            icon = new TextBlock
            {
                Text = emoji,
                FontSize = 22,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                Width = 28,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else if (image is not null)
        {
            icon = new Image { Source = image, Width = 28, Height = 28 };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        }
        else
        {
            var glyph = new TextBlock { Text = result.Glyph, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            icon = new Border { Width = 28, Height = 28, Child = glyph };
        }
        icon.Margin = new Thickness(0, 0, 14, 0);

        var title = new TextBlock { Text = result.Title, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis };
        var subtitle = new TextBlock { Text = result.Subtitle, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        if (result.Subtitle.Length == 0) subtitle.Visibility = Visibility.Collapsed;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, subtitle } };
        var row = new DockPanel { Children = { icon, text } };
        DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);

        var item = new ListBoxItem { Content = row, Padding = new Thickness(12, 7, 12, 7) };
        item.SetResourceReference(StyleProperty, "NavItem");
        return item;
    }

    private static IEnumerable<LauncherResult> DockApps()
    {
        foreach (var item in AppServices.Config.Items.Where(i => i.Kind == DockItemKind.App && !string.IsNullOrEmpty(i.Path)))
        {
            var app = item;
            string title = !string.IsNullOrWhiteSpace(app.Name) ? app.Name! : Path.GetFileNameWithoutExtension(app.Path!.Split('!')[0]);
            yield return new LauncherResult
            {
                Title = title,
                Subtitle = L.T("On your dock"),
                Icon = () => AppIcons.For(app, 32, out _),
                Weight = 12,
                Run = admin =>
                {
                    if (admin && !app.Path!.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) AppLauncher.RunAsAdmin(app.Path);
                    else AppLauncher.Launch(app);
                },
            };
        }
    }

    private static IEnumerable<LauncherResult> OpenWindows()
    {
        if (AppServices.Shell?.RunningApps is not { } running) yield break;
        foreach (var group in running.Groups.ToList())
        {
            foreach (var window in group.Windows.ToList())
            {
                var w = window;
                string title = string.IsNullOrWhiteSpace(w.Title) ? group.Title : w.Title;
                yield return new LauncherResult
                {
                    Title = title,
                    Subtitle = L.T("Switch to window · {0}", group.Title),
                    Keywords = group.Title,
                    Icon = () => group.Icon,
                    Weight = 8,
                    Run = _ =>
                    {
                        if (w.IsMinimized) w.Restore();
                        w.BringToFront();
                    },
                };
            }
        }
    }

    private static void EnsureAppsLoaded()
    {
        if (s_apps is not null && DateTime.UtcNow - s_appsLoadedAt < TimeSpan.FromMinutes(10)) return;
        // Enumerating the Start menu takes a moment; do it after the window is up.
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            var apps = new List<AppEntry>();
            try
            {
                using var folder = new ShellFolder("shell:AppsFolder", IntPtr.Zero, false, false);
                foreach (var file in folder.Files.ToList())
                {
                    if (string.IsNullOrWhiteSpace(file.DisplayName) || string.IsNullOrWhiteSpace(file.FileName)) continue;
                    apps.Add(new AppEntry(file.DisplayName, file.FileName));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Launcher could not read the app list");
            }
            s_apps = apps.GroupBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).ToList();
            s_appsLoadedAt = DateTime.UtcNow;
            if (s_instance is { IsVisible: true } launcher && launcher._query.Text.Length > 0) launcher.Search();
        });
    }

    private static IEnumerable<LauncherResult> Apps()
    {
        if (s_apps is null) yield break;
        foreach (var entry in s_apps)
        {
            var app = entry;
            yield return new LauncherResult
            {
                Title = app.Name,
                Subtitle = L.T("App"),
                Icon = () => app.Icon,
                Weight = 10,
                Run = admin =>
                {
                    if (admin && app.DockPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) AppLauncher.RunAsAdmin(app.DockPath);
                    else AppLauncher.Launch(app.DockPath);
                },
            };
        }
    }

    private static readonly (string Page, string Name, string Glyph, string Keywords)[] SettingsPages =
    {
        ("general", "General", "", "startup language taskbar başlangıç dil"),
        ("taskbar", "Taskbar", "", "tray clock start search tepsi saat"),
        ("appearance", "Appearance", "", "theme size glass tema boyut"),
        ("items", "Dock items", "", "apps widgets folders öğeler"),
        ("gallery", "Widget gallery", "", "widgets add ekle"),
        ("profiles", "Profiles", "", "profile work gaming profil"),
        ("keyboard", "Keyboard shortcuts", "", "hotkeys kısayol"),
        ("backup", "Backup and troubleshooting", "", "export import restore yedek"),
        ("about", "About", "", "version update sürüm güncelleme"),
    };

    private static IEnumerable<LauncherResult> DockHubPages()
    {
        foreach (var (page, name, glyph, keywords) in SettingsPages)
        {
            string p = page;
            yield return new LauncherResult
            {
                Title = L.T("DockHub settings: {0}", L.T(name)),
                Subtitle = L.T("DockHub"),
                Keywords = $"{name} {L.T(name)} {keywords} dockhub settings ayarlar",
                Glyph = glyph,
                Run = _ => App.Instance.ShowSettings(p),
            };
        }
    }

    private static readonly (string Uri, string Name, string Keywords)[] WindowsPages =
    {
        ("ms-settings:display", "Display", "screen resolution scale brightness ekran çözünürlük parlaklık"),
        ("ms-settings:nightlight", "Night light", "blue light gece ışığı"),
        ("ms-settings:sound", "Sound", "audio volume speaker microphone ses hoparlör mikrofon"),
        ("ms-settings:notifications", "Notifications", "do not disturb focus bildirim rahatsız etme"),
        ("ms-settings:bluetooth", "Bluetooth and devices", "bluetooth devices cihazlar"),
        ("ms-settings:network-wifi", "Wi-Fi", "wireless network internet kablosuz ağ"),
        ("ms-settings:network", "Network and internet", "ethernet vpn ağ internet"),
        ("ms-settings:personalization", "Personalization", "wallpaper colors kişiselleştirme"),
        ("ms-settings:personalization-background", "Background", "wallpaper arka plan duvar kağıdı"),
        ("ms-settings:colors", "Colors", "dark mode accent renkler karanlık mod"),
        ("ms-settings:appsfeatures", "Installed apps", "uninstall programs yüklü uygulamalar kaldır"),
        ("ms-settings:defaultapps", "Default apps", "browser default varsayılan uygulamalar"),
        ("ms-settings:startupapps", "Startup apps", "autostart başlangıç uygulamaları"),
        ("ms-settings:windowsupdate", "Windows Update", "update güncelleştirme"),
        ("ms-settings:powersleep", "Power and battery", "sleep battery güç pil uyku"),
        ("ms-settings:storagesense", "Storage", "disk space depolama"),
        ("ms-settings:mousetouchpad", "Mouse", "pointer touchpad fare"),
        ("ms-settings:typing", "Typing", "keyboard yazma klavye"),
        ("ms-settings:regionlanguage", "Language and region", "keyboard layout dil bölge klavye"),
        ("ms-settings:dateandtime", "Date and time", "clock time zone tarih saat"),
        ("ms-settings:privacy-microphone", "Microphone privacy", "mic permissions mikrofon izin"),
        ("ms-settings:privacy-webcam", "Camera privacy", "camera webcam kamera"),
        ("ms-settings:multitasking", "Multitasking", "snap virtual desktops çoklu görev sanal masaüstü"),
        ("ms-settings:about", "About this PC", "system info specs sistem hakkında"),
    };

    private static IEnumerable<LauncherResult> WindowsSettings()
    {
        foreach (var (uri, name, keywords) in WindowsPages)
        {
            string u = uri;
            yield return new LauncherResult
            {
                Title = L.T(name),
                Subtitle = L.T("Windows settings"),
                Keywords = $"{name} {keywords} settings ayarlar",
                Glyph = "",
                Run = _ => Open(u),
            };
        }
    }

    private static IEnumerable<LauncherResult> Commands()
    {
        var config = AppServices.Config;
        LauncherResult Command(string title, string glyph, string keywords, Action action) => new()
        {
            Title = L.T(title),
            Subtitle = L.T("DockHub command"),
            Keywords = $"{title} {keywords}",
            Glyph = glyph,
            Run = _ => action(),
        };

        yield return Command("Pin an application", "", "add app uygulama sabitle", () => App.Instance.ShowAppPicker());
        yield return Command(config.AutoHide ? "Turn off auto-hide" : "Turn on auto-hide", "", "hide gizle", () => config.AutoHide = !config.AutoHide);
        yield return Command("Show desktop", "", "minimize masaüstü", () => AppServices.Shell?.ToggleDesktop());
        yield return Command(AppServices.Audio.IsMuted ? "Unmute sound" : "Mute sound", "", "volume audio ses sessiz", AppServices.Audio.ToggleMute);
        yield return Command("Mute or unmute the microphone", "", "mic mikrofon", AppServices.Microphone.ToggleMute);
        yield return Command("New virtual desktop", "", "desktop masaüstü", AppServices.VirtualDesktops.CreateDesktop);
        yield return Command("Task Manager", "", "processes görev yöneticisi", () => Open("taskmgr.exe"));
        yield return Command("Lock the computer", "", "lock kilitle", () => LockWorkStation());
        yield return Command("Restart DockHub", "", "reload yeniden başlat", App.Instance.RestartApplication);
        foreach (var profile in AppServices.Profiles.Profiles.Where(p => p.Id != config.ActiveProfileId).ToList())
        {
            string id = profile.Id;
            yield return new LauncherResult
            {
                Title = L.T("Switch to the {0} profile", profile.Name),
                Subtitle = L.T("DockHub command"),
                Keywords = "profile profil",
                Glyph = "",
                Run = _ => AppServices.Profiles.SwitchTo(id),
            };
        }
    }

    private static IEnumerable<LauncherResult> RecentFiles()
    {
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        List<FileInfo> files;
        try
        {
            files = new DirectoryInfo(folder).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc).Take(150).ToList();
        }
        catch
        {
            yield break;
        }
        foreach (var file in files)
        {
            string path = file.FullName;
            yield return new LauncherResult
            {
                Title = Path.GetFileNameWithoutExtension(file.Name),
                Subtitle = L.T("Recent file · {0}", file.LastWriteTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture)),
                Glyph = "",
                Weight = -15,
                Run = _ => Open(path),
            };
        }
    }

    internal static void Open(string target, string? arguments = null)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = arguments ?? "" }); }
        catch (Exception ex) { Log.Error(ex, $"Launcher could not open {target}"); }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}
