import AppKit
import Combine
import DockHubCore
import DockHubPlatform

/// Dock'un canli durumu: ogeler ve hangi uygulamalarin calistigi.
/// Calisiyor bilgisi NSWorkspace bildirimleriyle guncellenir (yoklama yok).
@MainActor
public final class DockModel: ObservableObject {
    @Published public private(set) var items: [DockItem]
    @Published public private(set) var runningPaths: Set<String> = []

    public let config: AppConfig
    private let service: ConfigService?
    private let observers = ObserverBag()

    public init(config: AppConfig, items: [DockItem], service: ConfigService? = nil) {
        self.config = config
        self.items = items
        self.service = service
        refreshRunning()

        let nc = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.didLaunchApplicationNotification,
                     NSWorkspace.didTerminateApplicationNotification] {
            observers.add(nc.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.refreshRunning() }
            })
        }
    }

    public func refreshRunning() {
        var paths = Set<String>()
        for item in items where item.kind == .app {
            if let p = item.path, AppCatalog.isRunning(appAt: p) { paths.insert(p) }
        }
        runningPaths = paths
    }

    /// Bir widget kopyasinin ayarini kalici olarak gunceller.
    /// Ayar config.json icindeki o ogenin settings alanina yazilir; her kopya
    /// kendi ayarini tasidigi icin digerleri etkilenmez.
    public func setSetting(_ itemId: String, _ key: String, _ value: JSONValue) {
        guard let service else { return }
        do {
            try service.update { cfg in
                guard let i = cfg.items.firstIndex(where: { $0.id == itemId }) else { return }
                var dict: [String: JSONValue]
                if case .object(let d)? = cfg.items[i].settings { dict = d } else { dict = [:] }
                dict[key] = value
                cfg.items[i].settings = .object(dict)
            }
            items = service.config.items          // gorunumler tazelensin
        } catch {
            Log.error("Widget ayari kaydedilemedi: \(itemId).\(key)", error)
        }
    }

    public func activate(_ item: DockItem) {
        guard item.kind == .app, let path = item.path else { return }
        AppCatalog.activateOrLaunch(appAt: path) { _ in }
    }
}

/// Bildirim gozlemcilerini tutar ve kendi deinit'inde temizler.
/// Swift 6'da MainActor'a bagli bir sinifin deinit'i izole olmadigi icin
/// gozlemcileri dogrudan orada kaldiramiyoruz; bu kutu o isi ustleniyor.
final class ObserverBag: @unchecked Sendable {
    private var tokens: [NSObjectProtocol] = []
    private let lock = NSLock()

    func add(_ token: NSObjectProtocol) {
        lock.lock(); defer { lock.unlock() }
        tokens.append(token)
    }

    deinit {
        let nc = NSWorkspace.shared.notificationCenter
        tokens.forEach { nc.removeObserver($0) }
    }
}
