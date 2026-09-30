import Foundation

/// Dock'taki tek bir oge. Windows karsiligi: Core/AppConfig.cs -> DockItem.
/// Path/arguments/name/widget/variant/settings/display/surface ve grup alanlari
/// C# tarafinda [JsonIgnore(WhenWritingNull)] tasiyor, yani nil ise YAZILMAZ.
/// `pinnedEnd` ve `collapseWhenIdle` [JsonIgnore(WhenWritingDefault)]: false ise YAZILMAZ.
/// Bilinmeyen anahtarlar `extras`'ta saklanip aynen geri yazilir.
public struct DockItem: Codable, Sendable, Identifiable, Equatable {
    public var id: String
    public var kind: DockItemKind
    public var path: String?
    public var arguments: String?
    public var name: String?
    public var widget: String?
    public var variant: String?
    /// C# tarafinda JsonObject; sema kurali gerektirmeyen serbest ayar torbasi.
    public var settings: JSONValue?
    /// Yalniz widget'lar: kayan listeden cikip dock'un sabit sag ucuna yerlesir.
    public var pinnedEnd: Bool
    /// Yalniz widget'lar: "Tum ekranlarda goster" acikken widget'i gosteren
    /// ekran (Windows'ta aygit adi, macOS'ta NSScreen.localizedName). nil: ana dock.
    public var display: String?
    /// Yalniz widget'lar: "bar" ise ust barda (bar kapaliyken dock'ta). nil: dock.
    public var surface: String?
    /// Yalniz widget'lar: gosterecek bir sey yokken kucuk kutucuk olarak durur.
    public var collapseWhenIdle: Bool
    /// Yalniz gruplar (klasor).
    public var groupName: String?
    /// WPF firca anahtari ("AccentBlueBrush" vb.). Dosya Windows'la ortak
    /// kalsin diye ayni ad yazilir; renge cevirme arayuz katmaninda.
    public var groupAccent: String?
    public var children: [DockItem]?
    public var extras: [String: JSONValue] = [:]

    /// C#: DockItem.BarSurface
    public static let barSurface = "bar"

    public init(id: String = DockItem.newId(), kind: DockItemKind,
                path: String? = nil, arguments: String? = nil, name: String? = nil,
                widget: String? = nil, variant: String? = nil, settings: JSONValue? = nil,
                pinnedEnd: Bool = false, groupName: String? = nil, groupAccent: String? = nil,
                children: [DockItem]? = nil, display: String? = nil, surface: String? = nil,
                collapseWhenIdle: Bool = false) {
        self.id = id; self.kind = kind; self.path = path; self.arguments = arguments
        self.name = name; self.widget = widget; self.variant = variant; self.settings = settings
        self.pinnedEnd = pinnedEnd; self.groupName = groupName; self.groupAccent = groupAccent
        self.children = children; self.display = display; self.surface = surface
        self.collapseWhenIdle = collapseWhenIdle
    }

    /// Eksik alan varsayilanla okunur (System.Text.Json davranisi):
    /// id yoksa yeni kimlik, kind yoksa App (C# enum'unun sifir degeri).
    public init(from decoder: Decoder) throws {
        var r = try JSONObjectReader(decoder)
        id = r("id", DockItem.newId())
        kind = r("kind", DockItemKind.app)
        path = r.optional("path")
        arguments = r.optional("arguments")
        name = r.optional("name")
        widget = r.optional("widget")
        variant = r.optional("variant")
        settings = r.optional("settings")
        pinnedEnd = r("pinnedEnd", false)
        display = r.optional("display")
        surface = r.optional("surface")
        collapseWhenIdle = r("collapseWhenIdle", false)
        groupName = r.optional("groupName")
        groupAccent = r.optional("groupAccent")
        children = r.optional("children")
        extras = r.extras()
    }

    public func encode(to encoder: Encoder) throws {
        var w = JSONObjectWriter(encoder)
        try w("id", id)
        try w("kind", kind)
        try w.unlessNil("path", path)
        try w.unlessNil("arguments", arguments)
        try w.unlessNil("name", name)
        try w.unlessNil("widget", widget)
        try w.unlessNil("variant", variant)
        try w.unlessNil("settings", settings)
        try w.unlessFalse("pinnedEnd", pinnedEnd)
        try w.unlessNil("display", display)
        try w.unlessNil("surface", surface)
        try w.unlessFalse("collapseWhenIdle", collapseWhenIdle)
        try w.unlessNil("groupName", groupName)
        try w.unlessNil("groupAccent", groupAccent)
        try w.unlessNil("children", children)
        try w.extras(extras)
    }

    /// C#: Guid.NewGuid().ToString("N")[..10] -> 10 haneli hex.
    public static func newId() -> String {
        UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased().prefix(10).description
    }

    public static func app(_ path: String, name: String? = nil) -> DockItem {
        DockItem(kind: .app, path: path, name: name)
    }
    /// C#: ForWidget. Bazi widget'lar yeni eklenince bosta kucuk baslar.
    public static func widget(_ widget: String, variant: String? = nil) -> DockItem {
        DockItem(kind: .widget, widget: widget, variant: variant,
                 collapseWhenIdle: collapseByDefault.contains(widget))
    }

    /// C#: CollapseByDefault
    static let collapseByDefault: Set<String> = ["media", "notes", "reminders"]

    public static func separator() -> DockItem { DockItem(kind: .separator) }

    /// C#: DockItem.Group. Varsayilan vurgu AccentBlueBrush.
    public static func group(_ name: String, children: [DockItem] = []) -> DockItem {
        DockItem(kind: .group, groupName: name, groupAccent: "AccentBlueBrush", children: children)
    }
}
