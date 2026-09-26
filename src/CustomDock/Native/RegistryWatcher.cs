using Microsoft.Win32;
using static CustomDock.Native.NativeMethods;

namespace CustomDock.Native;

/// <summary>
/// Raises <see cref="Changed"/> when a registry key (optionally its whole subtree) changes, without polling.
/// A background thread waits on RegNotifyChangeKeyValue; the event is raised on that thread.
/// </summary>
public sealed class RegistryWatcher : IDisposable
{
    private readonly RegistryHive _hive;
    private readonly string _path;
    private readonly bool _subtree;
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _thread;

    public RegistryWatcher(RegistryHive hive, string path, bool subtree)
    {
        _hive = hive;
        _path = path;
        _subtree = subtree;
    }

    public event Action? Changed;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "DockHub registry watcher" };
        _thread.Start();
    }

    private void Run()
    {
        using var changed = new AutoResetEvent(false);
        var handles = new WaitHandle[] { changed, _stop };
        while (!_stop.WaitOne(0))
        {
            RegistryKey? key = null;
            try
            {
                key = RegistryKey.OpenBaseKey(_hive, RegistryView.Default).OpenSubKey(_path, writable: false);
                if (key is null)
                {
                    // The key doesn't exist yet (e.g. no app has used the microphone): look again later.
                    if (_stop.WaitOne(TimeSpan.FromSeconds(30))) return;
                    continue;
                }

                int result = RegNotifyChangeKeyValue(key.Handle, _subtree, REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET,
                    changed.SafeWaitHandle, asynchronous: true);
                if (result != 0)
                {
                    if (_stop.WaitOne(TimeSpan.FromSeconds(30))) return;
                    continue;
                }

                if (WaitHandle.WaitAny(handles) == 1) return;
                try { Changed?.Invoke(); }
                catch (Exception ex) { Core.Log.Error(ex, $"Registry change handler failed: {_path}"); }
            }
            catch (Exception ex)
            {
                Core.Log.Error(ex, $"Registry watcher failed: {_path}");
                if (_stop.WaitOne(TimeSpan.FromSeconds(30))) return;
            }
            finally
            {
                key?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        _stop.Set();
    }
}
