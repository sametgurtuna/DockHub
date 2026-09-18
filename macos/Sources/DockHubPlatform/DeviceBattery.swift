import Foundation
import IOKit
import DockHubCore

public struct PeripheralBattery: Sendable, Identifiable, Equatable {
    public let id: String          // urun adi benzersiz kabul edilir
    public let name: String
    public let percent: Int
}

/// Bagli cihazlarin (fare, klavye, kulaklik) pil seviyeleri.
/// Windows karsiligi: Native/HidInterop.cs (hid.dll + setupapi.dll ile HID raporu).
/// Eslestirme ag-hid-battery (partial):
///   - Bluetooth cevre birimleri: IORegistry'de AppleDeviceManagementHIDEventService
///     girdisindeki BatteryPercent - IZIN GEREKTIRMEZ, burada uygulanan yol budur.
///   - Saticiya ozel USB dongle (HyperX gibi): IOHIDManager ile ham rapor okumak
///     ve Girdi Izleme izni gerekir; bu surumde YAPILMADI ve uydurma deger uretilmez.
public enum DeviceBattery {
    private static let serviceClass = "AppleDeviceManagementHIDEventService"

    public static func read() -> [PeripheralBattery] {
        guard let matching = IOServiceMatching(serviceClass) else { return [] }
        var iterator: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, matching, &iterator) == KERN_SUCCESS
        else { return [] }
        defer { IOObjectRelease(iterator) }

        var sonuc: [PeripheralBattery] = []
        while case let entry = IOIteratorNext(iterator), entry != 0 {
            defer { IOObjectRelease(entry) }
            guard let yuzde = intProperty(entry, "BatteryPercent") else { continue }
            let ad = stringProperty(entry, "Product")
                ?? stringProperty(entry, "DeviceAddress")
                ?? "Bilinmeyen cihaz"
            sonuc.append(PeripheralBattery(id: ad, name: ad, percent: yuzde))
        }
        return sonuc.sorted { $0.percent < $1.percent }
    }

    private static func intProperty(_ entry: io_registry_entry_t, _ key: String) -> Int? {
        guard let v = IORegistryEntryCreateCFProperty(entry, key as CFString,
                                                      kCFAllocatorDefault, 0)?
            .takeRetainedValue() else { return nil }
        return (v as? NSNumber)?.intValue
    }

    private static func stringProperty(_ entry: io_registry_entry_t, _ key: String) -> String? {
        guard let v = IORegistryEntryCreateCFProperty(entry, key as CFString,
                                                      kCFAllocatorDefault, 0)?
            .takeRetainedValue() else { return nil }
        return v as? String
    }
}
