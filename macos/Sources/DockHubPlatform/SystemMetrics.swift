import Darwin
import Foundation
import DockHubCore

public struct SystemSample: Sendable {
    public let cpuPercent: Double        // 0...100
    public let memoryUsedBytes: UInt64
    public let memoryTotalBytes: UInt64

    public init(cpuPercent: Double, memoryUsedBytes: UInt64, memoryTotalBytes: UInt64) {
        self.cpuPercent = cpuPercent
        self.memoryUsedBytes = memoryUsedBytes
        self.memoryTotalBytes = memoryTotalBytes
    }

    public var memoryPercent: Double {
        memoryTotalBytes == 0 ? 0 : Double(memoryUsedBytes) / Double(memoryTotalBytes) * 100
    }
}

/// CPU ve bellek olcumleri. Windows karsiligi: Services/SystemMonitorService.cs
/// Eslestirme ag-system-metrics (mapped): host_statistics + host_statistics64.
/// Izin gerektirmez.
public final class SystemMetrics: @unchecked Sendable {
    /// CPU ticks kumulatiftir; yuzde ancak IKI olcum arasindaki FARKTAN cikar.
    /// Ilk ornek bu yuzden 0 doner, bu bir hata degil.
    private var previous: (user: UInt64, system: UInt64, idle: UInt64, nice: UInt64)?
    private let lock = NSLock()

    public init() {}

    public func sample() -> SystemSample {
        SystemSample(cpuPercent: cpuPercent(),
                     memoryUsedBytes: memoryUsed(),
                     memoryTotalBytes: ProcessInfo.processInfo.physicalMemory)
    }

    // ---- CPU: HOST_CPU_LOAD_INFO
    private func cpuPercent() -> Double {
        var size = mach_msg_type_number_t(MemoryLayout<host_cpu_load_info_data_t>.size
                                          / MemoryLayout<integer_t>.size)
        var info = host_cpu_load_info_data_t()
        let kr = withUnsafeMutablePointer(to: &info) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(size)) {
                host_statistics(mach_host_self(), HOST_CPU_LOAD_INFO, $0, &size)
            }
        }
        guard kr == KERN_SUCCESS else { return 0 }

        let now = (user: UInt64(info.cpu_ticks.0), system: UInt64(info.cpu_ticks.1),
                   idle: UInt64(info.cpu_ticks.2), nice: UInt64(info.cpu_ticks.3))

        lock.lock(); defer { lock.unlock() }
        defer { previous = now }
        guard let old = previous else { return 0 }

        let dUser = now.user &- old.user
        let dSystem = now.system &- old.system
        let dIdle = now.idle &- old.idle
        let dNice = now.nice &- old.nice
        let total = dUser + dSystem + dIdle + dNice
        guard total > 0 else { return 0 }
        return Double(dUser + dSystem + dNice) / Double(total) * 100
    }

    // ---- Bellek: HOST_VM_INFO64
    /// macOS'un "kullanilan bellek" tanimi: active + wired + sikistirilmis.
    /// Inactive ve free sayilmaz; Etkinlik Monitoru de boyle hesaplar.
    private func memoryUsed() -> UInt64 {
        var size = mach_msg_type_number_t(MemoryLayout<vm_statistics64_data_t>.size
                                          / MemoryLayout<integer_t>.size)
        var info = vm_statistics64_data_t()
        let kr = withUnsafeMutablePointer(to: &info) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(size)) {
                host_statistics64(mach_host_self(), HOST_VM_INFO64, $0, &size)
            }
        }
        guard kr == KERN_SUCCESS else { return 0 }
        // vm_kernel_page_size global degisken oldugu icin Swift 6'da erisilemiyor;
        // sysconf ayni degeri veriyor.
        let page = UInt64(sysconf(_SC_PAGESIZE))
        return (UInt64(info.active_count) + UInt64(info.wire_count)
                + UInt64(info.compressor_page_count)) * page
    }
}
