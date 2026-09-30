import Foundation

/// Geri alinabilir tek adim: degisiklikten onceki ogeler (ve istenirse gorunum).
/// Windows karsiligi: Core/ConfigHistory.cs -> HistoryEntry.
public struct HistoryEntry: Sendable {
    public let description: String
    public let items: [DockItem]
    /// Gorunum ayarlari, Windows'taki gibi PascalCase adlarla ("Edge", "Theme"...).
    public let appearance: [String: JSONValue]?
    /// Silme gibi adimlar "Geri al" bildirimiyle duyurulur.
    public var destructive: Bool
}

/// Dock degisikliklerinin geri alma yigini. Windows karsiligi: Core/ConfigHistory.cs.
/// AppConfig bir deger tipi oldugu icin anlik goruntu kopyanin kendisi; geri
/// alinca widget'larin kendi ayarlari (su sayaci, not) canli halinden alinir,
/// Windows'taki `Reuse` gibi.
public final class ConfigHistory: @unchecked Sendable {
    public static let capacity = 20

    /// Onayar, profil ve tema degisikliklerinde yakalanan gorunum ayarlari (C#: AppearanceProperties).
    public static let appearanceProperties = [
        "Edge", "Theme", "Backdrop", "TintOpacity", "Size", "Layout", "WidthMode", "Alignment",
        "EdgeMargin", "AutoHide", "HoverEffect", "ShowClock", "ShowTray", "ShowRunningApps",
        "ShowSearchButton", "ShowTaskViewButton", "ShowStartButton", "WidgetStyle",
    ]

    public private(set) var entries: [HistoryEntry] = []
    private var session: HistorySession?

    /// (adim, geri alindi mi). Ayarlar penceresi ve dock "Geri al"i tazelemek icin dinler.
    public var onChange: ((HistoryEntry, Bool) -> Void)?

    public init() {}

    public var latest: HistoryEntry? { entries.last }

    /// Acik bir adim varken (duzenleme modu) geri alma bekler.
    public var canUndo: Bool { session == nil && !entries.isEmpty }

    public var isSessionOpen: Bool { session != nil }

    /// Degisiklikten ONCE cagrilir. Oturum aciksa yeni adim eklenmez (oturumun adimi gecerli).
    @discardableResult
    public func push(_ config: AppConfig, _ description: String,
                     destructive: Bool = false, includeAppearance: Bool = false) -> HistoryEntry? {
        guard session == nil else { return nil }
        let entry = HistoryEntry(description: description, items: config.items,
                                 appearance: includeAppearance ? Self.captureAppearance(config) : nil,
                                 destructive: destructive)
        entries.append(entry)
        if entries.count > Self.capacity { entries.removeFirst(entries.count - Self.capacity) }
        onChange?(entry, false)
        return entry
    }

    /// Son adimi geri yukler; geri alinacak bir sey yoksa nil.
    @discardableResult
    public func undo(_ config: inout AppConfig) -> HistoryEntry? {
        guard session == nil, let entry = entries.popLast() else { return nil }
        var live: [String: DockItem] = [:]
        Self.flatten(config.items).forEach { if live[$0.id] == nil { live[$0.id] = $0 } }
        config.items = entry.items.map { Self.reuse($0, live) }
        if let appearance = entry.appearance { Self.restoreAppearance(&config, appearance) }
        onChange?(entry, true)
        return entry
    }

    // ---------------- Duzenleme oturumu (C#: BeginSession / HistorySession.End)

    /// Bitene kadar suren tek adim: arada yapilan her degisiklik birlikte geri alinir.
    public func beginSession(_ config: AppConfig, _ description: String) {
        guard session == nil else { return }
        let entry = HistoryEntry(description: description, items: config.items, appearance: nil, destructive: false)
        entries.append(entry)
        if entries.count > Self.capacity { entries.removeFirst(entries.count - Self.capacity) }
        session = HistorySession(startLayout: Self.layout(of: config.items))
    }

