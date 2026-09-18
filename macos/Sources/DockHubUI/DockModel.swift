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
    private let observers = ObserverBag()

    public init(config: AppConfig, items: [DockItem]) {
        self.config = config
        self.items = items
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
