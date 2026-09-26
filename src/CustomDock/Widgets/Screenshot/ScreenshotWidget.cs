using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Services;
using Drawing = System.Drawing;

namespace CustomDock.Widgets;

public sealed class ScreenshotSettings : ObservableObject
{
    private int _delaySeconds;
    private bool _copyToClipboard = true;

    /// <summary>Wait before the capture starts, to open a menu first.</summary>
    public int DelaySeconds { get => _delaySeconds; set => Set(ref _delaySeconds, Math.Clamp(value, 0, 30)); }

    /// <summary>Full-screen captures also go to the clipboard (snips always do).</summary>
    public bool CopyToClipboard { get => _copyToClipboard; set => Set(ref _copyToClipboard, value); }
}

/// <summary>
/// Screenshots in one click: the Windows snipping overlay (region, window or full screen), or an instant capture of
/// every screen saved to Pictures\Screenshots.
/// </summary>
public sealed class ScreenshotWidget : WidgetBase
{
    public const string Icon = "M4,8 A2,2 0 0 1 6,6 H8 L9.5,4 H14.5 L16,6 H18 A2,2 0 0 1 20,8 V17 A2,2 0 0 1 18,19 H6 A2,2 0 0 1 4,17 Z M12,9.5 A3.2,3.2 0 1 1 11.99,9.5 Z";
    private const string SnipGlyph = "";
    private const string ScreenGlyph = "";
    private const string FolderGlyph = "";

    private readonly Border _iconLayout;
    private readonly TextBlock _countdown;
    private readonly StackPanel _buttonsLayout;
    private ScreenshotSettings _settings = new();
    private DispatcherTimer? _delayTimer;
    private int _remaining;

    public ScreenshotWidget()
    {
        Background = System.Windows.Media.Brushes.Transparent;
        var (disc, _) = WidgetUi.IconDisc(SnipGlyph, "AccentPinkBrush");
        _countdown = WidgetUi.Text("ValueText", "", 15);
        _countdown.HorizontalAlignment = HorizontalAlignment.Center;
        _countdown.VerticalAlignment = VerticalAlignment.Center;
        _countdown.Visibility = Visibility.Collapsed;
        _iconLayout = new Border { Name = "Layout_icon", Cursor = Cursors.Hand, Child = new Grid { Children = { disc, _countdown } } };
        _iconLayout.MouseLeftButtonUp += (_, e) => OnButton(e, Snip);

        _buttonsLayout = new StackPanel { Name = "Layout_buttons", Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _buttonsLayout.Children.Add(Button(SnipGlyph, L.T("Snip an area, a window or the screen"), Snip));
        _buttonsLayout.Children.Add(Button(ScreenGlyph, L.T("Capture all screens now"), () => Capture(fullScreen: true)));
        _buttonsLayout.Children.Add(Button(FolderGlyph, L.T("Open the Screenshots folder"), OpenFolder));

        Content = new Grid { Background = System.Windows.Media.Brushes.Transparent, Children = { _iconLayout, _buttonsLayout } };
        ToolTip = L.T("Screenshot") + "\n" + L.T("Click to snip, right-click for more");
    }

    private Button Button(string glyph, string tooltip, Action action)
    {
        var button = WidgetUi.IconButton(glyph, tooltip, 32, 15);
        if (_buttonsLayout.Children.Count > 0) button.Margin = new Thickness(4, 0, 0, 0);
        button.Click += (_, _) =>
        {
            if (!IsPreview && !DockDragHelper.JustDragged) action();
        };
        return button;
    }

    private void OnButton(MouseButtonEventArgs e, Action action)
    {
        if (DockDragHelper.JustDragged || IsPreview) return;
        e.Handled = true;
        action();
    }

    protected override void OnAttached() => _settings = GetSettings<ScreenshotSettings>();

    protected override void OnDetached() => CancelDelay();

    protected override void OnVariantChanged() => ShowLayout(_iconLayout, _buttonsLayout);

    private void Snip() => Capture(fullScreen: false);

    /// <summary>Starts a capture, after the configured delay (a countdown shows on the widget).</summary>
    private void Capture(bool fullScreen)
    {
        CancelDelay();
        if (_settings.DelaySeconds <= 0)
        {
            Run(fullScreen);
            return;
        }
        _remaining = _settings.DelaySeconds;
        ShowCountdown();
        _delayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _delayTimer.Tick += (_, _) =>
        {
            if (--_remaining > 0)
            {
                ShowCountdown();
                return;
            }
            CancelDelay();
            Run(fullScreen);
        };
        _delayTimer.Start();
    }

    private void ShowCountdown()
    {
        _countdown.Text = _remaining.ToString();
        _countdown.Visibility = Visibility.Visible;
        ((Grid)_iconLayout.Child).Children[0].Opacity = 0.25;
        RefreshCompact();
    }

    private void CancelDelay()
    {
        _delayTimer?.Stop();
        _delayTimer = null;
        _remaining = 0;
        _countdown.Visibility = Visibility.Collapsed;
        ((Grid)_iconLayout.Child).Children[0].Opacity = 1;
        RefreshCompact();
    }

    private void Run(bool fullScreen)
    {
        if (!fullScreen)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Couldn't start the snipping overlay");
                // Old or trimmed Windows installs: fall back to a full capture.
                CaptureAllScreens();
            }
            return;
        }
        CaptureAllScreens();
    }

    private static string Folder
    {
        get
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(pictures))
                pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
            return Path.Combine(pictures, "Screenshots");
        }
    }

    /// <summary>Saves every screen as one PNG (like Win+PrintScreen) and copies it to the clipboard.</summary>
    private void CaptureAllScreens()
    {
        try
        {
            var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            using var bitmap = new Drawing.Bitmap(bounds.Width, bounds.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, Drawing.CopyPixelOperation.SourceCopy);

            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png");
            bitmap.Save(path, Drawing.Imaging.ImageFormat.Png);
            if (_settings.CopyToClipboard)
            {
                try { System.Windows.Forms.Clipboard.SetImage(bitmap); }
                catch (Exception ex) { Log.Debug($"Clipboard busy: {ex.Message}"); }
            }
            Notify(L.T("Screenshot saved"), Path.GetFileName(path), "screenshot",
                new ToastAction(L.T("Open"), NotificationService.ActionOpenScreenshot, path),
                new ToastAction(L.T("Show in folder"), NotificationService.ActionShowScreenshot, path));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Screenshot failed");
            Notify(L.T("Screenshot failed"), ex.Message, "screenshot");
        }
    }

    private static void Open(string file)
    {
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error(ex, $"Failed to open {file}"); }
    }

    private static void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        Open(Folder);
    }

    public override bool OnCompactClick()
    {
        if (!IsPreview) Snip();
        return true;
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        tile.ShowGlyph(Descriptor.Icon, "AccentPinkBrush");
        tile.Text = _remaining > 0 ? _remaining.ToString() : null;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(L.T("Snip"), SnipGlyph, Snip));
        items.Add(DockMenu.Item(L.T("Capture all screens"), ScreenGlyph, () => Capture(fullScreen: true)));
        items.Add(DockMenu.Item(L.T("Open the Screenshots folder"), FolderGlyph, OpenFolder));
        if (_remaining > 0) items.Add(DockMenu.Item(L.T("Cancel"), "", CancelDelay));
    }
}
