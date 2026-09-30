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

    // ---------------- Duzenleme modu (Windows: Dock/DockEditMode.cs)

    /// Ogeler x ile kaldirilir, surukleyerek siralanir, widget'in duzeni degistirilir.
    /// Mod boyunca tek geri alma adimi acik kalir; degisiklik yoksa iz birakmaz.
    @Published public private(set) var isEditing = false

    /// Dock'un yerlesimi degisti (AppDelegate dock'u yeniden kurar). Duzenleme
    /// boyunca beklenir, cikista bir kez gelir.
    public var onLayoutChanged: (() -> Void)?
    /// "+" kutucugu: galeriyi acar.
    public var onOpenGallery: (() -> Void)?

    public func beginEditing() {
        guard let service, !isEditing else { return }
        service.history.beginSession(service.config, L.t("Edited the dock"))
        isEditing = true
    }

    /// `notify` false: dock zaten yeniden kuruluyor (ayarlar penceresinden gelen degisiklik).
    public func endEditing(notify: Bool = true) {
        guard let service, isEditing else { return }
        isEditing = false
        let changed = service.history.endSession(service.config)
        if changed && notify { onLayoutChanged?() }
    }

    public func removeItem(_ id: String) {
        guard let service, let item = service.config.items.first(where: { $0.id == id }) else { return }
        service.history.push(service.config, L.t("Removed {0}", ItemText.title(item)), destructive: true)
        write { $0.items.removeAll { $0.id == id } }
    }

    public func moveItem(_ id: String, before target: String?) {
        write { $0.items = DockEditing.move($0.items, id, before: target) }
    }

    /// Galeriden birakilan widget; duzenleme disinda da calisir (Windows: galeriden dock'a surukleme).
    public func insertWidget(_ widget: String, variant: String?, before target: String?) {
        guard let service, let definition = WidgetRegistry.find(widget) else { return }
        let item = DockItem.widget(widget, variant: variant)
        service.history.push(service.config, L.t("Added {0}", definition.displayName))
        write { $0.items = DockEditing.insert($0.items, item, before: target) }
    }

    /// Widget'in bir sonraki duzenine gecer (Windows'taki boyut tutamaginin Mac karsiligi).
    public func cycleVariant(_ id: String) {
        guard let item = items.first(where: { $0.id == id }), let definition = WidgetRegistry.find(item.widget),
              let next = DockEditing.nextVariant(of: definition, current: item.effectiveVariant) else { return }
        write { c in
            guard let i = c.items.firstIndex(where: { $0.id == id }) else { return }
            c.items[i].variant = next
        }
    }

    /// Dock'a birakilan metin: galeriden widget ya da (duzenlemede) dock'taki bir oge.
    @discardableResult
    public func handleDrop(_ strings: [String], before target: String?) -> Bool {
        for text in strings {
            if let widget = DockDrag.decodeWidget(text) {
                insertWidget(widget.id, variant: widget.variant, before: target)
                return true
            }
            if isEditing, let id = DockDrag.decodeItem(text) {
                moveItem(id, before: target)
                return true
            }
        }
        return false
    }

    private func write(_ change: (inout AppConfig) -> Void) {
        guard let service else { return }
        do {
            try service.update(change)
            items = service.config.items
            if !isEditing { onLayoutChanged?() }
        } catch {
            Log.error("Dock kaydedilemedi", error)
        }
    }

    public func activate(_ item: DockItem) {
        guard !isEditing else { return }
        guard item.kind == .app, let path = item.path else { return }
        AppCatalog.activateOrLaunch(appAt: path,
                                    arguments: LaunchArguments.split(item.arguments ?? "")) { _ in }
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
