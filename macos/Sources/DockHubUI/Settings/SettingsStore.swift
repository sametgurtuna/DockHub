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

    /// Geri alinabilecek son adimin aciklamasi; yoksa nil.
    @Published public private(set) var undoDescription: String?
    private var changeObserver: NSObjectProtocol?

    public init(service: ConfigService) {
        self.service = service
        self.config = service.config
        // Dock menusu, kisayol ya da profil kurali da config'i degistirebilir.
        changeObserver = NotificationCenter.default.addObserver(
            forName: ConfigEvents.didChange, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.reload() }
        }
        refreshUndo()
    }

    /// Pencere acilirken diskteki son durumu alir (widget'lar arada yazmis olabilir).
    public func reload() {
        config = service.config
        refreshUndo()
    }

    private func refreshUndo() {
        undoDescription = service.history.canUndo ? service.history.latest?.description : nil
    }

    /// Degisiklikten once geri alma adimi (Windows: History.Push).
    private func record(_ description: String, destructive: Bool = false) {
        service.history.push(service.config, description, destructive: destructive)
    }

    /// Son adimi geri alir (Windows: ConfigService.Undo).
    public func undo() {
        do {
            guard try service.undo() != nil else { return }
            config = service.config
            lastError = nil
            NotificationCenter.default.post(name: ConfigEvents.didChange, object: nil)
        } catch {
            lastError = error.localizedDescription
            Log.error("Geri alinamadi", error)
        }
        refreshUndo()
    }

    public func update(_ change: (inout AppConfig) -> Void) {
        do {
            try service.update(change)
            config = service.config
            lastError = nil
            refreshUndo()
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
    public func addApps(_ paths: [String], description: String? = nil) -> Int {
        let mevcutYollar = Set(service.config.items.compactMap(\.path))
        let yeni = paths.filter { !mevcutYollar.contains($0) }
        guard !yeni.isEmpty else { return 0 }
        record(description ?? (yeni.count == 1
            ? L.t("Added {0}", AppCatalog.displayName(forAppAt: yeni[0]))
            : L.t("Added {0}", L.t("{0} apps", yeni.count))))
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
        record(L.t("Added {0}", L.t("Separator")))
        update { $0.items.append(.separator()) }
    }

    /// Widget'i sona ekler ve yeni ogenin kimligini doner.
    @discardableResult
    public func addWidget(_ widget: String, variant: String?) -> String {
        let item = DockItem.widget(widget, variant: variant)
        record(L.t("Added {0}", ItemText.title(item)))
        update { $0.items.append(item) }
        return item.id
    }

    public func moveItems(from source: IndexSet, to destination: Int) {
        if let first = source.first, first < service.config.items.count {
            record(L.t("Moved {0}", ItemText.title(service.config.items[first])))
        }
        update { $0.items.move(fromOffsets: source, toOffset: destination) }
    }

    public func removeItem(_ id: String) {
        guard let item = service.config.items.first(where: { $0.id == id }) else { return }
        record(L.t("Removed {0}", ItemText.title(item)), destructive: true)
        update { $0.items.removeAll { $0.id == id } }
    }

    // ---------------- Profiller (Windows: ProfileService)

    /// Elle profil secimi; kurallar bunu duyar (AppDelegate). Parametre: profil kimligi.
    public static let profileSwitched = Notification.Name("DockHub.profileSwitched")

    public func saveProfile(_ name: String) {
        update { Profiles.saveCurrentAs(name, &$0) }
    }

    public func switchProfile(_ id: String) {
        update { Profiles.switchTo(id, &$0) }
        NotificationCenter.default.post(name: Self.profileSwitched, object: id)
    }

    public func renameProfile(_ id: String, _ name: String) { update { Profiles.rename(id, name, &$0) } }
    public func deleteProfile(_ id: String) { update { Profiles.delete(id, &$0) } }
    public func setProfileDisplayCount(_ id: String, _ count: Int?) { update { Profiles.setAutoDisplayCount(id, count, &$0) } }
    public func setProfileApp(_ id: String, _ app: String) { update { Profiles.setAutoApp(id, app, &$0) } }
    public func setProfileTime(_ id: String, from: String, to: String, weekdaysOnly: Bool) {
        update { Profiles.setAutoTime(id, from: from, to: to, weekdaysOnly: weekdaysOnly, &$0) }
    }

    // ---------------- Kisayollar

    public func setHotkey(_ id: String, _ gesture: HotkeyGesture?) { update { HotkeyActions.set(id, gesture, in: &$0) } }
    public func resetHotkey(_ id: String) { update { HotkeyActions.reset(id, in: &$0) } }

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
        addApps(SystemDockApps.pinnedAppPaths(), description: L.t("Imported apps from the macOS Dock"))
    }

    private func edit(_ id: String, _ change: (inout DockItem) -> Void) {
        update { c in
            guard let i = c.items.firstIndex(where: { $0.id == id }) else { return }
            change(&c.items[i])
        }
    }
}
