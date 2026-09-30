import Foundation

/// Bir web widget'inin varyanti (boyut ve ad).
public struct WebWidgetVariant: Codable, Sendable, Equatable, Identifiable {
    public var id: String
    public var name: String
    /// "compact" (44x44), "standard" (170x44) veya "wide" (260x44).
    public var size: String

    public init(id: String = "default", name: String = "Default", size: String = "standard") {
        self.id = id
        self.name = name
        self.size = size
    }
    private enum CodingKeys: String, CodingKey {
        case id, name, size
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        self.id = try container.decodeIfPresent(String.self, forKey: .id) ?? "default"
        self.name = try container.decodeIfPresent(String.self, forKey: .name) ?? "Default"
        self.size = try container.decodeIfPresent(String.self, forKey: .size) ?? "standard"
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(id, forKey: .id)
        try container.encode(name, forKey: .name)
        try container.encode(size, forKey: .size)
    }
}

/// Web widget'inin manifestinde bildirdigi ayar alani.
public struct WebWidgetSetting: Codable, Sendable, Equatable, Identifiable {
    public var key: String
    /// "text", "number", "toggle" veya "choice".
    public var type: String
    public var label: String
    public var description: String?
    public var `default`: JSONValue?
    public var min: Double?
    public var max: Double?
    public var options: [String]?

    public var id: String { key }

    public init(
        key: String,
        type: String = "text",
        label: String = "",
        description: String? = nil,
        default: JSONValue? = nil,
        min: Double? = nil,
        max: Double? = nil,
        options: [String]? = nil
    ) {
        self.key = key
        self.type = type
        self.label = label
        self.description = description
        self.default = `default`
        self.min = min
        self.max = max
        self.options = options
    }

    private enum CodingKeys: String, CodingKey {
        case key, type, label, description, `default`, min, max, options
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        self.key = try container.decode(String.self, forKey: .key)
        self.type = try container.decodeIfPresent(String.self, forKey: .type) ?? "text"
        self.label = try container.decodeIfPresent(String.self, forKey: .label) ?? ""
        self.description = try container.decodeIfPresent(String.self, forKey: .description)
        self.default = try container.decodeIfPresent(JSONValue.self, forKey: .default)
        self.min = try container.decodeIfPresent(Double.self, forKey: .min)
        self.max = try container.decodeIfPresent(Double.self, forKey: .max)
        self.options = try container.decodeIfPresent([String].self, forKey: .options)
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(key, forKey: .key)
        try container.encode(type, forKey: .type)
        try container.encode(label, forKey: .label)
        try container.encodeIfPresent(description, forKey: .description)
        try container.encodeIfPresent(`default`, forKey: .default)
        try container.encodeIfPresent(min, forKey: .min)
        try container.encodeIfPresent(max, forKey: .max)
        try container.encodeIfPresent(options, forKey: .options)
    }
}

/// Web widget'inin guvenlik izinleri.
public struct WebWidgetPermissions: Codable, Sendable, Equatable {
    /// Widget'in erisebilecegi ana bilgisayarlar (tam ad veya "*.example.com").
    public var network: [String]
    /// Bildirim gonderme izni (dockhub.notify).
    public var notifications: Bool
    /// Kullanicinin girdigi sunucu adresini tutan metin ayarlarinin anahtarlari.
    public var networkFromSettings: [String]

    public init(network: [String] = [], notifications: Bool = false, networkFromSettings: [String] = []) {
        self.network = network
        self.notifications = notifications
        self.networkFromSettings = networkFromSettings
    }

    private enum CodingKeys: String, CodingKey {
        case network, notifications, networkFromSettings
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        self.network = try container.decodeIfPresent([String].self, forKey: .network) ?? []
        self.notifications = try container.decodeIfPresent(Bool.self, forKey: .notifications) ?? false
        self.networkFromSettings = try container.decodeIfPresent([String].self, forKey: .networkFromSettings) ?? []
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(network, forKey: .network)
        try container.encode(notifications, forKey: .notifications)
        try container.encode(networkFromSettings, forKey: .networkFromSettings)
    }
}

/// Web widget manifesti (manifest.json). Windows karsiligi: WebWidgetManifest.cs
public struct WebWidgetManifest: Codable, Sendable, Equatable, Identifiable {
    public var id: String
    public var name: String
    public var version: String
    public var author: String?
    public var description: String
    public var entry: String
    public var minDockHubVersion: String?
    public var variants: [WebWidgetVariant]
    public var settings: [WebWidgetSetting]
    public var permissions: WebWidgetPermissions
    public var files: [String]

    /// Disk konumu (JSON disinda saklanir).
    public var folder: URL?
    /// Indirildigi kaynak baglanti (guncellemeler icin).
    public var sourceLink: String?

    /// DockHub oge turu kimligi ("web." + manifest id).
    public var widgetId: String { "web." + id }

    /// Sanal ana bilgisayar adi.
    public var hostName: String { id.replacingOccurrences(of: ".", with: "-") + ".widget.dockhub" }

    public init(
        id: String,
        name: String,
        version: String = "1.0.0",
        author: String? = nil,
        description: String = "",
        entry: String = "index.html",
        minDockHubVersion: String? = nil,
        variants: [WebWidgetVariant] = [WebWidgetVariant()],
        settings: [WebWidgetSetting] = [],
        permissions: WebWidgetPermissions = WebWidgetPermissions(),
        files: [String] = [],
        folder: URL? = nil,
        sourceLink: String? = nil
    ) {
        self.id = id
        self.name = name
        self.version = version
        self.author = author
        self.description = description
        self.entry = entry
        self.minDockHubVersion = minDockHubVersion
        self.variants = variants.isEmpty ? [WebWidgetVariant()] : variants
        self.settings = settings
        self.permissions = permissions
        self.files = files
        self.folder = folder
        self.sourceLink = sourceLink
    }

    private enum CodingKeys: String, CodingKey {
        case id, name, version, author, description, entry, minDockHubVersion, variants, settings, permissions, files
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decode(String.self, forKey: .id)
        name = try container.decode(String.self, forKey: .name)
        version = try container.decodeIfPresent(String.self, forKey: .version) ?? "1.0.0"
        author = try container.decodeIfPresent(String.self, forKey: .author)
        description = try container.decodeIfPresent(String.self, forKey: .description) ?? ""
        entry = try container.decodeIfPresent(String.self, forKey: .entry) ?? "index.html"
        minDockHubVersion = try container.decodeIfPresent(String.self, forKey: .minDockHubVersion)
        let decodedVariants = try container.decodeIfPresent([WebWidgetVariant].self, forKey: .variants) ?? []
        variants = decodedVariants.isEmpty ? [WebWidgetVariant()] : decodedVariants
        settings = try container.decodeIfPresent([WebWidgetSetting].self, forKey: .settings) ?? []
        permissions = try container.decodeIfPresent(WebWidgetPermissions.self, forKey: .permissions) ?? WebWidgetPermissions()
        files = try container.decodeIfPresent([String].self, forKey: .files) ?? []
        folder = nil
        sourceLink = nil
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(id, forKey: .id)
        try container.encode(name, forKey: .name)
        try container.encode(version, forKey: .version)
        try container.encodeIfPresent(author, forKey: .author)
        try container.encode(description, forKey: .description)
        try container.encode(entry, forKey: .entry)
        try container.encodeIfPresent(minDockHubVersion, forKey: .minDockHubVersion)
        try container.encode(variants, forKey: .variants)
        try container.encode(settings, forKey: .settings)
        try container.encode(permissions, forKey: .permissions)
        try container.encode(files, forKey: .files)
    }
}
