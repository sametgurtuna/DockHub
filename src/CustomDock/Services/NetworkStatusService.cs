using System.Net.NetworkInformation;
using System.Windows;
using CustomDock.Core;
using Windows.Networking.Connectivity;
using WinRtNetworkInformation = Windows.Networking.Connectivity.NetworkInformation;

namespace CustomDock.Services;

public enum NetworkStatusKind { Ethernet, Wifi, Disconnected }

/// <summary>
/// Live connection type (wired / wireless / none), shown as its own tray-style icon.
/// Independent of Windows' own promoted notification-area icons, which don't always expose a network icon to enumerate.
/// Reads Windows' connection profiles, like the taskbar does, so virtual adapters (Hyper-V, WSL, VPN, VirtualBox)
/// don't pass for a cable; falls back to scanning the adapters if that API fails.
/// </summary>
public sealed class NetworkStatusService
{
    private const uint IanaEthernet = 6;
    private const uint IanaWifi = 71;

    public NetworkStatusService()
    {
        NetworkChange.NetworkAddressChanged += (_, _) => Refresh();
        NetworkChange.NetworkAvailabilityChanged += (_, _) => Refresh();
        try { WinRtNetworkInformation.NetworkStatusChanged += _ => Refresh(); }
        catch (Exception ex) { Log.Error(ex, "Failed to watch network status"); }
        Refresh();
    }

    public NetworkStatusKind Status { get; private set; } = NetworkStatusKind.Disconnected;

    /// <summary>False when connected to a network that doesn't reach the internet (captive portal, no uplink).</summary>
    public bool HasInternet { get; private set; }

    public event EventHandler? Changed;

    private void Refresh()
    {
        var (kind, internet) = ReadFromProfiles() ?? ReadFromAdapters();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Apply(kind, internet);
        else dispatcher.BeginInvoke(() => Apply(kind, internet));
    }

    /// <summary>Physical connections with network access; a cable wins over Wi-Fi, as on the taskbar.</summary>
    private static (NetworkStatusKind, bool)? ReadFromProfiles()
    {
        try
        {
            var kind = NetworkStatusKind.Disconnected;
            bool internet = false;
            foreach (var profile in WinRtNetworkInformation.GetConnectionProfiles())
            {
                var level = profile.GetNetworkConnectivityLevel();
                if (level == NetworkConnectivityLevel.None) continue;

                uint iana = profile.NetworkAdapter?.IanaInterfaceType ?? 0;
                NetworkStatusKind? type = profile.IsWlanConnectionProfile || iana == IanaWifi ? NetworkStatusKind.Wifi
                    : iana == IanaEthernet ? NetworkStatusKind.Ethernet
                    : null; // VPN, tunnels and other virtual adapters ride on a physical connection
                if (type is null) continue;

                if (type == NetworkStatusKind.Ethernet || kind == NetworkStatusKind.Disconnected) kind = type.Value;
                internet |= level == NetworkConnectivityLevel.InternetAccess;
            }

            // Only a VPN-style adapter reports access (e.g. a physical profile that is still settling): show it as wired.
            if (kind == NetworkStatusKind.Disconnected && WinRtNetworkInformation.GetInternetConnectionProfile() is { } active
                && active.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None)
            {
                kind = active.IsWlanConnectionProfile ? NetworkStatusKind.Wifi : NetworkStatusKind.Ethernet;
                internet = active.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
            }
            return (kind, internet);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read connection profiles");
            return null;
        }
    }

    private static (NetworkStatusKind, bool) ReadFromAdapters()
    {
        var kind = NetworkStatusKind.Disconnected;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                    IsVirtual(nic) || nic.GetIPProperties().GatewayAddresses.Count == 0)
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
        return (kind, kind != NetworkStatusKind.Disconnected);
    }

    private static readonly string[] VirtualAdapterWords =
        { "virtual", "hyper-v", "vethernet", "vmware", "virtualbox", "wsl", "tap-", "wireguard", "vpn", "bluetooth" };

    private static bool IsVirtual(NetworkInterface nic)
        => VirtualAdapterWords.Any(w => nic.Description.Contains(w, StringComparison.OrdinalIgnoreCase)
            || nic.Name.Contains(w, StringComparison.OrdinalIgnoreCase));

    private void Apply(NetworkStatusKind kind, bool internet)
    {
        if (kind == Status && internet == HasInternet) return;
        Status = kind;
        HasInternet = internet;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
