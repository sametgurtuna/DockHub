using System.Diagnostics;
using System.Windows.Controls;
using CustomDock.Core;
using CustomDock.Services;

namespace CustomDock.Dock;

/// <summary>
/// Always-on network status glyph (ethernet / Wi-Fi / disconnected) shown next to the pinned tray icons.
/// Unlike <see cref="TrayIconView"/>, this doesn't depend on Windows exposing a real notify icon for the connection.
/// </summary>
public sealed class NetworkStatusIconView : StatusIconViewBase
{
    protected override void Attach() => AppServices.NetworkStatus.Changed += RefreshSoon;

    protected override void Detach() => AppServices.NetworkStatus.Changed -= RefreshSoon;

    protected override void Refresh()
    {
        var service = AppServices.NetworkStatus;
        var status = service.Status;
        (Glyph.Text, ToolTip) = status switch
        {
            NetworkStatusKind.Ethernet => ("", L.T("Ethernet connected")),
            NetworkStatusKind.Wifi => ("", L.T("Wi-Fi connected")),
            _ => ("", L.T("No network connection")),
        };
        if (status != NetworkStatusKind.Disconnected && !service.HasInternet)
            ToolTip = $"{ToolTip}\n{L.T("No internet access")}";
        Glyph.Opacity = status == NetworkStatusKind.Disconnected ? 0.45 : service.HasInternet ? 1.0 : 0.7;
    }

    protected override void OnClick() => OpenQuickSettings();

    protected override void BuildMenu(ItemCollection items)
    {
        items.Add(DockMenu.Item("Network and internet settings", "", () => OpenSettings("ms-settings:network")));
        items.Add(DockMenu.Item("Wi-Fi settings", "", () => OpenSettings("ms-settings:network-wifi")));
    }

    internal static void OpenSettings(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error(ex, $"Failed to open {uri}"); }
    }
}
