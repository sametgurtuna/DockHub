import Foundation

/// Dock'taki tek bir oge. Windows karsiligi: Core/AppConfig.cs -> DockItem.
/// Path/arguments/name/widget/variant/settings alanlari C# tarafinda
/// [JsonIgnore(WhenWritingNull)] tasiyor, yani nil ise YAZILMAZ.
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

    public init(id: String = DockItem.newId(), kind: DockItemKind,
                path: String? = nil, arguments: String? = nil, name: String? = nil,
                widget: String? = nil, variant: String? = nil, settings: JSONValue? = nil) {
        self.id = id; self.kind = kind; self.path = path; self.arguments = arguments
        self.name = name; self.widget = widget; self.variant = variant; self.settings = settings
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
}
