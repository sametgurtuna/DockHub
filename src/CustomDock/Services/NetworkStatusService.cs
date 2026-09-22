using System.Net.NetworkInformation;
using System.Windows;
using CustomDock.Core;

namespace CustomDock.Services;

public enum NetworkStatusKind { Ethernet, Wifi, Disconnected }

/// <summary>
/// Live connection type (wired / wireless / none), shown as its own tray-style icon.
/// Independent of Windows' own promoted notification-area icons, which don't always expose a network icon to enumerate.
/// </summary>
public sealed class NetworkStatusService
{
    public NetworkStatusService()
    {
        NetworkChange.NetworkAddressChanged += (_, _) => Refresh();
        NetworkChange.NetworkAvailabilityChanged += (_, _) => Refresh();
        Refresh();
    }

    public NetworkStatusKind Status { get; private set; } = NetworkStatusKind.Disconnected;

    public event EventHandler? Changed;

    private void Refresh()
    {
        var kind = NetworkStatusKind.Disconnected;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    if (kind == NetworkStatusKind.Disconnected) kind = NetworkStatusKind.Wifi;
                }
                else
                {
                    kind = NetworkStatusKind.Ethernet;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read network status");
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Apply(kind);
        else dispatcher.BeginInvoke(() => Apply(kind));
    }

    private void Apply(NetworkStatusKind kind)
    {
        if (kind == Status) return;
        Status = kind;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
