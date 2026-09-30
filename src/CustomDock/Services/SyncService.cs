using System.IO;
using System.Windows;
using System.Windows.Threading;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>
/// Settings sync through a folder that syncs itself between PCs (OneDrive, Dropbox, a network share): DockHub writes
/// the shared settings (<see cref="SyncMerge"/>) to DockHub-sync.json there two seconds after a change, and takes the
/// file when another PC wrote a newer one, as one step that Undo reverts. The last writer wins. Nothing is written or
/// watched while sync is off.
/// </summary>
public sealed class SyncService
{
    private static readonly TimeSpan WriteDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadDelay = TimeSpan.FromSeconds(1.5);

    private readonly DispatcherTimer _writeTimer = new(DispatcherPriority.Background) { Interval = WriteDelay };
    private readonly DispatcherTimer _readTimer = new(DispatcherPriority.Background) { Interval = ReadDelay };
    private FileSystemWatcher? _watcher;
    private string? _folder;
    /// <summary>The shared settings last written or taken, so a save that changed nothing shared writes nothing.</summary>
    private string _lastShared = "";

    public SyncService()
    {
        _writeTimer.Tick += (_, _) =>
        {
            _writeTimer.Stop();
            Write();
        };
        _readTimer.Tick += (_, _) =>
        {
            _readTimer.Stop();
            Read();
        };
    }

    private static ConfigService Settings => AppServices.ConfigService;

    private static AppConfig Config => AppServices.Config;

    public bool IsRunning => _folder is not null;

    /// <summary>What happened last (for Settings), or null.</summary>
    public string? Status { get; private set; }

    public event Action? StatusChanged;

    private string FilePath => Path.Combine(_folder!, SyncFile.FileName);

    /// <summary>Starts (or restarts, after the folder changed) syncing; stops when the folder is cleared.</summary>
    public void Start()
    {
        Stop();
        if (Config.SyncFolder is not { } folder) return;
        if (!Directory.Exists(folder))
        {
            SetStatus(L.T("The sync folder {0} doesn't exist.", folder));
            return;
        }
        if (string.IsNullOrEmpty(Config.SyncDeviceId))
        {
            Config.SyncDeviceId = Guid.NewGuid().ToString("N");
            Settings.ScheduleSave();
        }
        _folder = folder;
        try
        {
            _watcher = new FileSystemWatcher(folder, SyncFile.FileName) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Warn($"Sync folder is not watched: {ex.Message}");
        }
        Settings.Saved += OnSaved;
        Log.Info($"Settings sync started in {folder}");
        // Another PC's newer settings first; then this PC's, if they differ from the file.
        Read();
        Write();
    }

    public void Stop()
    {
        _writeTimer.Stop();
        _readTimer.Stop();
        Settings.Saved -= OnSaved;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        _folder = null;
        _lastShared = "";
    }

    /// <summary>Shares a change that is still waiting (DockHub is closing).</summary>
    public void Flush()
    {
        if (!_writeTimer.IsEnabled) return;
        _writeTimer.Stop();
        Write();
    }

    private void OnSaved()
    {
        _writeTimer.Stop();
        _writeTimer.Start();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e) => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        if (_folder is null) return;
        _readTimer.Stop();
        _readTimer.Start();
    });

    private void Write()
    {
        if (_folder is null || Config.SyncDeviceId is not { } device) return;
        var shared = SyncMerge.Extract(Settings.ConfigJson());
        string text = shared.ToJsonString();
        if (text == _lastShared) return;
        var file = new SyncFile(device, Environment.MachineName, DateTime.UtcNow, shared);
        try
        {
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, file.ToJson());
            File.Move(temp, FilePath, overwrite: true);
            _lastShared = text;
            Config.SyncAppliedAt = file.UpdatedAt;
            SetStatus(L.T("Shared this PC's settings at {0}.", DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The sync app may be busy with the file; the next change tries again.
            SetStatus(L.T("The sync folder can't be written: {0}", ex.Message));
            Log.Warn($"Sync file not written: {ex.Message}");
        }
    }

    private void Read()
    {
        if (_folder is null || Config.SyncDeviceId is not { } device) return;
        string text;
        try
        {
            if (!File.Exists(FilePath)) return;
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still being written or synced; the watcher calls again when it's done.
            Log.Debug($"Sync file not read yet: {ex.Message}");
            return;
        }
        if (SyncFile.Parse(text) is not { } file)
        {
            SetStatus(L.T("The shared settings file is damaged or from a newer DockHub; it was left as it is."));
            return;
        }
        // The file already has this PC's settings (it wrote them, or took them before).
        if (file.Settings.ToJsonString() == SyncMerge.Extract(Settings.ConfigJson()).ToJsonString())
        {
            _lastShared = file.Settings.ToJsonString();
            return;
        }
        if (!file.IsNewFor(device, Config.SyncAppliedAt)) return;

        string from = string.IsNullOrEmpty(file.DeviceName) ? L.T("another PC") : file.DeviceName;
        Settings.ApplyShared(file.Settings, L.T("Settings from {0}", from));
        Config.SyncAppliedAt = file.UpdatedAt;
        _lastShared = SyncMerge.Extract(Settings.ConfigJson()).ToJsonString();
        SetStatus(L.T("Took the settings of {0} from {1}.", from, file.UpdatedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)));
        Log.Info($"Settings taken from {file.DeviceName} ({file.UpdatedAt:O})");
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }
}
