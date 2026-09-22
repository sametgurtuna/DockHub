import AppKit
import SwiftUI
import UniformTypeIdentifiers
import DockHubCore
import DockHubPlatform

/// Windows karsiligi: Settings/SettingsWindow.Items.cs ("Dock items" sayfasi).
/// Liste solda (surukleyerek siralama), secili ogenin ayrintisi sagda.
/// Widget'a ozel ayar formlari (Windows: WidgetSettingsTemplates.xaml) ayar
/// anahtarlari Windows'la esitlenince eklenecek (docs/WIDGET-SEMASI.md bolum 4).
struct ItemsPage: View {
    @ObservedObject var store: SettingsStore
    let openGallery: () -> Void
    @State private var selection: String?
    @State private var note: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 8) {
                Button { addApps() } label: { Label("Add App…", systemImage: "plus") }
                    .buttonStyle(.borderedProminent)
                Button(action: openGallery) { Label("Add Widget…", systemImage: "square.grid.2x2") }
                Button { store.addSeparator() } label: { Label("Add Separator", systemImage: "minus") }
                Button {
                    let n = store.importDockApps()
                    note = n == 0 ? "All macOS Dock apps are already on the dock." : "Added \(n) app\(n == 1 ? "" : "s") from the macOS Dock."
                } label: { Label("Import from Dock", systemImage: "square.and.arrow.down") }
                    .help("Adds the apps pinned to the macOS Dock")
                Spacer()
            }
            if let note { Text(note).font(.caption).foregroundStyle(.secondary) }

            HStack(alignment: .top, spacing: 0) {
                List(selection: $selection) {
                    ForEach(store.config.items) { item in
                        ItemRow(item: item).tag(item.id)
                    }
                    .onMove { store.moveItems(from: $0, to: $1) }
                }
                .contextMenu(forSelectionType: String.self) { ids in
                    Button("Remove from Dock") { ids.forEach(store.removeItem) }
                }
                .onDeleteCommand { if let id = selection { remove(id) } }
                .frame(width: 320)

                Divider()

                Group {
                    if let id = selection, let item = store.config.items.first(where: { $0.id == id }) {
                        ItemDetail(item: item, store: store) { remove(item.id) }
                            .id(item.id)
                    } else {
                        Text("Select an item to view details.")
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, maxHeight: .infinity)
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            }
            .background(Color(nsColor: .controlBackgroundColor))
            .clipShape(RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color(nsColor: .separatorColor)))
        }
        .padding(20)
    }

    private func remove(_ id: String) {
        let sira = store.config.items.firstIndex { $0.id == id }
        store.removeItem(id)
        // Silinenin yerindeki ogeye gec; liste bossa secim kalkar.
        let items = store.config.items
        selection = sira.flatMap { items.isEmpty ? nil : items[min($0, items.count - 1)].id }
    }

    private func addApps() {
        let panel = NSOpenPanel()
        panel.title = "Add App"
        panel.prompt = "Add"
        panel.allowedContentTypes = [.application]
        panel.allowsMultipleSelection = true
        panel.directoryURL = URL(fileURLWithPath: "/Applications")
        guard panel.runModal() == .OK else { return }
        let n = store.addApps(panel.urls.map(\.path))
        if n < panel.urls.count { note = "\(panel.urls.count - n) app(s) were already on the dock." } else { note = nil }
    }
}

/// Listedeki bir satir: ikon, baslik, alt baslik.
struct ItemRow: View {
    let item: DockItem

    var body: some View {
        HStack(spacing: 10) {
            ItemIcon(item: item, size: 24)
            VStack(alignment: .leading, spacing: 1) {
                Text(ItemText.title(item)).lineLimit(1)
                let alt = ItemText.subtitle(item)
                if !alt.isEmpty {
                    Text(alt).font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
                }
            }
        }
        .padding(.vertical, 2)
    }
}

struct ItemIcon: View {
    let item: DockItem
    let size: CGFloat

    var body: some View {
        Group {
            switch item.kind {
            case .app:
                if let p = item.path, let icon = AppCatalog.icon(forAppAt: p) {
                    Image(nsImage: icon).resizable().interpolation(.high)
                } else {
                    Image(systemName: "questionmark.app.dashed").resizable().scaledToFit()
                }
            case .widget:
                Image(systemName: WidgetCatalog.info(item.widget ?? "").symbol)
                    .resizable().scaledToFit().padding(size * 0.18)
                    .foregroundStyle(Color.accentColor)
                    .background(RoundedRectangle(cornerRadius: size * 0.24).fill(Color.accentColor.opacity(0.12)))
            case .separator:
                Rectangle().fill(Color.secondary.opacity(0.5)).frame(width: 2).frame(maxWidth: .infinity)
            case .group:
                Image(systemName: "folder.fill").resizable().scaledToFit()
                    .foregroundStyle(Color(nsColor: DockStyle.groupAccent(item.groupAccent)))
            }
        }
        .frame(width: size, height: size)
    }
}

