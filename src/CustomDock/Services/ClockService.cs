using System.Windows.Threading;
using Microsoft.Win32;

namespace CustomDock.Services;

/// <summary>
/// Single shared timer for all clock-based widgets.
/// Aligned to each second; if there are no second subscriptions, aligned to the minute to run less frequently.
/// Stops if there are no subscribers.
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
        // SystemEvents events arrive on a separate thread; dispatch to UI dispatcher.
        var dispatcher = _timer.Dispatcher;
        _timeChangedHandler = (_, _) => dispatcher.BeginInvoke(() => Raise(force: true));
        _powerModeChangedHandler = (_, e) =>
        {
            if (e.Mode == PowerModes.Resume) dispatcher.BeginInvoke(() => Raise(force: true));
        };
        SystemEvents.TimeChanged += _timeChangedHandler;
        SystemEvents.PowerModeChanged += _powerModeChangedHandler;
    }

    /// <summary>Fired every second (at the start of each second).</summary>
    public event EventHandler<DateTime> SecondTick
    {
        add { _secondTick += value; Reschedule(); }
        remove { _secondTick -= value; Reschedule(); }
    }

    /// <summary>Fired at the start of each minute.</summary>
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
        // +15 ms: prevents timer from triggering right before the boundary.
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
