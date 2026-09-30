import Foundation

/// Ust bar: ana ekranda widget'lari (ve saati) tasiyan ikinci, ince dock.
/// Windows karsiligi: Core/TopBarSettings.cs. Kapali baslar.
///
/// macOS'ta menu cubugu zaten ustte oldugu icin bar ancak menu cubugunun
/// altinda ya da baska bir kenarda durur ve ekran alani ayiramaz
/// (docs/PARITE-1.0.md, "Ust bar": uyarla, P2). Ayarlar yine de okunur ve aynen
/// geri yazilir; dosya Windows'a donunce bar kaybolmaz.
public struct TopBarSettings: Codable, Sendable, Equatable {
    public var enabled = false
    public var edge: DockEdge = .top
    public var size: DockSize = .small
    public var layout: DockLayout = .attached
    /// Barin kendi arka plani; nil ise dock'unki. C#'ta nitelik yok: nil `null` yazilir.
    public var backdrop: BackdropKind?
    public var autoHide = false
    public var showClock = true
    public var extras: [String: JSONValue] = [:]

    public init() {}

    public init(from decoder: Decoder) throws {
        var r = try JSONObjectReader(decoder)
        let d = TopBarSettings()
        enabled = r("enabled", d.enabled)
        edge = r("edge", d.edge)
        size = r("size", d.size)
        layout = r("layout", d.layout)
        backdrop = r.optional("backdrop")
        autoHide = r("autoHide", d.autoHide)
        showClock = r("showClock", d.showClock)
        extras = r.extras()
    }

    public func encode(to encoder: Encoder) throws {
        var w = JSONObjectWriter(encoder)
        try w("enabled", enabled)
        try w("edge", edge)
        try w("size", size)
        try w("layout", layout)
        try w("backdrop", backdrop)
        try w("autoHide", autoHide)
        try w("showClock", showClock)
        try w.extras(extras)
    }

    /// C#: EdgeFor. Barin kendi kenari; dock oradaysa karsi kenar.
    public static func edge(for barEdge: DockEdge, dockEdge: DockEdge) -> DockEdge {
        barEdge != dockEdge ? barEdge : opposite(barEdge)
    }

    public static func opposite(_ edge: DockEdge) -> DockEdge {
        switch edge {
        case .top: .bottom
        case .bottom: .top
        case .left: .right
        case .right: .left
        }
    }
}
