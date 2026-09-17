using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CustomDock.Core;
using CustomDock.Native;
using ManagedShell;
using ManagedShell.WindowsTasks;
using TBPFLAG = ManagedShell.Interop.NativeMethods.TBPFLAG;

namespace CustomDock.Shell;

/// <summary>Aynı uygulamaya ait açık pencereler.</summary>
public sealed class AppGroup : ObservableObject
{
    private bool _isActive;
    private bool _isFlashing;
    private ImageSource? _icon;
    private ImageSource? _overlayIcon;
    private string _title = "";
    private double _progress;
    private bool _hasProgress;
    private bool _progressError;
    private bool _progressIndeterminate;
    private int _windowCount;

    public AppGroup(string key)
    {
        Key = key;
    }

    public string Key { get; }

    public List<ApplicationWindow> Windows { get; } = new();

    public long Order { get; set; }

    public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }

    public bool IsFlashing { get => _isFlashing; private set => Set(ref _isFlashing, value); }

    public ImageSource? Icon { get => _icon; private set => Set(ref _icon, value); }

    public ImageSource? OverlayIcon { get => _overlayIcon; private set => Set(ref _overlayIcon, value); }

    public string Title { get => _title; private set => Set(ref _title, value); }

    public int WindowCount { get => _windowCount; private set => Set(ref _windowCount, value); }

    /// <summary>0-1 arası görev çubuğu ilerlemesi (ITaskbarList3).</summary>
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    public bool HasProgress { get => _hasProgress; private set => Set(ref _hasProgress, value); }

    public bool ProgressError { get => _progressError; private set => Set(ref _progressError, value); }

    public bool ProgressIndeterminate { get => _progressIndeterminate; private set => Set(ref _progressIndeterminate, value); }

    public string? ExecutablePath => AppKeys.ExecutableOf(Key);

    /// <summary>En son etkin olan pencere (ya da ilk pencere).</summary>
    public ApplicationWindow? PrimaryWindow =>
        Windows.FirstOrDefault(w => w.State == ApplicationWindow.WindowState.Active) ?? Windows.FirstOrDefault();

    internal void Refresh()
    {
        WindowCount = Windows.Count;
        IsActive = Windows.Any(w => w.State == ApplicationWindow.WindowState.Active);
        IsFlashing = Windows.Any(w => w.State == ApplicationWindow.WindowState.Flashing);
        OverlayIcon = Windows.Select(w => w.OverlayIcon).FirstOrDefault(i => i is not null);

        var progressWindow = Windows.FirstOrDefault(w => w.ProgressState != TBPFLAG.TBPF_NOPROGRESS);
        HasProgress = progressWindow is not null;
        if (progressWindow is not null)
        {
            Progress = Math.Clamp(progressWindow.ProgressValue / 65534.0, 0, 1);
            ProgressError = progressWindow.ProgressState == TBPFLAG.TBPF_ERROR || progressWindow.ProgressState == TBPFLAG.TBPF_PAUSED;
            ProgressIndeterminate = progressWindow.ProgressState == TBPFLAG.TBPF_INDETERMINATE;
        }

        var first = Windows.FirstOrDefault();
        if (first is null) return;

        if (ExecutablePath is { } exe)
        {
            Icon ??= ShellIcons.GetIcon(exe, 96);
            if (string.IsNullOrEmpty(Title))
            {
                string? description = null;
                try { description = FileVersionInfo.GetVersionInfo(exe).FileDescription; } catch { /* yoksay */ }
                Title = string.IsNullOrWhiteSpace(description) ? Path.GetFileNameWithoutExtension(exe) : description.Trim();
            }
        }
        else
        {
            Title = first.Title;
        }

        if (Icon is null || ExecutablePath is null)
        {
            var windowIcon = first.Icon;
            if (windowIcon is not null) Icon = windowIcon;
        }
    }
}

/// <summary>ManagedShell görev listesini uygulama gruplarına dönüştürür.</summary>
public sealed class RunningAppsService : IDisposable
{
    private static readonly HashSet<string> WatchedProperties = new()
    {
        "State", "Icon", "OverlayIcon", "ProgressState", "ProgressValue", "Title", "Category",
    };

    private readonly ICollectionView _view;
    private readonly Dictionary<string, AppGroup> _groups = new();
    private readonly HashSet<ApplicationWindow> _subscribed = new();
    private readonly Dispatcher _dispatcher;
    private bool _rebuildQueued;
    private long _orderCounter;

    public RunningAppsService(ShellManager manager)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _view = manager.Tasks.GroupedWindows;
        ((INotifyCollectionChanged)_view).CollectionChanged += OnViewChanged;
        Rebuild();
    }

    /// <summary>Açık uygulama grupları (ilk görülme sırasına göre).</summary>
    public ObservableCollection<AppGroup> Groups { get; } = new();

    /// <summary>Gruplar eklendiğinde/kaldırıldığında tetiklenir.</summary>
    public event Action? GroupsChanged;

    public AppGroup? Find(string key) => _groups.TryGetValue(key, out var group) ? group : null;

    private void OnViewChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRebuild();

    private void OnWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null || WatchedProperties.Contains(e.PropertyName))
            QueueRebuild();
    }

    private void QueueRebuild()
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.DataBind, () =>
        {
            _rebuildQueued = false;
            Rebuild();
        });
    }

    private void Rebuild()
    {
        List<ApplicationWindow> windows;
        try
        {
            windows = _view.Cast<ApplicationWindow>().Where(w => w.ShowInTaskbar).ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Görev listesi okunamadı");
            return;
        }

        // Olay abonelikleri
        foreach (var window in _subscribed.Except(windows).ToList())
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
            _subscribed.Remove(window);
        }
        foreach (var window in windows)
        {
            if (_subscribed.Add(window))
                window.PropertyChanged += OnWindowPropertyChanged;
        }

        var byKey = windows.GroupBy(w => w.Category ?? AppKeys.ForWindow(w)).ToDictionary(g => g.Key, g => g.ToList());
        bool structureChanged = false;

        foreach (var key in _groups.Keys.Except(byKey.Keys).ToList())
        {
            Groups.Remove(_groups[key]);
            _groups.Remove(key);
            structureChanged = true;
        }

        foreach (var (key, list) in byKey)
        {
            if (!_groups.TryGetValue(key, out var group))
            {
                group = new AppGroup(key) { Order = ++_orderCounter };
                _groups[key] = group;
                Groups.Add(group);
                structureChanged = true;
            }

            group.Windows.Clear();
            group.Windows.AddRange(list);
            group.Refresh();
        }

        if (structureChanged)
            GroupsChanged?.Invoke();
    }

    public void Dispose()
    {
        ((INotifyCollectionChanged)_view).CollectionChanged -= OnViewChanged;
        foreach (var window in _subscribed)
            window.PropertyChanged -= OnWindowPropertyChanged;
        _subscribed.Clear();
    }
}
