using System.Windows.Threading;

namespace CustomDock.Core;

/// <summary>
/// Tek örnek kontrolü. İkinci örnek açılırsa mevcut örneğe "ayarları göster" sinyali gönderir;
/// "--exit" ile açılırsa mevcut örneği düzgünce kapatır (görev çubuğu geri gelir).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\DockHub.SingleInstance.5B1E7C1A";
    private const string EventName = @"Local\DockHub.Activate.5B1E7C1A";
    private const string ExitEventName = @"Local\DockHub.Exit.5B1E7C1A";
    private const string PinEventName = @"Local\DockHub.Pin.5B1E7C1A";

    private readonly Mutex _mutex = new(false, MutexName);
    private EventWaitHandle? _event;
    private EventWaitHandle? _exitEvent;
    private EventWaitHandle? _pinEvent;
    private bool _owned;

    public bool TryAcquire()
    {
        try
        {
            _owned = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // Önceki örnek çökmüş; sahiplik bize geçti.
            _owned = true;
        }

        if (_owned)
        {
            _event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            _pinEvent = new EventWaitHandle(false, EventResetMode.AutoReset, PinEventName);
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

    public void Listen(Dispatcher dispatcher, Action onActivate, Action onExit, Action onPin)
    {
        if (_event is null || _exitEvent is null || _pinEvent is null) return;
        var handles = new WaitHandle[] { _event, _exitEvent, _pinEvent };
        var thread = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    int index = WaitHandle.WaitAny(handles);
                    dispatcher.BeginInvoke(index switch { 0 => onActivate, 1 => onExit, _ => onPin });
                    if (index == 1) break;
                }
            }
            catch (ObjectDisposedException)
            {
                // kapanış
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
            try { _mutex.ReleaseMutex(); } catch { /* yoksay */ }
        }
        _mutex.Dispose();
        _event?.Dispose();
        _exitEvent?.Dispose();
        _pinEvent?.Dispose();
    }
}
