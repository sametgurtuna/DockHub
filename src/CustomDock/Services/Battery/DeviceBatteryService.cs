using System.Windows;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>
/// Battery levels of peripherals: HID devices from <see cref="BatteryDeviceCatalog"/> (HyperX headsets, Compx mice)
/// and Bluetooth devices Windows reports a level for. Scans every 30 seconds on a worker thread.
/// </summary>
public sealed partial class DeviceBatteryService
{
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private BatteryDeviceCatalog _catalog = BatteryDeviceCatalog.BuiltIn;
    private DateTime _catalogStamp = DateTime.MinValue;

    public DeviceBatteryService()
    {
        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        // Initial scan
        Refresh();
    }

    public IReadOnlyList<BatteryDeviceInfo> Devices { get; private set; } = Array.Empty<BatteryDeviceInfo>();

    public BatteryDeviceInfo? PrimaryDevice => Devices.FirstOrDefault();

    public event EventHandler? Updated;

    private int _isRefreshing;

    public Task RefreshAsync() => Task.Run(async () =>
    {
        // One scan at a time: the timer, the panel and the refresh button can all ask at once.
        if (Interlocked.Exchange(ref _isRefreshing, 1) == 1) return;
        try
        {
            var catalog = CurrentCatalog();
            var list = new List<BatteryDeviceInfo>();

            // 1. HID devices from the catalog (HyperX headsets, Compx mice), filtered by the ids in their paths
            var hidPaths = EnumerateHidPaths();
            list.AddRange(ScanHyperX(hidPaths, catalog));
            list.AddRange(ScanCompxMice(hidPaths, catalog));

            // 2. Bluetooth (classic and Low Energy) devices Windows reports a battery level for
            list.AddRange(await ScanBluetoothDevicesAsync().ConfigureAwait(false));

            Devices = list;
            Application.Current?.Dispatcher.BeginInvoke(() => Updated?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to scan device batteries");
        }
        finally
        {
            Volatile.Write(ref _isRefreshing, 0);
        }
    });

    public void Refresh() => _ = RefreshAsync();

    /// <summary>The catalog, re-read when the user's battery-devices.json appears, changes or goes away.</summary>
    private BatteryDeviceCatalog CurrentCatalog()
    {
        DateTime stamp;
        try { stamp = File.Exists(BatteryDeviceCatalog.UserFile) ? File.GetLastWriteTimeUtc(BatteryDeviceCatalog.UserFile) : DateTime.MinValue; }
        catch { stamp = DateTime.MinValue; }

        if (stamp != _catalogStamp)
        {
            _catalogStamp = stamp;
            _catalog = BatteryDeviceCatalog.Load();
            if (stamp != DateTime.MinValue) Log.Info($"Battery device catalog: {_catalog.Entries.Count} entries (with {BatteryDeviceCatalog.UserFile})");
        }
        return _catalog;
    }
}
