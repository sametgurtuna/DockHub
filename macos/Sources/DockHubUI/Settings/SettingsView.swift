import AppKit
import SwiftUI
import DockHubCore

/// Ayarlar penceresinin sayfalari. Windows'taki sayfalardan "Taskbar"
/// (Baslat, Arama, Gorev Gorunumu, tepsi, saat) yok: o dugmelerin hicbiri
/// macOS dock'unda henuz uygulanmiyor, islevsiz anahtar gostermiyoruz.
public enum SettingsPage: String, CaseIterable, Identifiable, Sendable {
    case general, appearance, items, gallery, about

    public var id: String { rawValue }

    var title: String {
        switch self {
        case .general: "General"
        case .appearance: "Appearance"
        case .items: "Dock Items"
        case .gallery: "Widget Gallery"
        case .about: "About"
        }
    }

    var subtitle: String {
        switch self {
        case .general: "macOS Dock, display and maintenance."
        case .appearance: "Style, size, shape and position of the dock."
        case .items: "Apps, widgets and separators, in the order they appear on the dock."
        case .gallery: "Pick a widget and a layout, then add it to the dock. Every copy keeps its own settings."
        case .about: ""
        }
    }

    var symbol: String {
        switch self {
        case .general: "gearshape"
        case .appearance: "paintbrush"
        case .items: "square.grid.3x1.below.line.grid.1x2"
        case .gallery: "square.grid.2x2"
        case .about: "info.circle"
        }
    }
}

@MainActor
final class SettingsNavigation: ObservableObject {
    @Published var page: SettingsPage = .general
}

struct SettingsView: View {
    @ObservedObject var store: SettingsStore
    @ObservedObject var nav: SettingsNavigation

    var body: some View {
        NavigationSplitView {
            List(SettingsPage.allCases, selection: Binding(get: { nav.page }, set: { nav.page = $0 ?? nav.page })) { page in
                Label(page.title, systemImage: page.symbol).tag(page)
            }
            .navigationSplitViewColumnWidth(min: 180, ideal: 200, max: 240)
        } detail: {
            page
                .navigationTitle(nav.page.title)
                .navigationSubtitle(nav.page.subtitle)
        }
    }

    @ViewBuilder private var page: some View {
        switch nav.page {
        case .general: GeneralPage(store: store)
        case .appearance: AppearancePage(store: store)
        case .items: ItemsPage(store: store) { nav.page = .gallery }
        case .gallery: GalleryPage(store: store)
        case .about: AboutPage()
        }
    }
}

/// Ayarlar penceresi. Windows karsiligi: Settings/SettingsWindow.xaml (Mica pencere).
/// Uygulama .accessory oldugu icin pencere acilirken uygulama one alinir.
@MainActor
public final class SettingsWindowController: NSWindowController {
    private let store: SettingsStore
    private let nav = SettingsNavigation()

    public init(store: SettingsStore) {
        self.store = store
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 940, height: 660),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                              backing: .buffered, defer: false)
        window.title = "DockHub Settings"
        window.isReleasedWhenClosed = false
        window.minSize = NSSize(width: 840, height: 560)
        window.toolbarStyle = .unified
        super.init(window: window)
        let hosting = NSHostingController(rootView: SettingsView(store: store, nav: nav))
        // Varsayilan sizingOptions pencereyi icerigin en kucuk boyutuna ceker.
        hosting.sizingOptions = []
        window.contentViewController = hosting
        window.setContentSize(NSSize(width: 940, height: 660))
        if !window.setFrameUsingName("DockHubSettings") { window.center() }
        window.setFrameAutosaveName("DockHubSettings")
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("init(coder:) kullanilmiyor") }

    public var page: SettingsPage { nav.page }

    public func show(_ page: SettingsPage) {
        nav.page = page
        store.reload()
        NSApp.activate()
        window?.makeKeyAndOrderFront(nil)
        // Komut satirindan (--settings) acilista uygulama one gelemeyebilir;
        // pencere yine de gorunsun.
        window?.orderFrontRegardless()
    }

    /// Sayfayi acip pencere icerigini PNG olarak yazar. Kendi penceremizi
    /// cizdigi icin Ekran Kaydi izni gerekmez (screencapture'in aksine).
    /// Pencere arka plani ve materyaller ekrandakiyle birebir olmayabilir.
    public func snapshot(_ page: SettingsPage, to url: URL) async -> Bool {
        show(page)
        try? await Task.sleep(for: .milliseconds(700))
        guard let view = window?.contentView?.superview ?? window?.contentView else { return false }
        view.layoutSubtreeIfNeeded()
        guard let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { return false }
        view.cacheDisplay(in: view.bounds, to: rep)
        guard let png = rep.representation(using: .png, properties: [:]) else { return false }
        return (try? png.write(to: url)) != nil
    }
}

// ---------------- Ortak parcalar

/// Degeri birakinca kaydeden kaydirici: her adimda config yazip dock'u
/// yeniden kurmak yerine yalniz surukleme bitince bir kez yazar.
struct CommitSlider: View {
    let value: Double
    let range: ClosedRange<Double>
    let step: Double
    let label: (Double) -> String
    let commit: (Double) -> Void
    @State private var draft: Double?

    var body: some View {
        HStack(spacing: 10) {
            Slider(value: Binding(get: { draft ?? value }, set: { draft = $0 }),
                   in: range, step: step) { editing in
                if !editing, let d = draft { commit(d); draft = nil }
            }
            .frame(width: 200)
            Text(label(draft ?? value))
                .monospacedDigit()
                .foregroundStyle(.secondary)
                .frame(width: 44, alignment: .trailing)
        }
    }
}

/// Satir altina aciklama: Windows'taki SettingRow Description karsiligi.
struct RowNote: View {
    let text: String
    init(_ text: String) { self.text = text }
    var body: some View {
        Text(text).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
    }
}
