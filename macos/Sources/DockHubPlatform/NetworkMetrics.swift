import Darwin
import Foundation
import DockHubCore

public struct NetworkSample: Sendable {
    public let downBytesPerSec: Double
    public let upBytesPerSec: Double
    public let totalDown: UInt64
    public let totalUp: UInt64

    public init(downBytesPerSec: Double, upBytesPerSec: Double,
                totalDown: UInt64, totalUp: UInt64) {
        self.downBytesPerSec = downBytesPerSec; self.upBytesPerSec = upBytesPerSec
        self.totalDown = totalDown; self.totalUp = totalUp
    }
}

/// Ag hizi. Windows karsiligi: Services/NetworkMonitorService.cs
/// Eslestirme ag-system-metrics: getifaddrs + if_data sayac farki. Izin gerektirmez.
///
/// Sayaclar kumulatiftir; hiz ancak IKI olcum arasindaki farktan cikar.
/// Ilk ornek bu yuzden 0 doner.
public final class NetworkMetrics: @unchecked Sendable {
    private var previous: (down: UInt64, up: UInt64, at: Date)?
    private let lock = NSLock()

    public init() {}

    /// Loopback (lo0) ve kapali arayuzler sayilmaz; kalan hepsi toplanir.
    public static func totals() -> (down: UInt64, up: UInt64) {
        var ifaddr: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&ifaddr) == 0, let first = ifaddr else { return (0, 0) }
        defer { freeifaddrs(ifaddr) }

        var down: UInt64 = 0, up: UInt64 = 0
        for ptr in sequence(first: first, next: { $0.pointee.ifa_next }) {
            let flags = Int32(ptr.pointee.ifa_flags)
            guard flags & IFF_UP != 0, flags & IFF_LOOPBACK == 0 else { continue }
            guard ptr.pointee.ifa_addr?.pointee.sa_family == UInt8(AF_LINK) else { continue }
            guard let data = ptr.pointee.ifa_data?.assumingMemoryBound(to: if_data.self) else { continue }
            down &+= UInt64(data.pointee.ifi_ibytes)
            up &+= UInt64(data.pointee.ifi_obytes)
        }
        return (down, up)
    }

    public func sample() -> NetworkSample {
        let (d, u) = Self.totals()
        let now = Date()
        lock.lock(); defer { lock.unlock() }
        defer { previous = (d, u, now) }

        guard let p = previous else {
            return NetworkSample(downBytesPerSec: 0, upBytesPerSec: 0, totalDown: d, totalUp: u)
        }
        let dt = now.timeIntervalSince(p.at)
        guard dt > 0.01 else {
            return NetworkSample(downBytesPerSec: 0, upBytesPerSec: 0, totalDown: d, totalUp: u)
        }
        // Sayac tasmasi veya arayuz sifirlanmasi negatif fark verebilir; 0'a kirp.
        let dd = d >= p.down ? Double(d - p.down) : 0
        let du = u >= p.up ? Double(u - p.up) : 0
        return NetworkSample(downBytesPerSec: dd / dt, upBytesPerSec: du / dt,
                             totalDown: d, totalUp: u)
    }

    /// Insan okunur hiz: 1.2 MB/s gibi.
    public static func format(_ bytesPerSec: Double) -> String {
        let b = max(0, bytesPerSec)
        if b >= 1_048_576 { return String(format: "%.1f MB/s", b / 1_048_576) }
        if b >= 1024 { return String(format: "%.0f KB/s", b / 1024) }
        return String(format: "%.0f B/s", b)
    }
}

/// Disk doluluk orani. Windows karsiligi: Status widget'indaki disk halkasi.
public enum DiskInfo {
    /// Onyukleme biriminin kullanilan orani ve baytlari.
    public static func usage() -> (used: UInt64, total: UInt64)? {
        let url = URL(fileURLWithPath: "/")
        guard let v = try? url.resourceValues(forKeys: [
            .volumeTotalCapacityKey, .volumeAvailableCapacityForImportantUsageKey
        ]), let total = v.volumeTotalCapacity else { return nil }
        let bos = UInt64(v.volumeAvailableCapacityForImportantUsage ?? 0)
        let toplam = UInt64(total)
        return (toplam > bos ? toplam - bos : 0, toplam)
    }
}
