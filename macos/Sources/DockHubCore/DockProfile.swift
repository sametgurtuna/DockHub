import Foundation

/// Kayitli dock duzeni. Windows karsiligi: Core/ProfileService.cs -> DockProfile.
/// `items` ve `appearance` C#'ta JsonArray/JsonObject: serbest JSON olarak
/// tasinir, etkin profil icin nil'dir (canli config zaten odur).
public struct DockProfile: Codable, Sendable, Equatable, Identifiable {
    public var id: String = DockItem.newId()
    public var name: String = ""
    /// nil de `null` yazilir (C#'ta nitelik yok).
    public var items: JSONValue?
    public var appearance: JSONValue?
    /// Bu kadar ekran bagliyken otomatik gecilir (nil: hic).
    public var autoDisplayCount: Int?
    /// Bu uygulamanin penceresi acikken gecilir. Windows'ta .exe adi ("steam"),
    /// macOS'ta uygulama adi ya da paket kimligi; ProfileRules.normalizeApp ikisini de sadelestirir.
    public var autoApp: String?
    /// "HH:mm"; aralik gece yarisini gecebilir.
    public var autoTimeFrom: String?
    public var autoTimeTo: String?
    /// Saat kurali yalniz Pazartesi-Cuma.
    public var autoWeekdaysOnly = false
    public var extras: [String: JSONValue] = [:]

    public init(id: String = DockItem.newId(), name: String = "") {
        self.id = id
        self.name = name
    }

    public init(from decoder: Decoder) throws {
        var r = try JSONObjectReader(decoder)
        id = r("id", DockItem.newId())
        name = r("name", "")
        items = r.optional("items")
        appearance = r.optional("appearance")
        autoDisplayCount = r.optional("autoDisplayCount")
        autoApp = r.optional("autoApp")
        autoTimeFrom = r.optional("autoTimeFrom")
        autoTimeTo = r.optional("autoTimeTo")
        autoWeekdaysOnly = r("autoWeekdaysOnly", false)
        extras = r.extras()
    }

    public func encode(to encoder: Encoder) throws {
        var w = JSONObjectWriter(encoder)
        try w("id", id)
        try w("name", name)
        try w("items", items)
        try w("appearance", appearance)
        try w("autoDisplayCount", autoDisplayCount)
        try w.unlessNil("autoApp", autoApp)
        try w.unlessNil("autoTimeFrom", autoTimeFrom)
        try w.unlessNil("autoTimeTo", autoTimeTo)
        try w.unlessFalse("autoWeekdaysOnly", autoWeekdaysOnly)
        try w.extras(extras)
    }
}

/// Kullanicinin kendi kurulumundan kaydettigi duzen onayari.
/// Windows karsiligi: Core/AppConfig.cs -> CustomLayoutPreset.
public struct CustomLayoutPreset: Codable, Sendable, Equatable, Identifiable {
    public var id: String = DockItem.newId()
    public var name: String = ""
    public var appearance: JSONValue?
    public var widgets: [CustomPresetWidget] = []
    public var extras: [String: JSONValue] = [:]

    public init(id: String = DockItem.newId(), name: String = "") {
        self.id = id
        self.name = name
    }

    public init(from decoder: Decoder) throws {
        var r = try JSONObjectReader(decoder)
        id = r("id", DockItem.newId())
        name = r("name", "")
        appearance = r.optional("appearance")
        widgets = r("widgets", [])
        extras = r.extras()
    }

    public func encode(to encoder: Encoder) throws {
        var w = JSONObjectWriter(encoder)
        try w("id", id)
        try w("name", name)
        try w("appearance", appearance)
        try w("widgets", widgets)
        try w.extras(extras)
    }
}

public struct CustomPresetWidget: Codable, Sendable, Equatable {
    public var widget: String = ""
    public var variant: String?

    public init(widget: String, variant: String? = nil) {
        self.widget = widget
        self.variant = variant
    }

    public init(from decoder: Decoder) throws {
        var r = try JSONObjectReader(decoder)
        widget = r("widget", "")
        variant = r.optional("variant")
    }

    public func encode(to encoder: Encoder) throws {
        var w = JSONObjectWriter(encoder)
        try w("widget", widget)
        try w("variant", variant)
    }
}
