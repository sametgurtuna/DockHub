using CustomDock.Core;
using Windows.Gaming.Input;
using Windows.System.Power;

namespace CustomDock.Services;

public sealed partial class DeviceBatteryService
{
    private const ushort SonyVendorId = 0x054C;

    /// <summary>Rescans when a controller is connected or removed; also wakes up the controller list, which starts empty.</summary>
    private void WatchGamepads()
    {
        try
        {
            RawGameController.RawGameControllerAdded += (_, _) => { if (_listening) Refresh(); };
            RawGameController.RawGameControllerRemoved += (_, _) => { if (_listening) Refresh(); };
        }
        catch (Exception ex)
        {
            Log.Debug($"Game controller events unavailable: {ex.Message}");
        }
    }

    /// <summary>Xbox and other controllers that report a battery to Windows (Windows.Gaming.Input).</summary>
    private static List<BatteryDeviceInfo> ScanGamepads()
    {
        var result = new List<BatteryDeviceInfo>();
        try
        {
            foreach (var controller in RawGameController.RawGameControllers)
            {
                if (controller.HardwareVendorId == SonyVendorId) continue; // read over HID with their charging state

                var report = controller.TryGetBatteryReport();
                if (report is null || report.Status == BatteryStatus.NotPresent) continue;
                if (BatteryProtocols.GamepadPercent(report.RemainingCapacityInMilliwattHours, report.FullChargeCapacityInMilliwattHours) is not { } percent)
                    continue;

                string name = string.IsNullOrWhiteSpace(controller.DisplayName) ? L.T("Controller") : controller.DisplayName;
                string id = "pad-" + StableHash(controller.NonRoamableId ?? $"{controller.HardwareVendorId:x4}{controller.HardwareProductId:x4}");
                result.Add(new BatteryDeviceInfo(id, name, percent, report.Status == BatteryStatus.Charging, false, BatteryDeviceKind.Controller));
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Game controller battery read failed: {ex.Message}");
        }
        return result;
    }
}
