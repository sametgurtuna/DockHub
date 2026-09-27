namespace CustomDock.Services;

/// <summary>
/// Decides when to warn about a low device battery: once when a device drops to the threshold, and again only after
/// it has charged or climbed back above the threshold plus <see cref="RearmMargin"/>. A device that goes to sleep and
/// comes back at the same level is not announced twice.
/// </summary>
public sealed class LowBatteryAlerts
{
    public const int RearmMargin = 5;

    private readonly HashSet<string> _warned = new();

    /// <summary>Devices that just reached the threshold (0 turns warnings off).</summary>
    public IReadOnlyList<BatteryDeviceInfo> Check(IEnumerable<BatteryDeviceInfo> devices, int threshold)
    {
        var alerts = new List<BatteryDeviceInfo>();
        if (threshold <= 0) return alerts;

        foreach (var device in devices)
        {
            if (device.IsCharging || device.BatteryPercent > threshold + RearmMargin)
                _warned.Remove(device.Id);
            else if (device.BatteryPercent <= threshold && _warned.Add(device.Id))
                alerts.Add(device);
        }
        return alerts;
    }
}
