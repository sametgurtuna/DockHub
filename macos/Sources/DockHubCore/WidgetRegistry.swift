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
/// Kimlikler ve varyant kimlikleri Windows v0.6.1 ile AYNI; config.json iki
/// platformda okunabilsin diye (MIMARI.md bolum 3). Ilk varyant varsayilandir,
/// sira da Windows'la ayni. Windows'ta olup bizde henuz cizilmeyen varyantlar
/// (clock/calendar, system/bars, status/icons, weather/hourly, ai-usage/numbers)
/// listeye konmadi; eksikler docs/WIDGET-SEMASI.md'de.
public enum WidgetRegistry {
    public static let all: [WidgetDefinition] = [
        // ---- Saatler (Windows: Clocks)
        WidgetDefinition(id: "clock", name: "Saat", category: "Saatler",
                         variants: [WidgetVariant("analog", "Analog"),
                                    WidgetVariant("digital", "Dijital")]),
        WidgetDefinition(id: "world-clock", name: "Dünya saati", category: "Saatler",
                         variants: [WidgetVariant("single", "Tek şehir"),
                                    WidgetVariant("multi", "Çoklu şehir")]),
        WidgetDefinition(id: "stopwatch", name: "Kronometre", category: "Saatler",
                         variants: [WidgetVariant("default", "Kronometre")]),
        WidgetDefinition(id: "focus", name: "Odak zamanlayıcı", category: "Saatler",
                         variants: [WidgetVariant("default", "Odak zamanlayıcı")]),
        WidgetDefinition(id: "countdown", name: "Geri sayım", category: "Saatler",
                         variants: [WidgetVariant("default", "Geri sayım")]),
        WidgetDefinition(id: "alarm", name: "Alarm", category: "Saatler",
                         variants: [WidgetVariant("default", "Alarm")]),
        WidgetDefinition(id: "time-progress", name: "Zaman ilerlemesi", category: "Saatler",
                         variants: [WidgetVariant("bar", "Çubuk"),
                                    WidgetVariant("ring", "Halka")]),
        // ---- Hatirlaticilar (Windows: Reminders)
        WidgetDefinition(id: "hydration", name: "Su takibi", category: "Hatırlatıcılar",
                         variants: [WidgetVariant("timer", "Zamanlayıcı"),
                                    WidgetVariant("progress", "Günlük hedef")]),
        WidgetDefinition(id: "reminders", name: "Hatırlatıcılar", category: "Hatırlatıcılar",
                         variants: [WidgetVariant("list", "Liste"),
                                    WidgetVariant("next", "Sıradaki"),
                                    WidgetVariant("count", "Sayı")]),
        // ---- Yapiskan notlar (Windows: Sticky notes)
        WidgetDefinition(id: "notes", name: "Yapışkan not", category: "Yapışkan notlar",
                         variants: [WidgetVariant("sticky", "Yapışkan not")]),
        // ---- Medya (Windows: Media)
        WidgetDefinition(id: "media", name: "Çalan medya", category: "Medya",
                         variants: [WidgetVariant("full", "Tam"),
                                    WidgetVariant("compact", "Kompakt"),
                                    WidgetVariant("mini", "Mini")]),
        WidgetDefinition(id: "audio", name: "Ses aygıtı", category: "Medya",
                         variants: [WidgetVariant("compact", "Kompakt"),
                                    WidgetVariant("slider", "Çubuklu")]),
        // ---- Sistem (Windows: System)
        WidgetDefinition(id: "system", name: "İşlemci ve bellek", category: "Sistem",
                         variants: [WidgetVariant("numbers", "Sayılar"),
                                    WidgetVariant("rings", "Halkalar")]),
        WidgetDefinition(id: "network", name: "Ağ hızı", category: "Sistem",
                         variants: [WidgetVariant("numbers", "Yalnızca sayılar"),
                                    WidgetVariant("chart", "Grafikli")]),
        WidgetDefinition(id: "status", name: "Durum", category: "Sistem",
                         variants: [WidgetVariant("rings", "Halkalar"),
                                    WidgetVariant("percent", "Yüzde halkası")]),
        WidgetDefinition(id: "recycle-bin", name: "Çöp kutusu", category: "Sistem",
                         variants: [WidgetVariant("icon", "Yalnızca ikon"),
                                    WidgetVariant("details", "Detaylı")]),
        WidgetDefinition(id: "battery-devices", name: "Aygıt pilleri", category: "Sistem",
                         variants: [WidgetVariant("single", "Tek aygıt"),
                                    WidgetVariant("multi", "Çoklu aygıt")]),
        // ---- Hava durumu (Windows: Weather)
        WidgetDefinition(id: "weather", name: "Hava durumu", category: "Hava durumu",
                         variants: [WidgetVariant("current", "Anlık"),
                                    WidgetVariant("conditions", "Durum")]),
        // ---- Yapay zeka (Windows: AI)
        WidgetDefinition(id: "ai-usage", name: "AI kullanımı", category: "Yapay zeka",
                         variants: [WidgetVariant("rings", "Halkalar"),
                                    WidgetVariant("bars", "Çubuklar")]),
        // ---- macOS'a ozgu: Windows kayit defterinde karsiliklari yok.
        // "battery" Windows'ta Durum widget'inin bir halkasi; burada ayri widget.
        WidgetDefinition(id: "battery", name: "Pil", category: "Sistem",
                         variants: [WidgetVariant("percent", "Yüzde"),
                                    WidgetVariant("icon", "Yalnızca ikon")]),
        WidgetDefinition(id: "shortcut", name: "Kısayol", category: "Ekler",
                         variants: [WidgetVariant("single", "Tek")]),
        WidgetDefinition(id: "airdrop", name: "AirDrop", category: "Ekler",
                         variants: [WidgetVariant("full", "Tam"),
                                    WidgetVariant("icon", "Yalnızca ikon")]),
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
