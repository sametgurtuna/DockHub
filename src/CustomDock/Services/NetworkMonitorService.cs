using System.Net.NetworkInformation;
using System.Windows.Threading;
using CustomDock.Core;

namespace CustomDock.Services;

public readonly record struct NetworkStats(double DownBytesPerSec, double UpBytesPerSec);

/// <summary>Tüm etkin ağ arabirimlerinin toplam indirme/yükleme hızı (saniyede bir, yalnızca abone varken).</summary>
public sealed class NetworkMonitorService
{
    public const int HistoryLength = 40;

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly Queue<NetworkStats> _history = new();
    private EventHandler<NetworkStats>? _updated;
    private long _lastReceived = -1, _lastSent = -1;
    private DateTime _lastSample;

    public NetworkMonitorService()
    {
        _timer.Tick += (_, _) => Sample();
    }

    public NetworkStats Current { get; private set; }

    public IReadOnlyCollection<NetworkStats> History => _history;

    public event EventHandler<NetworkStats> Updated
    {
        add
        {
            _updated += value;
            if (!_timer.IsEnabled)
            {
                Sample();
                _timer.Start();
            }
        }
        remove
        {
            _updated -= value;
            if (_updated is null) _timer.Stop();
        }
    }

    private void Sample()
    {
        long received = 0, sent = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                var stats = nic.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ağ istatistikleri okunamadı");
            return;
        }

        var now = DateTime.UtcNow;
        if (_lastReceived >= 0)
        {
            double seconds = Math.Max(0.25, (now - _lastSample).TotalSeconds);
            Current = new NetworkStats(
                Math.Max(0, received - _lastReceived) / seconds,
                Math.Max(0, sent - _lastSent) / seconds);
            _history.Enqueue(Current);
            while (_history.Count > HistoryLength) _history.Dequeue();
        }

        _lastReceived = received;
        _lastSent = sent;
        _lastSample = now;
        _updated?.Invoke(this, Current);
    }

    /// <summary>Hızı okunur biçime çevirir: (değer, birim).</summary>
    public static (string Value, string Unit) Format(double bytesPerSec)
    {
        double kb = bytesPerSec / 1024;
        if (kb < 1000) return (kb.ToString(kb < 10 ? "0.0" : "0"), "KB/s");
        double mb = kb / 1024;
        return (mb.ToString(mb < 10 ? "0.0" : "0"), "MB/s");
    }
}
