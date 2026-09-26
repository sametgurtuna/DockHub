using System.Runtime.InteropServices;
using System.Windows.Threading;
using CustomDock.Core;
using Microsoft.Win32;

namespace CustomDock.Services;

public sealed record GpuStats(bool Available, double Utilization, double MemoryUsedGb, double MemoryTotalGb, string Name)
{
    public static readonly GpuStats Empty = new(false, 0, 0, 0, "");

    public double MemoryPercent => MemoryTotalGb > 0 ? Math.Clamp(MemoryUsedGb / MemoryTotalGb * 100, 0, 100) : 0;
}

/// <summary>
/// GPU load and video memory from the same performance counters Task Manager uses ("GPU Engine" and "GPU Adapter
/// Memory"). Load is the busiest engine (3D, video decode, copy...), summed over all processes. Samples on a
/// background thread only while a widget listens and a dock is on screen.
/// </summary>
public sealed class GpuMonitorService
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private EventHandler<GpuStats>? _updated;
    private IntPtr _query;
    private IntPtr _engineCounter;
    private IntPtr _memoryCounter;
    private bool _sampling;
    private bool _unavailable;
    private (string Name, double TotalGb)? _adapter;

    public GpuMonitorService()
    {
        _timer.Interval = TimeSpan.FromSeconds(2);
        _timer.Tick += (_, _) => Sample();
    }

    public GpuStats Current { get; private set; } = GpuStats.Empty;

    public TimeSpan UpdateInterval
    {
        get => _timer.Interval;
        set => _timer.Interval = value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : value;
    }

    public event EventHandler<GpuStats> Updated
    {
        add
        {
            _updated += value;
            if (!_timer.IsEnabled)
            {
                _timer.Start();
                Sample();
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
        if (_sampling || _unavailable || !DockVisibility.IsAnyDockVisible) return;
        _sampling = true;
        Task.Run(Collect).ContinueWith(task =>
        {
            _sampling = false;
            if (task.Result is { } stats)
            {
                Current = stats;
                _updated?.Invoke(this, stats);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private GpuStats? Collect()
    {
        try
        {
            if (_query == IntPtr.Zero)
            {
                if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0
                    || PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _engineCounter) != 0)
                {
                    _unavailable = true;
                    Log.Info("GPU performance counters are not available.");
                    return GpuStats.Empty;
                }
                PdhAddEnglishCounterW(_query, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out _memoryCounter);
                // Rates need two samples.
                PdhCollectQueryData(_query);
                Thread.Sleep(250);
                _adapter ??= ReadAdapter();
            }
            if (PdhCollectQueryData(_query) != 0) return null;

            // "pid_1234_luid_0x0_0xD1B5_phys_0_eng_3_engtype_3D": sum each engine over processes, then take the busiest.
            var engines = new Dictionary<string, double>();
            foreach (var (name, value) in ReadArray(_engineCounter))
            {
                int luid = name.IndexOf("luid_", StringComparison.Ordinal);
                int type = name.IndexOf("_engtype", StringComparison.Ordinal);
                string key = luid >= 0 && type > luid ? name[luid..type] : name;
                engines[key] = engines.GetValueOrDefault(key) + value;
            }
            double utilization = engines.Count == 0 ? 0 : Math.Clamp(engines.Values.Max(), 0, 100);

            double usedBytes = _memoryCounter == IntPtr.Zero ? 0 : ReadArray(_memoryCounter).Select(v => v.Value).DefaultIfEmpty(0).Max();
            var adapter = _adapter ?? ("", 0);
            return new GpuStats(true, utilization, usedBytes / (1024.0 * 1024 * 1024), adapter.TotalGb, adapter.Name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GPU sampling failed");
            _unavailable = true;
            return GpuStats.Empty;
        }
    }

    private static List<(string Name, double Value)> ReadArray(IntPtr counter)
    {
        var list = new List<(string, double)>();
        uint size = 0;
        int status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, out uint count, IntPtr.Zero);
        if (status != PDH_MORE_DATA || size == 0) return list;
        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE, ref size, out count, buffer) != 0) return list;
            // PDH_FMT_COUNTERVALUE_ITEM_W: name pointer, then {CStatus, padding, double} (24 bytes on x64).
            int itemSize = IntPtr.Size + 16;
            for (int i = 0; i < count; i++)
            {
                IntPtr item = buffer + i * itemSize;
                string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                int valueStatus = Marshal.ReadInt32(item + IntPtr.Size);
                if (valueStatus != 0) continue;
                double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + IntPtr.Size + 8));
                list.Add((name, value));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return list;
    }

    /// <summary>Name and dedicated memory of the display adapter with the most memory (the discrete GPU if any).</summary>
    private static (string Name, double TotalGb) ReadAdapter()
    {
        (string, double) best = ("", 0);
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null) return best;
            foreach (var sub in classKey.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                using var key = classKey.OpenSubKey(sub);
                if (key is null) continue;
                double bytes = key.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long q => q,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    _ => key.GetValue("HardwareInformation.MemorySize") switch
                    {
                        int d => (uint)d,
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => 0,
                    },
                };
                double gb = bytes / (1024.0 * 1024 * 1024);
                if (gb > best.Item2) best = (key.GetValue("DriverDesc") as string ?? "", gb);
            }
        }
        catch
        {
            // no access: memory total stays unknown
        }
        return best;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);
}
