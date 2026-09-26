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
        var status = AppServices.NetworkStatus.Status;
        (Glyph.Text, ToolTip) = status switch
        {
            NetworkStatusKind.Ethernet => ("", "Ethernet connected"),
            NetworkStatusKind.Wifi => ("", "Wi-Fi connected"),
            _ => ("", "No network connection"),
        };
        Glyph.Opacity = status == NetworkStatusKind.Disconnected ? 0.45 : 1.0;
    }

    protected override void OnClick()
    {
        try { AppServices.Shell?.ShowQuickSettings(); }
        catch (Exception ex) { Log.Error(ex, "Failed to open quick settings from network icon"); }
    }

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
