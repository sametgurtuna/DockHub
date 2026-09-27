using System.Windows.Threading;

namespace CustomDock.Core;

/// <summary>
/// Single-instance control. If a second instance is launched, signals the running instance to show settings;
/// if launched with "--exit", gracefully closes the running instance (restores taskbar).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\DockHub.SingleInstance.5B1E7C1A";
    private const string EventName = @"Local\DockHub.Activate.5B1E7C1A";
    private const string ExitEventName = @"Local\DockHub.Exit.5B1E7C1A";
    private const string PinEventName = @"Local\DockHub.Pin.5B1E7C1A";
    private const string WidgetEventName = @"Local\DockHub.Widget.5B1E7C1A";

    private readonly Mutex _mutex = new(false, MutexName);
    private EventWaitHandle? _event;
    private EventWaitHandle? _exitEvent;
    private EventWaitHandle? _pinEvent;
    private EventWaitHandle? _widgetEvent;
    private bool _owned;

    public bool TryAcquire()
    {
        try
        {
            _owned = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed; ownership transferred to us.
            _owned = true;
        }

        if (_owned)
        {
            _event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            _pinEvent = new EventWaitHandle(false, EventResetMode.AutoReset, PinEventName);
            _widgetEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WidgetEventName);
        }
        return _owned;
    }

    public static void SignalExisting()
    {
        if (EventWaitHandle.TryOpenExisting(EventName, out var handle))
        {
            using (handle)
                handle.Set();
        }
    }

    public static void SignalExit()
    {
        if (EventWaitHandle.TryOpenExisting(ExitEventName, out var handle))
        {
            using (handle)
                handle.Set();
        }
    }

    public static void SignalPin()
    {
        if (EventWaitHandle.TryOpenExisting(PinEventName, out var handle))
        {
            using (handle)
                handle.Set();
        }
    }

    /// <summary>Tells the running instance that a widget package is waiting in <see cref="RequestQueue.WidgetPackages"/>.</summary>
    public static void SignalWidget()
    {
        if (EventWaitHandle.TryOpenExisting(WidgetEventName, out var handle))
        {
            using (handle)
                handle.Set();
        }
    }

    public void Listen(Dispatcher dispatcher, Action onActivate, Action onExit, Action onPin, Action onWidget)
    {
        if (_event is null || _exitEvent is null || _pinEvent is null || _widgetEvent is null) return;
        var handles = new WaitHandle[] { _event, _exitEvent, _pinEvent, _widgetEvent };
        var thread = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    int index = WaitHandle.WaitAny(handles);
                    dispatcher.BeginInvoke(index switch { 0 => onActivate, 1 => onExit, 2 => onPin, _ => onWidget });
                    if (index == 1) break;
                }
            }
            catch (ObjectDisposedException)
            {
                // shutdown
            }
        })
        {
            IsBackground = true,
            Name = "DockHub.SingleInstance",
        };
        thread.Start();
    }

    public void Dispose()
    {
        if (_owned)
        {
            try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
        }
        _mutex.Dispose();
        _event?.Dispose();
        _exitEvent?.Dispose();
        _pinEvent?.Dispose();
        _widgetEvent?.Dispose();
    }
}
