using System.Windows;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>
/// Battery levels of peripherals: HID devices from <see cref="BatteryDeviceCatalog"/> (HyperX, Compx, PlayStation,
/// Razer, Logitech and catalog-defined requests), game controllers Windows reports on, and Bluetooth devices.
/// Scans every 30 seconds on a worker thread, only while something listens to <see cref="Updated"/>.
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
    }

    public IReadOnlyList<BatteryDeviceInfo> Devices { get; private set; } = Array.Empty<BatteryDeviceInfo>();

    public BatteryDeviceInfo? PrimaryDevice => Devices.FirstOrDefault();

    private EventHandler? _updated;
    private bool _watchingGamepads;
    private volatile bool _listening; // read by controller events, which arrive on other threads

    /// <summary>Raised on the UI thread after each scan. The first listener starts scanning, the last one stops it.</summary>
    public event EventHandler? Updated
    {
        add
        {
            _updated += value;
            if (_timer.IsEnabled) return;
            _listening = true;
            _timer.Start();
            if (!_watchingGamepads)
            {
                _watchingGamepads = true;
                WatchGamepads();
            }
            _ = FirstScanAsync();
        }
        remove
        {
            _updated -= value;
            if (_updated is not null) return;
            _listening = false;
            _timer.Stop();
        }
    }

    private async Task FirstScanAsync()
    {
        await StartupPacing.WaitAsync(StartupPacing.DeviceBatteries).ConfigureAwait(true);
        Refresh();
    }

    private int _isRefreshing;
    private int _refreshRequested;

    /// <summary>
    /// Scans on a worker thread. One scan runs at a time; a request that arrives during a scan (the timer, the panel,
    /// a controller being connected) runs another scan right after it instead of being dropped.
    /// </summary>
    public Task RefreshAsync() => Task.Run(async () =>
    {
        Volatile.Write(ref _refreshRequested, 1);
        while (Volatile.Read(ref _refreshRequested) == 1)
        {
            if (Interlocked.Exchange(ref _isRefreshing, 1) == 1) return; // the running scan picks the request up
            try
            {
                while (Interlocked.Exchange(ref _refreshRequested, 0) == 1)
                    await ScanAsync().ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _isRefreshing, 0);
            }
        }
    });

    private async Task ScanAsync()
    {
        try
        {
            var catalog = CurrentCatalog();
            var list = new List<BatteryDeviceInfo>();

            // 1. HID devices from the catalog, filtered by the ids in their paths before anything is opened
            var hidPaths = EnumerateHidPaths();
            list.AddRange(ScanHyperX(hidPaths, catalog));
            list.AddRange(ScanCompxMice(hidPaths, catalog));
            list.AddRange(ScanPlayStation(hidPaths, catalog));
            list.AddRange(ScanRazer(hidPaths, catalog));
            list.AddRange(ScanLogitech(hidPaths, catalog));
            list.AddRange(ScanHidRequests(hidPaths, catalog));

            // 2. Game controllers that report a battery to Windows (Xbox)
            list.AddRange(ScanGamepads());

            // 3. Bluetooth (classic and Low Energy) devices; a controller or HID device read above is not listed twice
            var bluetooth = await ScanBluetoothDevicesAsync().ConfigureAwait(false);
            list.AddRange(bluetooth.Where(bt => !list.Any(d => string.Equals(d.Name, bt.Name, StringComparison.OrdinalIgnoreCase))));

            Devices = list;
            Application.Current?.Dispatcher.BeginInvoke(() => _updated?.Invoke(this, EventArgs.Empty));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to scan device batteries");
        }
    }

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
