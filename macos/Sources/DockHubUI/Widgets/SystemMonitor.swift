import Foundation
import Combine
import DockHubCore
import DockHubPlatform

/// Tek bir paylasilan olcumcu: kac tane sistem widget'i olursa olsun
/// olcum bir kez alinir. CPU yuzdesi iki olcum arasindaki farktan ciktigi
/// icin duzenli araliklarla ornek almak dogru sonucu verir.
@MainActor
public final class SystemMonitor: ObservableObject {
    public static let shared = SystemMonitor()

    @Published public private(set) var sample = SystemSample(
        cpuPercent: 0, memoryUsedBytes: 0, memoryTotalBytes: 0)

    private let metrics = SystemMetrics()
    private var timer: Timer?

    private init() {}

    public func start(interval: TimeInterval) {
        guard timer == nil else { return }
        sample = metrics.sample()                      // ilk ornek: temel al
        timer = Timer.scheduledTimer(withTimeInterval: max(1, interval), repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
    }

    private func tick() { sample = metrics.sample() }
}
