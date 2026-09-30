using System.Net.NetworkInformation;
using System.Windows.Threading;
using CustomDock.Core;

namespace CustomDock.Services;

/// <summary>The last replies of a ping target: a round-trip time in milliseconds, or null for a lost packet.</summary>
public sealed class PingStats
{
    public const int Window = 30;

    private readonly Queue<double?> _samples = new();

    public void Add(double? milliseconds)
    {
        _samples.Enqueue(milliseconds);
        while (_samples.Count > Window) _samples.Dequeue();
    }

    public int Count => _samples.Count;

    /// <summary>The last reply (null: none yet, or it was lost).</summary>
    public double? Latest => _samples.Count == 0 ? null : _samples.Last();

    public bool LastLost => _samples.Count > 0 && _samples.Last() is null;

    private IEnumerable<double> Replies => _samples.Where(s => s is not null).Select(s => s!.Value);

    public double? Average => Replies.Any() ? Replies.Average() : null;

    public double? Min => Replies.Any() ? Replies.Min() : null;

    public double? Max => Replies.Any() ? Replies.Max() : null;

    /// <summary>Lost packets, in percent of the samples.</summary>
    public double Loss => _samples.Count == 0 ? 0 : _samples.Count(s => s is null) * 100.0 / _samples.Count;

    /// <summary>How much the time changes from one reply to the next (the mean difference).</summary>
    public double? Jitter
    {
        get
        {
            var replies = Replies.ToList();
            if (replies.Count < 2) return null;
            double sum = 0;
            for (int i = 1; i < replies.Count; i++) sum += Math.Abs(replies[i] - replies[i - 1]);
            return sum / (replies.Count - 1);
        }
    }

    /// <summary>For the trend line: replies as they are, lost packets as the slowest reply.</summary>
    public IReadOnlyList<double> History
    {
        get
        {
            double lost = Max ?? 0;
            return _samples.Select(s => s ?? lost).ToList();
        }
    }

    /// <summary>Good (under 60 ms), fair (under 150 ms) or poor: the brush the widget shows the time in.</summary>
    public static string BrushFor(double? milliseconds) => milliseconds switch
    {
        null => "AccentRedBrush",
        < 60 => "AccentGreenBrush",
        < 150 => "AccentOrangeBrush",
        _ => "AccentRedBrush",
    };
}

/// <summary>Pings one target every five seconds while a widget watches it and a dock is on screen.</summary>
public sealed class PingMonitor
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const int TimeoutMs = 2000;

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = Interval };
    private bool _pinging;

    internal PingMonitor(string target)
    {
        Target = target;
        _timer.Tick += (_, _) => _ = SampleAsync();
    }

    public string Target { get; }

    public PingStats Stats { get; } = new();

    /// <summary>Why the last ping failed (unknown host...), or null.</summary>
    public string? Error { get; private set; }

    public event Action? Updated;

    internal int Watchers { get; set; }

    internal void Start()
    {
        _timer.Start();
        _ = SampleAsync();
    }

    internal void Stop() => _timer.Stop();

    private async Task SampleAsync()
    {
        // Nothing shows the time while every dock is hidden.
        if (_pinging || !DockVisibility.IsAnyDockVisible) return;
        _pinging = true;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(Target, TimeoutMs).ConfigureAwait(true);
            Stats.Add(reply.Status == IPStatus.Success ? reply.RoundtripTime : null);
            Error = null;
        }
        catch (Exception ex) when (ex is PingException or ArgumentException or InvalidOperationException)
        {
            // An unknown name or no network: a lost packet, with the reason in the tooltip.
            Stats.Add(null);
            Error = (ex.InnerException ?? ex).Message;
        }
        finally
        {
            _pinging = false;
        }
        Updated?.Invoke();
    }
}

/// <summary>The ping targets widgets watch; widgets with the same target share one monitor.</summary>
public sealed class PingService
{
    public const string DefaultTarget = "1.1.1.1";

    private readonly Dictionary<string, PingMonitor> _monitors = new(StringComparer.OrdinalIgnoreCase);

    public PingMonitor Watch(string target)
    {
        target = string.IsNullOrWhiteSpace(target) ? DefaultTarget : target.Trim();
        if (!_monitors.TryGetValue(target, out var monitor))
        {
            monitor = new PingMonitor(target);
            _monitors[target] = monitor;
        }
        if (monitor.Watchers++ == 0) monitor.Start();
        return monitor;
    }

    public void Unwatch(PingMonitor monitor)
    {
        if (--monitor.Watchers > 0) return;
        monitor.Stop();
        _monitors.Remove(monitor.Target);
    }
}