    /// Oturumu kapatir. Dock degistiyse adim kalir (`announce` ile duyurulur) ve true doner;
    /// degismediyse adim silinir, acip kapatmak iz birakmaz.
    @discardableResult
    public func endSession(_ config: AppConfig, announce: Bool = true) -> Bool {
        guard let open = session, var entry = entries.popLast() else { session = nil; return false }
        session = nil
        guard Self.layout(of: config.items) != open.startLayout else { return false }
        entry.destructive = announce
        entries.append(entry)
        onChange?(entry, false)
        return true
    }

    // ---------------- Yardimcilar

    /// Geri almanin geri verdigi kisim: widget'larin kendi ayarlari haric ogeler.
    public static func layout(of items: [DockItem]) -> [DockItem] {
        items.map { item in
            var copy = item
            copy.settings = nil
            copy.collapseWhenIdle = false
            copy.extras = [:]
            copy.children = item.children.map(layout(of:))
            return copy
        }
    }

    /// C#: Reuse. Hala var olan ogenin canli hali (ayarlari) korunur, yerlesim alanlari goruntuden gelir.
    static func reuse(_ snapshot: DockItem, _ live: [String: DockItem]) -> DockItem {
        guard var item = live[snapshot.id], item.kind == snapshot.kind else {
            var copy = snapshot
            copy.children = snapshot.children?.map { reuse($0, live) }
            return copy
        }
        item.path = snapshot.path
        item.arguments = snapshot.arguments
        item.name = snapshot.name
        item.widget = snapshot.widget
        item.variant = snapshot.variant
        item.pinnedEnd = snapshot.pinnedEnd
        item.display = snapshot.display
        item.surface = snapshot.surface
        item.groupName = snapshot.groupName
        item.groupAccent = snapshot.groupAccent
        item.children = snapshot.children?.map { reuse($0, live) }
        return item
    }

    static func flatten(_ items: [DockItem]) -> [DockItem] {
        items.flatMap { [$0] + flatten($0.children ?? []) }
    }

    /// C#: CaptureAppearance. Anahtarlar C# ozellik adlari (PascalCase); dosya Windows'la ortak.
    public static func captureAppearance(_ config: AppConfig) -> [String: JSONValue] {
        guard let object = jsonObject(config) else { return [:] }
        var out: [String: JSONValue] = [:]
        for name in appearanceProperties {
            if let value = object[camel(name)] { out[name] = value }
        }
        return out
    }

    /// C#: RestoreAppearance. Goruntudeki her taninan ayari geri koyar (TopBar gibi listede
    /// olmayanlar dahil). `missingAsDefault`: goruntude olmayan gorunum ayari varsayilana doner,
    /// boylece bir profilin secimi digerine tasmaz.
    public static func restoreAppearance(_ config: inout AppConfig, _ appearance: [String: JSONValue],
                                         missingAsDefault: Bool = false) {
        guard var object = jsonObject(config) else { return }
        if missingAsDefault, let defaults = jsonObject(AppConfig()) {
            for name in appearanceProperties where appearance[name] == nil {
                object[camel(name)] = defaults[camel(name)]
            }
        }
        for (name, value) in appearance where value != .null {
            let key = camel(name)
            if object[key] != nil { object[key] = value }
        }
        guard let data = try? JSONStore.encoder.encode(JSONValue.object(object)),
              let restored = try? JSONStore.decoder.decode(AppConfig.self, from: data) else { return }
        config = restored
    }

    static func jsonObject(_ config: AppConfig) -> [String: JSONValue]? {
        guard let data = try? JSONStore.encoder.encode(config),
              case .object(let object)? = try? JSONStore.decoder.decode(JSONValue.self, from: data) else { return nil }
        return object
    }

    /// "TintOpacity" -> "tintOpacity" (C# JsonNamingPolicy.CamelCase, tek kelimelik adlar icin yeterli).
    static func camel(_ name: String) -> String {
        guard let first = name.first else { return name }
        return first.lowercased() + name.dropFirst()
    }
}

/// Acik duzenleme oturumu (C#: HistorySession).
struct HistorySession {
    let startLayout: [DockItem]
}