/// Satir ve ayrinti metinleri. AppCatalog MainActor oldugu icin burada da.
@MainActor
enum ItemText {
    static func title(_ item: DockItem) -> String {
        switch item.kind {
        case .app: item.name ?? item.path.map(AppCatalog.displayName(forAppAt:)) ?? "App"
        case .widget: WidgetRegistry.find(item.widget)?.name ?? "Unknown widget (\(item.widget ?? "-"))"
        case .separator: "Separator"
        case .group: item.groupName ?? "Folder"
        }
    }

    static func subtitle(_ item: DockItem) -> String {
        switch item.kind {
        case .app: (item.path as NSString?)?.abbreviatingWithTildeInPath ?? ""
        case .widget:
            WidgetRegistry.find(item.widget)?.variants.first { $0.id == item.effectiveVariant }?.name
                ?? item.effectiveVariant
        case .separator: ""
        case .group: "\(item.children?.count ?? 0) item\(item.children?.count == 1 ? "" : "s")"
        }
    }
}

/// Secili ogenin ayrintisi. Windows'taki DetailPanel (AppDetail / WidgetDetail).
struct ItemDetail: View {
    let item: DockItem
    @ObservedObject var store: SettingsStore
    let remove: () -> Void
    @State private var name = ""
    @State private var arguments = ""

    var body: some View {
        Form {
            Section {
                HStack(spacing: 14) {
                    ItemIcon(item: item, size: 44)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(ItemText.title(item)).font(.title3.weight(.semibold))
                        Text(kindText).font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
            switch item.kind {
            case .app: appSection
            case .widget: widgetSection
            case .group: groupSection
            case .separator: EmptyView()
            }
            Section {
                Button("Remove from Dock", role: .destructive, action: remove)
            }
        }
        .formStyle(.grouped)
        .onAppear {
            name = item.kind == .group ? (item.groupName ?? "") : (item.name ?? "")
            arguments = item.arguments ?? ""
        }
    }

    private var kindText: String {
        switch item.kind {
        case .app: "App"
        case .widget: WidgetCatalog.info(item.widget ?? "").summary
        case .separator: "Separator"
        case .group: "Folder"
        }
    }

    @ViewBuilder private var appSection: some View {
        Section {
            VStack(alignment: .leading, spacing: 4) {
                TextField("Display name", text: $name,
                          prompt: Text(item.path.map(AppCatalog.displayName(forAppAt:)) ?? ""))
                    .onSubmit { store.setName(item.id, name) }
                RowNote("Uses the app name if left empty. Press Return to save.")
            }
            LabeledContent("Target") {
                HStack {
                    Text((item.path as NSString?)?.abbreviatingWithTildeInPath ?? "-")
                        .lineLimit(1).truncationMode(.middle).foregroundStyle(.secondary)
                    Button("Show in Finder") {
                        if let p = item.path { NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: p)]) }
                    }
                }
            }
            VStack(alignment: .leading, spacing: 4) {
                TextField("Launch arguments", text: $arguments, prompt: Text("none"))
                    .onSubmit { store.setArguments(item.id, arguments) }
                RowNote("Passed to the app when DockHub launches it. Press Return to save.")
            }
        }
    }

    @ViewBuilder private var widgetSection: some View {
        if let tanim = WidgetRegistry.find(item.widget) {
            Section {
                if tanim.variants.count > 1 {
                    Picker("Layout", selection: Binding(get: { item.effectiveVariant },
                                                        set: { store.setVariant(item.id, $0) })) {
                        ForEach(tanim.variants) { Text($0.name).tag($0.id) }
                    }
                    RowNote("Appearance of the widget on the dock.")
                } else {
                    LabeledContent("Layout", value: tanim.variants.first?.name ?? "-")
                }
            }
        } else {
            Section {
                RowNote("This widget is not available on macOS. It stays in the configuration so the file still works on Windows.")
            }
        }
    }

    @ViewBuilder private var groupSection: some View {
        Section {
            VStack(alignment: .leading, spacing: 4) {
                TextField("Name", text: $name).onSubmit { store.setGroupName(item.id, name) }
                RowNote("Press Return to save.")
            }
            ForEach(item.children ?? []) { child in
                HStack(spacing: 8) {
                    ItemIcon(item: child, size: 18)
                    Text(ItemText.title(child))
                }
            }
            RowNote("Opening folders on the dock and moving items in and out of them are not available on macOS yet.")
        }
    }
}
