using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Dock;
using CustomDock.Native;

namespace CustomDock.Widgets;

public sealed class StackSettings : ObservableObject
{
    private string _folder = "";
    private string _sort = "date";

    /// <summary>Watched folder; empty means the Downloads folder.</summary>
    public string Folder { get => _folder; set => Set(ref _folder, value ?? ""); }

    /// <summary>"date" (newest first) or "name".</summary>
    public string Sort { get => _sort; set => Set(ref _sort, value is "name" ? "name" : "date"); }
}

/// <summary>macOS-style stack: the newest files of a folder (Downloads by default), drag them out or click to open.</summary>
public partial class StackWidget : WidgetBase
{
    private const int MaxFiles = 40;
    private StackSettings _settings = new();
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _refreshDebounce;
    private List<FileSystemInfo> _files = new();

    public StackWidget()
    {
        InitializeComponent();
        _refreshDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _refreshDebounce.Tick += (_, _) => { _refreshDebounce.Stop(); Reload(); };
    }

    public static string DownloadsFolder
    {
        get
        {
            try
            {
                var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
                if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var path) == 0) return path;
            }
            catch { /* fall through */ }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    private string Folder => string.IsNullOrWhiteSpace(_settings.Folder) ? DownloadsFolder : _settings.Folder;

    protected override void OnAttached()
    {
        _settings = GetSettings<StackSettings>();
        _settings.PropertyChanged += OnSettingsChanged;
        Watch();
        Reload();
    }

    protected override void OnDetached()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        _refreshDebounce.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        Watch();
        Reload();
    }

    protected override void OnVariantChanged()
    {
        ShowLayout(Layout_stack, Layout_details);
        Render();
    }

    private void Watch()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (IsPreview || !Directory.Exists(Folder)) return;
        try
        {
            _watcher = new FileSystemWatcher(Folder) { IncludeSubdirectories = false, EnableRaisingEvents = true };
            FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(() => { _refreshDebounce.Stop(); _refreshDebounce.Start(); });
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Renamed += (s, e) => changed(s, e);
            _watcher.Error += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                // Network folders drop their watcher; reconnect a minute later.
                var retry = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
                retry.Tick += (_, _) => { retry.Stop(); Watch(); Reload(); };
                retry.Start();
            });
        }
        catch (Exception ex)
        {
            Log.Debug($"Stack widget can't watch {Folder}: {ex.Message}");
        }
    }

    private void Reload()
    {
        try
        {
            var dir = new DirectoryInfo(Folder);
            _files = !dir.Exists ? new() : dir.EnumerateFileSystemInfos()
                .Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0 &&
                            !f.Name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) &&
                            !f.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                            !f.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(MaxFiles)
                .ToList();
            if (_settings.Sort == "name") _files = _files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            Log.Debug($"Stack widget can't read {Folder}: {ex.Message}");
            _files = new();
        }
        Render();
    }

    private IEnumerable<FileSystemInfo> Newest => _files.OrderByDescending(f => f.LastWriteTime);

    private void Render()
    {
        var newest = Newest.Take(3).ToList();
        var icons = new[] { Icon1, Icon2, Icon3 };
        for (int i = 0; i < icons.Length; i++)
        {
            icons[i].Source = i < newest.Count ? ShellIcons.GetIcon(newest[i].FullName, 48) : null;
            icons[i].Visibility = i < newest.Count ? Visibility.Visible : Visibility.Collapsed;
        }
        if (newest.Count == 0)
        {
            Icon1.Source = ShellIcons.GetIcon(Folder, 48);
            Icon1.Visibility = Visibility.Visible;
        }

        string folderName = Path.GetFileName(Folder.TrimEnd('\\')) is { Length: > 0 } name ? name : Folder;
        FolderTitle.Text = folderName;
        PopupTitle.Text = folderName;
        DetailsIcon.Source = newest.Count > 0 ? ShellIcons.GetIcon(newest[0].FullName, 48) : ShellIcons.GetIcon(Folder, 48);
        NewestText.Text = newest.Count > 0 ? newest[0].Name : L.T("Empty");
        ToolTip = newest.Count > 0 ? L.T("{0} · newest: {1}", folderName, newest[0].Name) : folderName;
        SetIdle(_files.Count == 0);
        if (FilesPopup.IsOpen) BuildGrid();
        RefreshCompact();
    }

    private void BuildGrid()
    {
        FileGrid.Children.Clear();
        if (_files.Count == 0)
        {
            FileGrid.Children.Add(new TextBlock { Text = L.T("This folder is empty"), Margin = new Thickness(8), Foreground = (Brush)FindResource("TextSecondaryBrush") });
            return;
        }
        foreach (var file in _files)
        {
            var f = file;
            var tile = new StackPanel { Width = 84, Margin = new Thickness(2) };
            tile.Children.Add(new Image { Source = ShellIcons.GetIcon(f.FullName, 48), Width = 40, Height = 40, Margin = new Thickness(0, 6, 0, 4) });
            tile.Children.Add(new TextBlock
            {
                Text = f.Name,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 30,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
            });
            var cell = new Border { CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = tile, Cursor = Cursors.Hand, ToolTip = $"{f.Name}\n{f.LastWriteTime:g}" };
            cell.MouseEnter += (_, _) => cell.SetResourceReference(Border.BackgroundProperty, "DockHoverBrush");
            cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;

            Point? pressedAt = null;
            cell.PreviewMouseLeftButtonDown += (_, e) => pressedAt = e.GetPosition(cell);
            cell.PreviewMouseMove += (_, e) =>
            {
                if (pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
                var delta = e.GetPosition(cell) - start;
                if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6) return;
                pressedAt = null;
                // Drag the file into any app or folder.
                DragDrop.DoDragDrop(cell, new DataObject(DataFormats.FileDrop, new[] { f.FullName }), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            };
            cell.MouseLeftButtonUp += (_, e) =>
            {
                if (pressedAt is null) return;
                pressedAt = null;
                e.Handled = true;
                ClosePopup(FilesPopup);
                Open(f.FullName);
            };
            var menu = new ContextMenu();
            menu.Items.Add(DockMenu.Item(L.T("Open"), "", () => Open(f.FullName)));
            menu.Items.Add(DockMenu.Item(L.T("Show in folder"), "", () => Process.Start("explorer.exe", $"/select,\"{f.FullName}\"")));
            cell.ContextMenu = menu;
            FileGrid.Children.Add(cell);
        }
    }

    private static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error(ex, $"Failed to open {path}"); }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        ClosePopup(FilesPopup);
        Open(Folder);
    }

    private void ShowFiles()
    {
        Reload();
        BuildGrid();
        OpenPopup(FilesPopup);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (DockDragHelper.JustDragged || IsPreview || e.Handled) return;
        ShowFiles();
        e.Handled = true;
    }

    public override bool OnCompactClick()
    {
        ShowFiles();
        return true;
    }

    protected override void UpdateCompact(CompactTile tile)
    {
        var newest = Newest.FirstOrDefault();
        tile.SetVisual(new Image { Source = newest is null ? ShellIcons.GetIcon(Folder, 48) : ShellIcons.GetIcon(newest.FullName, 48), Width = 24, Height = 24 });
        tile.Text = _files.Count > 0 ? _files.Count.ToString() : null;
    }

    public override void AddContextMenuItems(ItemCollection items)
    {
        items.Add(DockMenu.Item(L.T("Open folder"), "", () => Open(Folder)));
        items.Add(DockMenu.Check(L.T("Sort by name"), _settings.Sort == "name", () => _settings.Sort = _settings.Sort == "name" ? "date" : "name"));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out string path);
}
