using System.Windows.Threading;
using Microsoft.Win32;

namespace CustomDock.Services;

/// <summary>
/// Tüm saat tabanlı widget'lar için tek bir paylaşılan zamanlayıcı.
/// Saniye başına hizalanır; saniye aboneliği yoksa dakika başına hizalanarak daha seyrek çalışır.
/// Hiç abone yoksa durur.
/// </summary>
public sealed class ClockService : IDisposable
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private readonly EventHandler _timeChangedHandler;
    private readonly PowerModeChangedEventHandler _powerModeChangedHandler;
    private EventHandler<DateTime>? _secondTick;
    private EventHandler<DateTime>? _minuteTick;
    private int _lastMinute = -1;
    private bool _disposed;

    public ClockService()
    {
        _timer.Tick += OnTick;
        // SystemEvents olayları ayrı bir iş parçacığında gelir; UI dispatcher'ına aktar.
        var dispatcher = _timer.Dispatcher;
        _timeChangedHandler = (_, _) => dispatcher.BeginInvoke(() => Raise(force: true));
        _powerModeChangedHandler = (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) dispatcher.BeginInvoke(() => Raise(force: true));
        };
        SystemEvents.TimeChanged += _timeChangedHandler;
        SystemEvents.PowerModeChanged += _powerModeChangedHandler;
    }

    /// <summary>Her saniye (saniye başında) tetiklenir.</summary>
    public event EventHandler<DateTime> SecondTick
    {
        add { _secondTick += value; Reschedule(); }
        remove { _secondTick -= value; Reschedule(); }
    }

    /// <summary>Her dakika başında tetiklenir.</summary>
    public event EventHandler<DateTime> MinuteTick
    {
        add { _minuteTick += value; Reschedule(); }
        remove { _minuteTick -= value; Reschedule(); }
    }

    private void OnTick(object? sender, EventArgs e) => Raise(force: false);

    private void Raise(bool force)
    {
        var now = DateTime.Now;
        _secondTick?.Invoke(this, now);
        if (force || now.Minute != _lastMinute)
        {
            _lastMinute = now.Minute;
            _minuteTick?.Invoke(this, now);
        }
        Reschedule();
    }

    private void Reschedule()
    {
        if (_secondTick is null && _minuteTick is null)
        {
            _timer.Stop();
            return;
        }

        var now = DateTime.Now;
        // +15 ms: zamanlayıcının sınırdan hemen önce tetiklenmesini önler.
        var interval = _secondTick is not null
            ? TimeSpan.FromMilliseconds(1000 - now.Millisecond + 15)
            : TimeSpan.FromMilliseconds((60 - now.Second) * 1000 - now.Millisecond + 15);

        _timer.Interval = interval;
        if (!_timer.IsEnabled) _timer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        SystemEvents.TimeChanged -= _timeChangedHandler;
        SystemEvents.PowerModeChanged -= _powerModeChangedHandler;
    }
}
