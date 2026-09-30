import AppKit
import SwiftUI
import DockHubCore
import DockHubPlatform

/// Ayarlar penceresinin config'e tek yazma yolu.
/// Windows karsiligi: SettingsWindow'un AppConfig'e bagli DataContext'i,
/// SettingsWindow.Items.cs oge islemleri ve ConfigService.Save.
///
/// Her degisiklik diske yazilir ve ConfigEvents.didChange gonderilir;
/// AppDelegate yerlesim imzasi degistiyse dock'u yeniden kurar. Yazma her
/// zaman service'in guncel config'i uzerinden yapilir, boylece widget'in o
/// arada yazdigi kendi ayari (su sayaci gibi) ezilmez.
@MainActor
public final class SettingsStore: ObservableObject {
    @Published public private(set) var config: AppConfig
    @Published public private(set) var lastError: String?
    public let service: ConfigService

    public init(service: ConfigService) {
        self.service = service
        self.config = service.config
    }

    /// Pencere acilirken diskteki son durumu alir (widget'lar arada yazmis olabilir).
    public func reload() { config = service.config }

    public func update(_ change: (inout AppConfig) -> Void) {
        do {
            try service.update(change)
            config = service.config
            lastError = nil
            NotificationCenter.default.post(name: ConfigEvents.didChange, object: nil)
        } catch {
            lastError = error.localizedDescription
            Log.error("Ayar kaydedilemedi", error)
        }
    }

    public func binding<T>(_ keyPath: WritableKeyPath<AppConfig, T>) -> Binding<T> {
        Binding(get: { self.config[keyPath: keyPath] },
                set: { yeni in self.update { $0[keyPath: keyPath] = yeni } })
    }

    // ---------------- Oge islemleri (ust duzey ogeler)

    /// Uygulamalari sona ekler; dock'ta zaten sabit olan yol tekrar eklenmez
    /// (Windows'ta da ayni uygulama iki kez sabitlenmez). Eklenen sayiyi doner.
    @discardableResult
    public func addApps(_ paths: [String]) -> Int {
        var eklenen = 0
        update { c in
            var mevcut = Set(c.items.compactMap(\.path))
            for p in paths where !mevcut.contains(p) {
                c.items.append(.app(p))
                mevcut.insert(p)
                eklenen += 1
            }
        }
        return eklenen
    }

    public func addSeparator() {
        update { $0.items.append(.separator()) }
    }

    /// Widget'i sona ekler ve yeni ogenin kimligini doner.
    @discardableResult
    public func addWidget(_ widget: String, variant: String?) -> String {
        let item = DockItem.widget(widget, variant: variant)
        update { $0.items.append(item) }
        return item.id
    }

    public func moveItems(from source: IndexSet, to destination: Int) {
        update { $0.items.move(fromOffsets: source, toOffset: destination) }
    }

    public func removeItem(_ id: String) {
        update { $0.items.removeAll { $0.id == id } }
    }

    /// Bos ad nil yazilir: Windows'ta oldugu gibi uygulamanin kendi adina duser.
    public func setName(_ id: String, _ name: String) {
        let ad = name.trimmingCharacters(in: .whitespacesAndNewlines)
        edit(id) { $0.name = ad.isEmpty ? nil : ad }
    }

    public func setArguments(_ id: String, _ arguments: String) {
        let a = arguments.trimmingCharacters(in: .whitespacesAndNewlines)
        edit(id) { $0.arguments = a.isEmpty ? nil : a }
    }

    public func setVariant(_ id: String, _ variant: String) {
        edit(id) { $0.variant = variant }
    }

    public func setGroupName(_ id: String, _ name: String) {
        let ad = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !ad.isEmpty else { return }
        edit(id) { $0.groupName = ad }
    }

    /// macOS Dock'undaki sabit uygulamalari ekler (Windows: "Import taskbar pins").
    @discardableResult
    public func importDockApps() -> Int {
        addApps(SystemDockApps.pinnedAppPaths())
    }

    private func edit(_ id: String, _ change: (inout DockItem) -> Void) {
        update { c in
            guard let i = c.items.firstIndex(where: { $0.id == id }) else { return }
            change(&c.items[i])
        }
    }
}
