import AppKit
import DockHubCore

/// Tema, vurgu rengi ve arka plan materyali.
/// Windows karsiligi: Core/ThemeManager.cs + Native/WindowEffects.cs
/// ag-theme-accent ve ag-blur-effect eslestirmeleri.
public enum Appearance {
    public static func nsAppearance(for theme: ThemePreference) -> NSAppearance? {
        switch theme {
        case .dark:   NSAppearance(named: .darkAqua)
        case .light:  NSAppearance(named: .aqua)
        case .system: nil                                  // sistemi izle
        }
    }

    /// Windows'taki Blur / Acrylic / Solid ayrimi macOS'ta materyal secimidir.
    public static func material(for backdrop: BackdropKind) -> NSVisualEffectView.Material {
        switch backdrop {
        case .blur:    .hudWindow
        case .acrylic: .underWindowBackground
        case .solid:   .windowBackground
        }
    }

    public static func blendingMode(for backdrop: BackdropKind) -> NSVisualEffectView.BlendingMode {
        backdrop == .solid ? .withinWindow : .behindWindow
    }

    public static var accentColor: NSColor { .controlAccentColor }

    /// Sistem temasi degisimini izler. ag-theme-accent: registry yoklama yerine
    /// DistributedNotificationCenter bildirimi gelir.
    public static func observeSystemTheme(_ handler: @escaping @Sendable () -> Void) -> NSObjectProtocol {
        DistributedNotificationCenter.default().addObserver(
            forName: Notification.Name("AppleInterfaceThemeChangedNotification"),
            object: nil, queue: .main) { _ in handler() }
    }
}
