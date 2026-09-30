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
///
/// Kimlikler, varyant kimlikleri ve gorunen adlar (d-arayuz-dili) Windows v0.6.1 ile AYNI; config.json iki
/// platformda okunabilsin diye (MIMARI.md bolum 3). Ilk varyant varsayilandir,
/// sira da Windows'la ayni. Windows'ta olup bizde henuz cizilmeyen varyantlar
/// (clock/calendar, system/bars, status/icons, weather/hourly, ai-usage/numbers)
/// listeye konmadi; eksikler docs/WIDGET-SEMASI.md'de.
public enum WidgetRegistry {
    public static let all: [WidgetDefinition] = [
        // ---- Clocks
        WidgetDefinition(id: "clock", name: "Clock", category: "Clocks",
                         variants: [WidgetVariant("analog", "Analog"),
                                    WidgetVariant("digital", "Digital")]),
        WidgetDefinition(id: "world-clock", name: "World clock", category: "Clocks",
                         variants: [WidgetVariant("single", "Single city"),
                                    WidgetVariant("multi", "Multiple cities")]),
        WidgetDefinition(id: "stopwatch", name: "Stopwatch", category: "Clocks",
                         variants: [WidgetVariant("default", "Stopwatch")]),
        WidgetDefinition(id: "focus", name: "Focus timer", category: "Clocks",
                         variants: [WidgetVariant("default", "Focus timer")]),
        WidgetDefinition(id: "countdown", name: "Countdown", category: "Clocks",
                         variants: [WidgetVariant("default", "Countdown")]),
        WidgetDefinition(id: "alarm", name: "Alarm", category: "Clocks",
                         variants: [WidgetVariant("default", "Alarm")]),
        WidgetDefinition(id: "time-progress", name: "Time progress", category: "Clocks",
                         variants: [WidgetVariant("bar", "Bar"),
                                    WidgetVariant("ring", "Ring")]),
        // ---- Reminders
        WidgetDefinition(id: "hydration", name: "Hydration", category: "Reminders",
                         variants: [WidgetVariant("timer", "Timer"),
                                    WidgetVariant("progress", "Daily goal")]),
        WidgetDefinition(id: "reminders", name: "Reminders", category: "Reminders",
                         variants: [WidgetVariant("list", "List"),
                                    WidgetVariant("next", "Next"),
                                    WidgetVariant("count", "Count")]),
        // ---- Sticky notes
        WidgetDefinition(id: "notes", name: "Sticky note", category: "Sticky notes",
                         variants: [WidgetVariant("sticky", "Sticky note")]),
        // ---- Media
        WidgetDefinition(id: "media", name: "Now playing", category: "Media",
                         variants: [WidgetVariant("full", "Full"),
                                    WidgetVariant("compact", "Compact"),
                                    WidgetVariant("mini", "Mini")]),
        WidgetDefinition(id: "audio", name: "Audio device", category: "Media",
                         variants: [WidgetVariant("compact", "Compact"),
                                    WidgetVariant("slider", "Slider")]),
        // ---- System
        WidgetDefinition(id: "system", name: "CPU and memory", category: "System",
                         variants: [WidgetVariant("numbers", "Numbers"),
                                    WidgetVariant("rings", "Rings")]),
        WidgetDefinition(id: "network", name: "Network speed", category: "System",
                         variants: [WidgetVariant("numbers", "Numbers only"),
                                    WidgetVariant("chart", "Chart")]),
        WidgetDefinition(id: "status", name: "Status", category: "System",
                         variants: [WidgetVariant("rings", "Rings"),
                                    WidgetVariant("percent", "Percentage ring")]),
        WidgetDefinition(id: "recycle-bin", name: "Recycle bin", category: "System",
                         variants: [WidgetVariant("icon", "Icon only"),
                                    WidgetVariant("details", "Detailed")]),
        WidgetDefinition(id: "battery-devices", name: "Device batteries", category: "System",
                         variants: [WidgetVariant("single", "Single device"),
                                    WidgetVariant("multi", "Multiple devices")]),
        // ---- Weather
        WidgetDefinition(id: "weather", name: "Weather", category: "Weather",
                         variants: [WidgetVariant("current", "Current"),
                                    WidgetVariant("conditions", "Conditions")]),
        // ---- AI
        WidgetDefinition(id: "ai-usage", name: "AI usage", category: "AI",
                         variants: [WidgetVariant("rings", "Rings"),
                                    WidgetVariant("bars", "Bars")]),
        // ---- macOS'a ozgu: Windows kayit defterinde karsiliklari yok.
        // "battery" Windows'ta Durum widget'inin bir halkasi; burada ayri widget.
        WidgetDefinition(id: "battery", name: "Battery", category: "System",
                         variants: [WidgetVariant("percent", "Percentage"),
                                    WidgetVariant("icon", "Icon only")]),
        WidgetDefinition(id: "shortcut", name: "Shortcut", category: "Extras",
                         variants: [WidgetVariant("single", "Shortcut")]),
        WidgetDefinition(id: "airdrop", name: "AirDrop", category: "Extras",
                         variants: [WidgetVariant("full", "Full"),
                                    WidgetVariant("icon", "Icon only")]),
    ]

    public static func find(_ id: String?) -> WidgetDefinition? {
        guard let id else { return nil }
        return all.first { $0.id == id }
    }

    // ---------------- Eski macOS kimliklerinin tasinmasi

    /// Windows'la esitlemeden once macOS'un yazdigi widget kimlikleri.
    static let legacyWidgetIds: [String: String] = [
        "sticky-note": "notes",
        "now-playing": "media",
        "trash": "recycle-bin",
        "device-battery": "battery-devices",
    ]

    /// Eski varyant adlari; anahtar YENI widget kimligi. Yalniz o widget'ta
    /// Windows'ta bulunmayan eski adlar eslenir, boylece Windows'un yazdigi
    /// bir config asla degismez (ornegin world-clock/single Windows'ta da var).
    static let legacyVariants: [String: [String: String]] = [
        "stopwatch": ["single": "default"],
        "focus": ["single": "default"],
        "countdown": ["single": "default"],
        "alarm": ["single": "default"],
        "notes": ["single": "sticky"],
        "hydration": ["goal": "progress"],
        "network": ["graph": "chart"],
        "weather": ["condition": "conditions"],
    ]

    /// Oge listesindeki eski macOS kimliklerini Windows kimliklerine tasir,
    /// klasor icindekiler dahil. Degisiklik yoksa `changed` false doner;
    /// ikinci cagri hicbir seyi degistirmez.
    public static func migrateLegacyIds(_ items: [DockItem]) -> (items: [DockItem], changed: Bool) {
        var changed = false
        let out = items.map { item -> DockItem in
            var it = item
            if it.kind == .widget, let w = it.widget {
                if let yeni = legacyWidgetIds[w] { it.widget = yeni; changed = true }
                if let v = it.variant, let yeni = legacyVariants[it.widget ?? ""]?[v] {
                    it.variant = yeni; changed = true
                }
            }
            if let children = it.children {
                let sonuc = migrateLegacyIds(children)
                if sonuc.changed { it.children = sonuc.items; changed = true }
            }
            return it
        }
        return (out, changed)
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
