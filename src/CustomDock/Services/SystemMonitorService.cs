using System.Runtime.InteropServices;
using System.Windows.Threading;
using CustomDock.Native;
using Forms = System.Windows.Forms;

namespace CustomDock.Services;

public readonly record struct SystemStats(
    double CpuPercent,
    double RamPercent,
    double RamUsedGb,
    double RamTotalGb,
    double DiskPercent,
    double? BatteryPercent,
    bool IsCharging);

/// <summary>
/// Measures CPU, RAM, disk, and battery state. Uses GetSystemTimes / GlobalMemoryStatusEx instead of PerformanceCounter:
/// no initial measurement delay, language-independent, and very low overhead. Runs only while subscribed.
/// </summary>
public sealed class SystemMonitorService
{
    private static readonly TimeSpan DiskInterval = TimeSpan.FromSeconds(60);

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private EventHandler<SystemStats>? _updated;
    private ulong _lastIdle, _lastKernel, _lastUser;
    private double _diskPercent;
    private DateTime _lastDiskSample = DateTime.MinValue;
    private bool _diskSampling;

    public SystemMonitorService()
    {
        _timer.Tick += (_, _) => Sample();
        UpdateInterval = TimeSpan.FromSeconds(2);
    }

    public SystemStats Current { get; private set; }

    public TimeSpan UpdateInterval
    {
        get => _timer.Interval;
        // Performance: does not sample faster than 1 second.
        set => _timer.Interval = value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : value;
    }

    public event EventHandler<SystemStats> Updated
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
        double cpu = Current.CpuPercent;
        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong idleDelta = idle.Value - _lastIdle;
            ulong totalDelta = (kernel.Value - _lastKernel) + (user.Value - _lastUser); // kernel time includes idle
            if (_lastKernel != 0 && totalDelta > 0)
                cpu = Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0, 100);
            _lastIdle = idle.Value;
            _lastKernel = kernel.Value;
            _lastUser = user.Value;
        }

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        double ramPercent = 0, usedGb = 0, totalGb = 0;
        if (NativeMethods.GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0)
        {
            totalGb = mem.ullTotalPhys / 1024d / 1024 / 1024;
            usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1024d / 1024 / 1024;
            ramPercent = usedGb / totalGb * 100.0;
        }

        CheckDiskSample();

        double? battery = null;
        bool charging = false;
        var power = Forms.SystemInformation.PowerStatus;
        if (!power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery) &&
            !power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.Unknown))
        {
            battery = Math.Clamp(power.BatteryLifePercent * 100, 0, 100);
            charging = power.PowerLineStatus == Forms.PowerLineStatus.Online;
        }

        Current = new SystemStats(cpu, ramPercent, usedGb, totalGb, _diskPercent, battery, charging);
        _updated?.Invoke(this, Current);
    }

    private void CheckDiskSample()
    {
        if (_diskSampling || DateTime.UtcNow - _lastDiskSample <= DiskInterval) return;
        _lastDiskSample = DateTime.UtcNow;
        _diskSampling = true;

        Task.Run(() =>
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
                if (drive.IsReady && drive.TotalSize > 0)
                {
                    _diskPercent = (1 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100;
                }
            }
            catch { }
            finally
            {
                _diskSampling = false;
            }
        });
    }
}
