import Foundation

public struct WidgetVariant: Sendable, Identifiable {
    public let id: String
    public let name: String
    public init(_ id: String, _ name: String) { self.id = id; self.name = name }
}

public struct WidgetDefinition: Sendable, Identifiable {
    public let id: String
    public let name: String
    public let category: String
    public let variants: [WidgetVariant]

    public var defaultVariant: String { variants.first?.id ?? "" }
}

/// Widget kayit defteri. Windows karsiligi: Widgets/WidgetRegistry.cs
/// Gorunum ureticisi burada DEGIL DockHubUI tarafinda; bu katman AppKit bilmez.
public enum WidgetRegistry {
    public static let all: [WidgetDefinition] = [
        WidgetDefinition(id: "clock", name: "Saat", category: "Saatler",
                         variants: [WidgetVariant("digital", "Dijital"),
                                    WidgetVariant("analog", "Analog")]),
        WidgetDefinition(id: "system", name: "Sistem", category: "Sistem",
                         variants: [WidgetVariant("numbers", "Sayılar"),
                                    WidgetVariant("rings", "Halkalar")]),
        WidgetDefinition(id: "battery", name: "Pil", category: "Sistem",
                         variants: [WidgetVariant("percent", "Yüzde"),
                                    WidgetVariant("icon", "Yalnızca ikon")]),
        WidgetDefinition(id: "weather", name: "Hava durumu", category: "Hava",
                         variants: [WidgetVariant("current", "Anlık"),
                                    WidgetVariant("condition", "Durum")]),
        WidgetDefinition(id: "world-clock", name: "Dünya saati", category: "Saatler",
                         variants: [WidgetVariant("single", "Tek şehir"),
                                    WidgetVariant("multi", "Çoklu şehir")]),
        WidgetDefinition(id: "stopwatch", name: "Kronometre", category: "Saatler",
                         variants: [WidgetVariant("single", "Tek")]),
        WidgetDefinition(id: "focus", name: "Odak zamanlayıcı", category: "Saatler",
                         variants: [WidgetVariant("single", "Tek")]),
        WidgetDefinition(id: "countdown", name: "Geri sayım", category: "Saatler",
                         variants: [WidgetVariant("single", "Tek")]),
        WidgetDefinition(id: "alarm", name: "Alarm", category: "Saatler",
                         variants: [WidgetVariant("single", "Tek")]),
        WidgetDefinition(id: "time-progress", name: "Zaman ilerlemesi", category: "Saatler",
                         variants: [WidgetVariant("bar", "Çubuk"),
                                    WidgetVariant("ring", "Halka")]),
        WidgetDefinition(id: "hydration", name: "Su takibi", category: "Hatırlatıcılar",
                         variants: [WidgetVariant("timer", "Zamanlayıcı"),
                                    WidgetVariant("goal", "Günlük hedef")]),
        WidgetDefinition(id: "reminders", name: "Hatırlatıcılar", category: "Hatırlatıcılar",
                         variants: [WidgetVariant("next", "Sonraki"),
                                    WidgetVariant("list", "Liste"),
                                    WidgetVariant("count", "Sayı")]),
        WidgetDefinition(id: "sticky-note", name: "Yapışkan not", category: "Notlar",
                         variants: [WidgetVariant("single", "Tek")]),
        WidgetDefinition(id: "network", name: "Ağ", category: "Sistem",
                         variants: [WidgetVariant("numbers", "Yalnızca sayı"),
                                    WidgetVariant("graph", "Grafikli")]),
        WidgetDefinition(id: "status", name: "Durum", category: "Sistem",
                         variants: [WidgetVariant("rings", "Halkalar"),
                                    WidgetVariant("percent", "Yüzde")]),
        WidgetDefinition(id: "audio", name: "Ses aygıtı", category: "Medya",
                         variants: [WidgetVariant("compact", "Kompakt"),
                                    WidgetVariant("slider", "Çubuklu")]),
        WidgetDefinition(id: "device-battery", name: "Aygıt pilleri", category: "Sistem",
                         variants: [WidgetVariant("single", "Tek aygıt"),
                                    WidgetVariant("multi", "Çoklu aygıt")]),
    ]

    public static func find(_ id: String?) -> WidgetDefinition? {
        guard let id else { return nil }
        return all.first { $0.id == id }
    }
}

/// Widget ornegi basina ayarlar DockItem.settings icinde durur; her kopya
/// kendi ayarini tasir. Windows'ta da ayni yer kullaniliyor (JsonObject).
public extension DockItem {
    func setting(_ key: String) -> JSONValue? {
        guard case .object(let dict)? = settings else { return nil }
        return dict[key]
    }
    func boolSetting(_ key: String, default def: Bool) -> Bool {
        if case .bool(let v)? = setting(key) { return v }
        return def
    }
    func numberSetting(_ key: String, default def: Double) -> Double {
        if case .number(let v)? = setting(key) { return v }
        return def
    }
    /// Kayitli varyant; yoksa defterdeki ilk varyant.
    var effectiveVariant: String {
        variant ?? WidgetRegistry.find(widget)?.defaultVariant ?? ""
    }
}
