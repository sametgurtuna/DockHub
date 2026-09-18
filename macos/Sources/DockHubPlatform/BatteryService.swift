import Foundation
import IOKit.ps
import DockHubCore

public struct BatteryState: Sendable {
    public let percent: Int
    public let isCharging: Bool
    public let isPluggedIn: Bool
    /// Kalan dakika; bilinmiyorsa nil (hesaplaniyor veya sinirsiz).
    public let minutesRemaining: Int?
    /// Makinede hic pil yoksa (masaustu Mac) false.
    public let hasBattery: Bool

    public static let none = BatteryState(percent: 0, isCharging: false, isPluggedIn: true,
                                          minutesRemaining: nil, hasBattery: false)
}

/// Pil durumu. Windows karsiligi: Services/SystemMonitorService.cs icindeki pil kismi.
/// Eslestirme ag-battery: IOKit IOPSCopyPowerSourcesInfo. Izin gerektirmez.
public enum BatteryService {
    public static func read() -> BatteryState {
        guard let blob = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
              let list = IOPSCopyPowerSourcesList(blob)?.takeRetainedValue() as? [CFTypeRef],
              let first = list.first,
              let desc = IOPSGetPowerSourceDescription(blob, first)?.takeUnretainedValue()
                         as? [String: Any]
        else { return .none }

        let current = desc[kIOPSCurrentCapacityKey as String] as? Int ?? 0
        let max = desc[kIOPSMaxCapacityKey as String] as? Int ?? 100
        let percent = max > 0 ? Int((Double(current) / Double(max) * 100).rounded()) : 0

        let state = desc[kIOPSPowerSourceStateKey as String] as? String
        let plugged = state == (kIOPSACPowerValue as String)
        let charging = desc[kIOPSIsChargingKey as String] as? Bool ?? false

        // -1 = hesaplaniyor, sinirsiz veya bilinmiyor
        let raw = desc[kIOPSTimeToEmptyKey as String] as? Int ?? -1
        let rawFull = desc[kIOPSTimeToFullChargeKey as String] as? Int ?? -1
        let mins = charging ? rawFull : raw
        return BatteryState(percent: percent, isCharging: charging, isPluggedIn: plugged,
                            minutesRemaining: mins > 0 ? mins : nil, hasBattery: true)
    }
}
