import AppKit
import SwiftUI
import DockHubCore
import DockHubPlatform

/// Ekran kenarinda duran, her zaman ustte, cam efektli dock penceresi.
/// Windows karsiligi: Dock/DockWindow.xaml.cs
/// Eslestirmeler: ag-window-layer + ag-blur-effect
/// Gorsel dil: karar d-gorsel-hedef, oranlar DockStyle icinde.
@MainActor
public final class DockPanel {
    public let panel: NSPanel
    public let model: DockModel
    public private(set) var geometry: DockGeometry
    public let style: DockStyle
    /// "Fit content" icin olculen icerik uzunlugu; Full'da nil.
    private let contentLength: CGFloat?
    private let menuActions = MenuActions()

    /// Sag tik menusunden ayarlar penceresini acar; AppDelegate baglar.
    public var onOpenSettings: ((SettingsPage) -> Void)? {
        get { menuActions.openSettings }
        set { menuActions.openSettings = newValue }
    }

    public init(config: AppConfig, items: [DockItem], service: ConfigService? = nil) {
        let screen = ScreenPlacement.screen(named: config.monitorDevice) ?? NSScreen.screens[0]
        // Dikey dock'ta "kalinlik" genisliktir; stil her zaman kalinliktan turer.
        self.style = DockStyle(height: CGFloat(config.size.thickness))
        self.model = DockModel(config: config, items: items, service: service)

        // Icerik gorunumu yerlesimden ONCE kurulur: "Fit content" (widthMode .fit)
        // dock'un uzunlugunu icerigin gercek boyutundan alir. Windows'ta da Fit
        // genisligi ogelerden hesaplanir; eskiden burada sabit 420 vardi.
        let host = NSHostingView(rootView: DockContentView(model: model, style: style))
        if config.widthMode == .fit {
            let ideal = host.fittingSize
            contentLength = ceil(config.edge.isVertical ? ideal.height : ideal.width)
        } else {
            contentLength = nil
        }
        self.geometry = ScreenPlacement.geometry(for: config, on: screen, contentLength: contentLength)

        panel = NSPanel(contentRect: geometry.frame,
                        styleMask: [.nonactivatingPanel, .borderless],
                        backing: .buffered, defer: false)

        // Ayarlar degisince panel kapatilip yenisi kurulur; Swift referansi
        // tutarken AppKit'in ayrica serbest birakmasi cokmeye yol acar.
        panel.isReleasedWhenClosed = false

        // ag-window-layer
        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        panel.isFloatingPanel = true
        panel.hidesOnDeactivate = false
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false          // golgeyi kendimiz ciziyoruz (yuvarlak koseye uysun)
        panel.appearance = Appearance.nsAppearance(for: config.theme)

        let bounds = CGRect(origin: .zero, size: geometry.frame.size)
        let radius = config.layout == .floating ? style.cornerRadius : 0

        // --- Dis kap: yalniz golge tasir, kirpilmaz
        let container = NSView(frame: bounds)
        container.wantsLayer = true
        container.autoresizingMask = [.width, .height]
        if let l = container.layer {
            l.masksToBounds = false
            l.shadowColor = DockStyle.shadowColor.cgColor
            l.shadowOpacity = 1
            l.shadowRadius = style.shadowRadius
            l.shadowOffset = CGSize(width: 0, height: style.shadowOffsetY)
            l.shadowPath = CGPath(roundedRect: bounds, cornerWidth: radius,
                                  cornerHeight: radius, transform: nil)
        }

        // --- Cam katman: ag-blur-effect
        let fx = NSVisualEffectView(frame: bounds)
        fx.material = Appearance.material(for: config.backdrop)
        fx.blendingMode = Appearance.blendingMode(for: config.backdrop)
        fx.state = .active
        fx.autoresizingMask = [.width, .height]
        fx.wantsLayer = true
        fx.layer?.cornerRadius = radius
        fx.layer?.cornerCurve = .continuous
        fx.layer?.masksToBounds = true

        // Ton katmani (config.tintOpacity)
        let tint = NSView(frame: bounds)
        tint.autoresizingMask = [.width, .height]
        tint.wantsLayer = true
        tint.layer?.backgroundColor = NSColor.black
            .withAlphaComponent(config.clampedTintOpacity * 0.45).cgColor
        fx.addSubview(tint)

        // Icerik
        host.frame = bounds
        host.autoresizingMask = [.width, .height]
        fx.addSubview(host)

        // --- Cam kenar isigi: ust belirgin, alt cok hafif.
        // Cam hissini veren asil ayrinti; kirpildigi icin yuvarlak koseyi izler.
        let top = NSView(frame: CGRect(x: 0, y: bounds.maxY - 1, width: bounds.width, height: 1))
        top.autoresizingMask = [.width, .minYMargin]
        top.wantsLayer = true
        top.layer?.backgroundColor = DockStyle.topEdgeLight.cgColor
        fx.addSubview(top)

        let bottom = NSView(frame: CGRect(x: 0, y: 0, width: bounds.width, height: 1))
        bottom.autoresizingMask = [.width, .maxYMargin]
        bottom.wantsLayer = true
        bottom.layer?.backgroundColor = DockStyle.bottomEdgeLight.cgColor
        fx.addSubview(bottom)

        container.addSubview(fx)
        panel.contentView = container

        // Dock menusu. Tam wf-dock-menu (sabitleme, ayirici, konum, otomatik
        // gizleme) ayri gorev; burada ayarlara giden yol ve cikis var.
        let menu = NSMenu()
        let widget = NSMenuItem(title: "Add Widget…", action: #selector(MenuActions.openGallery), keyEquivalent: "")
        widget.target = menuActions
        menu.addItem(widget)
        let settings = NSMenuItem(title: "Settings…", action: #selector(MenuActions.openGeneral), keyEquivalent: ",")
        settings.target = menuActions
        menu.addItem(settings)
        menu.addItem(.separator())
        let quit = NSMenuItem(title: "Quit DockHub",
                              action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        quit.target = NSApp
        menu.addItem(quit)
        fx.menu = menu
    }

    /// Paneli ekrandan kaldirir; ayarlar degisince yeni panel kurulmadan once.
    public func close() {
        panel.orderOut(nil)
        panel.close()
    }

    public func show() {
        panel.setFrame(geometry.frame, display: true)
        panel.orderFrontRegardless()
    }

    /// Ekran duzeni degistiginde konumu yeniden hesaplar.
    /// Sistem Dock'u gizlendiginde visibleFrame buyur; cozunurluk degisiminde
    /// ve monitor takilip cikarildiginda da ayni yol calisir.
    /// ONEMLI: bu bildirimi beklemek yerine ana is parcacigini uyutmak ise
    /// yaramaz - AppKit bildirim isleyemedigi icin NSScreen eski degeri doner.
    @discardableResult
    public func reposition(config: AppConfig) -> Bool {
        let screen = ScreenPlacement.screen(named: config.monitorDevice) ?? NSScreen.screens[0]
        let yeni = ScreenPlacement.geometry(for: config, on: screen, contentLength: contentLength)
        guard yeni.frame != geometry.frame else { return false }
        geometry = yeni
        panel.setFrame(yeni.frame, display: true, animate: false)
        return true
    }

    public func observation() -> [String: String] {
        let f = panel.frame
        let apps = model.items.filter { $0.kind == .app }
        let ikonlu = apps.filter { $0.path.flatMap { AppCatalog.icon(forAppAt: $0) } != nil }
        return [
            "isVisible": String(panel.isVisible),
            "occlusionVisible": String(panel.occlusionState.contains(.visible)),
            "frame": "x=\(Int(f.minX)) y=\(Int(f.minY)) w=\(Int(f.width)) h=\(Int(f.height))",
            "screen": geometry.screenName,
            "ogeSayisi": String(model.items.count),
            "uygulamaSayisi": String(apps.count),
            "ikonuCozulen": String(ikonlu.count),
            "calisanSayisi": String(model.runningPaths.count),
            "calisanlar": model.runningPaths.map { ($0 as NSString).lastPathComponent }.sorted().joined(separator: ", "),
            "cornerRadius": String(format: "%.1f", style.cornerRadius),
            "padding": String(format: "%.1f", style.padding),
            "ogeYuksekligi": String(format: "%.1f", style.itemHeight),
            "ikonBoyutu": String(format: "%.1f", style.iconSize),
        ]
    }
}

/// NSMenuItem hedefi: DockPanel NSObject olmadigi icin menu eylemleri burada.
@MainActor
final class MenuActions: NSObject {
    var openSettings: ((SettingsPage) -> Void)?
    @objc func openGeneral() { openSettings?(.general) }
    @objc func openGallery() { openSettings?(.gallery) }
}
