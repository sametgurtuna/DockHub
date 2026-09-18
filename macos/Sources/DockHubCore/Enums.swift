// Windows surumunun enum'lari. JsonStringEnumConverter C# uye adini oldugu gibi
// yazdigi icin ham degerler PascalCase olmak ZORUNDA (MIMARI.md bolum 3).

public enum DockEdge: String, Codable, Sendable, CaseIterable {
    case bottom = "Bottom", top = "Top", left = "Left", right = "Right"

    public var isVertical: Bool { self == .left || self == .right }
}

public enum TaskbarMode: String, Codable, Sendable {
    case replace = "Replace", showBoth = "ShowBoth"
}

public enum ThemePreference: String, Codable, Sendable {
    case dark = "Dark", light = "Light", system = "System"
}

public enum BackdropKind: String, Codable, Sendable {
    case blur = "Blur", acrylic = "Acrylic", solid = "Solid"
}

public enum DockSize: String, Codable, Sendable {
    case small = "Small", medium = "Medium", large = "Large"

    /// Windows'taki DIP degerleri; macOS'ta 1 point = 1 DIP.
    public var thickness: Double {
        switch self {
        case .small: 48
        case .medium: 56
        case .large: 66
        }
    }
}

public enum DockLayout: String, Codable, Sendable {
    case floating = "Floating", attached = "Attached"
}

public enum DockWidthMode: String, Codable, Sendable {
    case full = "Full", fit = "Fit"
}

public enum DockAlignment: String, Codable, Sendable {
    case start = "Start", center = "Center"
}

public enum DockItemKind: String, Codable, Sendable {
    case app = "App", widget = "Widget", separator = "Separator"
}
