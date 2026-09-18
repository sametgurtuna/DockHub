import AppKit
import SwiftUI
import DockHubCore
import DockHubPlatform

/// Ekran kenarinda duran, her zaman ustte, cam efektli dock penceresi.
/// Windows karsiligi: Dock/DockWindow.xaml.cs
/// ag-window-layer + ag-blur-effect eslestirmeleri.
@MainActor
public final class DockPanel {
    public let panel: NSPanel
    public private(set) var geometry: DockGeometry

    public init(config: AppConfig) {
        let screen = ScreenPlacement.screen(named: config.monitorDevice) ?? NSScreen.screens[0]
        self.geometry = ScreenPlacement.geometry(for: config, on: screen)

        panel = NSPanel(contentRect: geometry.frame,
                        styleMask: [.nonactivatingPanel, .borderless],
                        backing: .buffered, defer: false)

        // ag-window-layer: her zaman ustte, tum Space'lerde, tiklamada one gelmez
        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        panel.isFloatingPanel = true
        panel.hidesOnDeactivate = false
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.appearance = Appearance.nsAppearance(for: config.theme)

        // ag-blur-effect: NSVisualEffectView
        let fx = NSVisualEffectView(frame: CGRect(origin: .zero, size: geometry.frame.size))
        fx.material = Appearance.material(for: config.backdrop)
        fx.blendingMode = Appearance.blendingMode(for: config.backdrop)
        fx.state = .active
        fx.autoresizingMask = [.width, .height]
        fx.wantsLayer = true
        fx.layer?.cornerRadius = geometry.cornerRadius
        fx.layer?.masksToBounds = true

        // Windows'taki tintOpacity karsiligi: materyal uzerine yari saydam katman
        let tint = NSView(frame: fx.bounds)
        tint.autoresizingMask = [.width, .height]
        tint.wantsLayer = true
        tint.layer?.backgroundColor = NSColor.black
            .withAlphaComponent(config.clampedTintOpacity * 0.35).cgColor
        fx.addSubview(tint)

        let host = NSHostingView(rootView: DockContentView(config: config,
                                                           screenName: geometry.screenName))
        host.frame = fx.bounds
        host.autoresizingMask = [.width, .height]
        fx.addSubview(host)

        panel.contentView = fx

        // Asgari cikis yolu. LSUIElement oldugu icin menu cubugu yok; tam
        // wf-dock-menu (widget ekle, uygulama sabitle, konum...) sonraki gorevin isi.
        let menu = NSMenu()
        menu.addItem(withTitle: "DockHub — iskelet surum", action: nil, keyEquivalent: "")
            .isEnabled = false
        menu.addItem(.separator())
        let quit = NSMenuItem(title: "DockHub'dan Cik",
                              action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        quit.target = NSApp
        menu.addItem(quit)
        fx.menu = menu
    }

    public func show() {
        panel.setFrame(geometry.frame, display: true)
        panel.orderFrontRegardless()
    }

    /// Programatik gozlem: pencere gercekten ekranda mi?
    public func observation() -> [String: String] {
        let f = panel.frame
        return [
            "isVisible": String(panel.isVisible),
            "occlusionVisible": String(panel.occlusionState.contains(.visible)),
            "level": String(panel.level.rawValue),
            "frame": "x=\(Int(f.minX)) y=\(Int(f.minY)) w=\(Int(f.width)) h=\(Int(f.height))",
            "screen": geometry.screenName,
            "cornerRadius": String(Int(geometry.cornerRadius)),
        ]
    }
}
