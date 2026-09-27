using System.Windows;
using System.Windows.Threading;
using CustomDock.Core;

namespace CustomDock.Widgets.Web;

/// <summary>
/// Developer mode ("debugLogging"): watches the widgets folder and reloads a web widget as soon as one of its files
/// changes, so an edit shows up without reinstalling. A changed manifest.json is read again first.
/// </summary>
public static class WebWidgetDevReload
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);
    private static readonly Dictionary<string, (DispatcherTimer Timer, bool Manifest)> s_pending = new(StringComparer.OrdinalIgnoreCase);
    private static FileSystemWatcher? s_watcher;

    /// <summary>A widget's files changed; <c>manifestChanged</c> means the manifest was read again.</summary>
    public static event Action<WebWidgetManifest, bool>? Reloaded;

    public static bool IsWatching => s_watcher is not null;

    public static void Apply(bool enabled)
    {
        if (enabled) Start();
        else Stop();
    }

    private static void Start()
    {
        if (s_watcher is not null) return;
        try
        {
            Directory.CreateDirectory(WebWidgetCatalog.WidgetsDir);
            s_watcher = new FileSystemWatcher(WebWidgetCatalog.WidgetsDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            s_watcher.Changed += OnFileEvent;
            s_watcher.Created += OnFileEvent;
            s_watcher.Deleted += OnFileEvent;
            s_watcher.Renamed += OnFileEvent;
            s_watcher.EnableRaisingEvents = true;
            Log.Info($"Developer mode: reloading web widgets when files in {WebWidgetCatalog.WidgetsDir} change.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Couldn't watch the widgets folder");
            Stop();
        }
    }

    private static void Stop()
    {
        if (s_watcher is null) return;
        s_watcher.EnableRaisingEvents = false;
        s_watcher.Dispose();
        s_watcher = null;
        foreach (var (timer, _) in s_pending.Values) timer.Stop();
        s_pending.Clear();
    }

    // Watcher events arrive on a worker thread.
    private static void OnFileEvent(object sender, FileSystemEventArgs e)
        => Application.Current?.Dispatcher.BeginInvoke(() => Schedule(e.FullPath));

    private static void Schedule(string path)
    {
        if (s_watcher is null || WidgetFolderOf(WebWidgetCatalog.WidgetsDir, path) is not { } folder) return;
        bool manifest = Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase);

        if (!s_pending.TryGetValue(folder, out var pending))
        {
            var timer = new DispatcherTimer { Interval = Debounce };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                bool manifestChanged = s_pending.Remove(folder, out var entry) && entry.Manifest;
                Reload(folder, manifestChanged);
            };
            pending = (timer, false);
        }
        pending.Timer.Stop();
        pending.Timer.Start(); // an editor's burst of writes becomes one reload
        s_pending[folder] = (pending.Timer, pending.Manifest || manifest);
    }

    /// <summary>The widget folder (the first level under <paramref name="widgetsDir"/>) a changed path belongs to.</summary>
    internal static string? WidgetFolderOf(string widgetsDir, string path)
    {
        string root = Path.GetFullPath(widgetsDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        string rest = full[root.Length..];
        int separator = rest.IndexOf(Path.DirectorySeparatorChar);
        string first = separator < 0 ? rest : rest[..separator];
        return first.Length == 0 ? null : root + first;
    }

    private static void Reload(string folder, bool manifestChanged)
    {
        var manifest = WebWidgetCatalog.Read(folder, out var error);
        if (manifest is null)
        {
            Log.Warn($"Developer mode: {folder} not reloaded: {error}");
            return;
        }
        if (manifestChanged) WebWidgetCatalog.Refresh(manifest);
        Reloaded?.Invoke(manifest, manifestChanged);
        Log.Info($"Developer mode: web widget {manifest.Id} reloaded{(manifestChanged ? " with its new manifest" : "")}.");
    }
}
