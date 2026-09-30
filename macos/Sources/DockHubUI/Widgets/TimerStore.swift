import Foundation
import Combine
import DockHubCore
import DockHubPlatform

/// Kronometre, geri sayim ve odak zamanlayicisinin calisma durumu.
/// Oge kimligine gore tutulur; ayni widget'in iki kopyasi birbirini etkilemez.
///
/// Zaman SAKLANMAZ, HESAPLANIR: biriken sure + (simdi - baslangic). Boylece
/// saniyede bir tick atan bir zamanlayiciya gerek kalmaz; gorunum TimelineView
/// ile kendini tazeler ve uygulama arka plandayken de dogru kalir.
@MainActor
public final class TimerStore: ObservableObject {
    public static let shared = TimerStore()

    public struct Run: Sendable {
        var accumulated: TimeInterval = 0
        var startedAt: Date?
        var running: Bool { startedAt != nil }
    }

    @Published private var runs: [String: Run] = [:]
    private init() {}

    public func isRunning(_ id: String) -> Bool { runs[id]?.running ?? false }

    public func elapsed(_ id: String, now: Date = .now) -> TimeInterval {
        guard let r = runs[id] else { return 0 }
        return r.accumulated + (r.startedAt.map { now.timeIntervalSince($0) } ?? 0)
    }

    /// Baslat/duraklat. Geri sayim ve odak icin bitis bildirimi de burada
    /// planlanir ve duraklatilinca iptal edilir.
    public func toggle(_ id: String, notifyAfter: TimeInterval? = nil,
                       baslik: String = "", metin: String = "") {
        var r = runs[id] ?? Run()
        if let started = r.startedAt {
            r.accumulated += Date().timeIntervalSince(started)
            r.startedAt = nil
            Notifier.iptal(id: id)
        } else {
            r.startedAt = Date()
            if let kalan = notifyAfter, kalan > 0 {
                Notifier.gonder(baslik: baslik, metin: metin, after: kalan, id: id)
            }
        }
        runs[id] = r
    }

    public func reset(_ id: String) {
        runs[id] = Run()
        Notifier.iptal(id: id)
    }
}
