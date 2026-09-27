using System.Runtime.InteropServices;
using CustomDock.Native;

namespace CustomDock.Services;

/// <summary>
/// Overlapped reads kept pending on several HID collections of one device at once, so an answer is seen as soon as
/// it arrives on any of them (a Logitech receiver answers on its long-report collection but reports errors on the
/// short one). Dispose before closing the handles: it cancels the pending reads.
/// </summary>
internal sealed class HidReadGroup : IDisposable
{
    private sealed class PendingRead
    {
        public IntPtr Handle;
        public IntPtr Buffer;
        public IntPtr Overlapped;
        public IntPtr Event;
        public int Length;
        public bool Active;
    }

    private static readonly int OverlappedSize = Marshal.SizeOf<HidInterop.OVERLAPPED>();
    private readonly List<PendingRead> _reads = new();

    /// <summary>Starts reading input reports of <paramref name="reportLength"/> bytes from <paramref name="handle"/>.</summary>
    public void Add(IntPtr handle, int reportLength)
    {
        var read = new PendingRead
        {
            Handle = handle,
            Length = reportLength,
            Buffer = Marshal.AllocHGlobal(reportLength),
            Overlapped = Marshal.AllocHGlobal(OverlappedSize),
            Event = HidInterop.CreateEvent(IntPtr.Zero, true, false, null),
        };
        _reads.Add(read);
        Issue(read);
    }

    private static void Issue(PendingRead read)
    {
        HidInterop.ResetEvent(read.Event);
        Marshal.StructureToPtr(new HidInterop.OVERLAPPED { hEvent = read.Event }, read.Overlapped, false);
        bool ok = HidInterop.ReadFile(read.Handle, read.Buffer, (uint)read.Length, IntPtr.Zero, read.Overlapped);
        // On an overlapped handle a read that completes at once also signals the event.
        read.Active = ok || Marshal.GetLastWin32Error() == 997; // ERROR_IO_PENDING
    }

    /// <summary>
    /// The next input report from any collection, waiting at most <paramref name="timeoutMs"/>; null on timeout or when
    /// nothing can be read. A failed read returns an empty array and stops that collection.
    /// </summary>
    public byte[]? Next(uint timeoutMs)
    {
        var active = _reads.Where(r => r.Active).ToArray();
        if (active.Length == 0) return null;

        uint signaled = HidInterop.WaitForMultipleObjects((uint)active.Length, active.Select(r => r.Event).ToArray(), false, timeoutMs);
        if (signaled >= active.Length) return null; // WAIT_TIMEOUT or WAIT_FAILED

        var read = active[signaled];
        if (!HidInterop.GetOverlappedResult(read.Handle, read.Overlapped, out uint bytes, false) || bytes == 0)
        {
            read.Active = false;
            return Array.Empty<byte>();
        }

        var report = new byte[Math.Min((int)bytes, read.Length)];
        Marshal.Copy(read.Buffer, report, 0, report.Length);
        Issue(read);
        return report;
    }

    public void Dispose()
    {
        foreach (var read in _reads)
        {
            if (read.Active)
            {
                HidInterop.CancelIoEx(read.Handle, read.Overlapped);
                HidInterop.GetOverlappedResult(read.Handle, read.Overlapped, out _, true);
            }
            HidInterop.CloseHandle(read.Event);
            Marshal.FreeHGlobal(read.Buffer);
            Marshal.FreeHGlobal(read.Overlapped);
        }
        _reads.Clear();
    }
}
