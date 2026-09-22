import Foundation

/// Dock'taki tek bir oge. Windows karsiligi: Core/AppConfig.cs -> DockItem.
/// Path/arguments/name/widget/variant/settings ve grup alanlari C# tarafinda
/// [JsonIgnore(WhenWritingNull)] tasiyor, yani nil ise YAZILMAZ.
/// `pinnedEnd` [JsonIgnore(WhenWritingDefault)] tasiyor: false ise YAZILMAZ.
public struct DockItem: Codable, Sendable, Identifiable {
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
    /// Yalniz gruplar (klasor).
    public var groupName: String?
    /// WPF firca anahtari ("AccentBlueBrush" vb.). Dosya Windows'la ortak
    /// kalsin diye ayni ad yazilir; renge cevirme arayuz katmaninda.
    public var groupAccent: String?
    public var children: [DockItem]?

    public init(id: String = DockItem.newId(), kind: DockItemKind,
                path: String? = nil, arguments: String? = nil, name: String? = nil,
                widget: String? = nil, variant: String? = nil, settings: JSONValue? = nil,
                pinnedEnd: Bool = false, groupName: String? = nil, groupAccent: String? = nil,
                children: [DockItem]? = nil) {
        self.id = id; self.kind = kind; self.path = path; self.arguments = arguments
        self.name = name; self.widget = widget; self.variant = variant; self.settings = settings
        self.pinnedEnd = pinnedEnd; self.groupName = groupName; self.groupAccent = groupAccent
        self.children = children
    }

    enum CodingKeys: String, CodingKey {
        case id, kind, path, arguments, name, widget, variant, settings
        case pinnedEnd, groupName, groupAccent, children
    }

    /// Eksik alan varsayilanla okunur (System.Text.Json davranisi):
    /// id yoksa yeni kimlik, kind yoksa App (C# enum'unun sifir degeri).
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(String.self, forKey: .id) ?? DockItem.newId()
        kind = try c.decodeIfPresent(DockItemKind.self, forKey: .kind) ?? .app
        path = try c.decodeIfPresent(String.self, forKey: .path)
        arguments = try c.decodeIfPresent(String.self, forKey: .arguments)
        name = try c.decodeIfPresent(String.self, forKey: .name)
        widget = try c.decodeIfPresent(String.self, forKey: .widget)
        variant = try c.decodeIfPresent(String.self, forKey: .variant)
        settings = try c.decodeIfPresent(JSONValue.self, forKey: .settings)
        pinnedEnd = try c.decodeIfPresent(Bool.self, forKey: .pinnedEnd) ?? false
        groupName = try c.decodeIfPresent(String.self, forKey: .groupName)
        groupAccent = try c.decodeIfPresent(String.self, forKey: .groupAccent)
        children = try c.decodeIfPresent([DockItem].self, forKey: .children)
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(kind, forKey: .kind)
        try c.encodeIfPresent(path, forKey: .path)
        try c.encodeIfPresent(arguments, forKey: .arguments)
        try c.encodeIfPresent(name, forKey: .name)
        try c.encodeIfPresent(widget, forKey: .widget)
        try c.encodeIfPresent(variant, forKey: .variant)
        try c.encodeIfPresent(settings, forKey: .settings)
        if pinnedEnd { try c.encode(pinnedEnd, forKey: .pinnedEnd) }   // WhenWritingDefault
        try c.encodeIfPresent(groupName, forKey: .groupName)
        try c.encodeIfPresent(groupAccent, forKey: .groupAccent)
        try c.encodeIfPresent(children, forKey: .children)
    }

    /// C#: Guid.NewGuid().ToString("N")[..10] -> 10 haneli hex.
    public static func newId() -> String {
        UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased().prefix(10).description
    }

    public static func app(_ path: String, name: String? = nil) -> DockItem {
        DockItem(kind: .app, path: path, name: name)
    }
    public static func widget(_ widget: String, variant: String? = nil) -> DockItem {
        DockItem(kind: .widget, widget: widget, variant: variant)
    }
    public static func separator() -> DockItem { DockItem(kind: .separator) }

    /// C#: DockItem.Group. Varsayilan vurgu AccentBlueBrush.
    public static func group(_ name: String, children: [DockItem] = []) -> DockItem {
        DockItem(kind: .group, groupName: name, groupAccent: "AccentBlueBrush", children: children)
    }
}
