using System.Windows;
using System.Windows.Controls;
using CustomDock.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CustomDock.Dock;

/// <summary>Battery glyph next to the tray; only shown on devices with a battery.</summary>
public sealed class BatteryStatusIconView : StatusIconViewBase
{
    // Segoe Fluent Icons: Battery0..Battery9 = E850..E859, Battery10 = E83F; charging 0..9 = E85A..E863, charging 10 = E83E.
    private const int Battery0 = 0xE850;
    private const int BatteryCharging0 = 0xE85A;

    /// <summary>True when the system reports a battery (desktops without one never show the icon).</summary>
    public static bool HasBattery
    {
        get
        {
            var status = Forms.SystemInformation.PowerStatus;
            return status.BatteryChargeStatus != Forms.BatteryChargeStatus.NoSystemBattery &&
                   status.BatteryChargeStatus != Forms.BatteryChargeStatus.Unknown;
        }
    }

    // Percentage changes raise no event; a slow timer keeps the level current while the icon is shown.
    private readonly System.Windows.Threading.DispatcherTimer _timer = new(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(60) };

    public BatteryStatusIconView()
    {
        _timer.Tick += (_, _) => Refresh();
    }

    protected override void Attach()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _timer.Start();
    }

    protected override void Detach()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _timer.Stop();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) => RefreshSoon();

    protected override void Refresh()
    {
        var status = Forms.SystemInformation.PowerStatus;
        bool charging = status.PowerLineStatus == Forms.PowerLineStatus.Online;
        int percent = (int)Math.Round(Math.Clamp(status.BatteryLifePercent, 0f, 1f) * 100);
        int step = Math.Clamp(percent / 10, 0, 10);

        Glyph.Text = char.ConvertFromUtf32(step == 10
            ? (charging ? 0xE83E : 0xE83F)
            : (charging ? BatteryCharging0 : Battery0) + step);

        string brush = !charging && percent <= 10 ? "AccentRedBrush"
            : !charging && percent <= 20 ? "AccentOrangeBrush"
            : "TextPrimaryBrush";
        Glyph.SetResourceReference(TextBlock.ForegroundProperty, brush);

        string remaining = !charging && status.BatteryLifeRemaining > 0
            ? $" · about {TimeSpan.FromSeconds(status.BatteryLifeRemaining):h\\:mm} left"
            : charging ? (percent >= 100 ? " · fully charged" : " · charging") : "";
        ToolTip = $"Battery {percent}%{remaining}";
    }

    protected override void OnClick() => NetworkStatusIconView.OpenSettings("ms-settings:batterysaver");

    protected override void BuildMenu(ItemCollection items)
    {
        items.Add(DockMenu.Item("Power and battery settings", "", () => NetworkStatusIconView.OpenSettings("ms-settings:batterysaver")));
        items.Add(DockMenu.Item("Quick settings", "", () =>
        {
            try { AppServices.Shell?.ShowQuickSettings(); }
            catch (Exception ex) { Log.Error(ex, "Failed to open quick settings from battery icon"); }
        }));
    }
}
