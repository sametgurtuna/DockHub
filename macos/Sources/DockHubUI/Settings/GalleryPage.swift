import AppKit
import SwiftUI
import DockHubCore

/// Windows karsiligi: Settings/SettingsWindow.Gallery.cs ("Widget gallery").
/// Windows kartlarda canli onizleme gosteriyor; burada simge ve aciklama var
/// (fark docs/AYARLAR.md'de).
struct GalleryPage: View {
    @ObservedObject var store: SettingsStore
    @State private var chosen: [String: String] = [:]
    @State private var justAdded: String?
    @State private var query = ""
    @State private var refreshTrigger = 0

    private var allWidgets: [WidgetDefinition] {
        var list = WidgetRegistry.all
        _ = refreshTrigger
        let webWidgets = WebWidgetCatalog.shared.installed.map { WebWidgetCatalog.definition(for: $0) }
        list.append(contentsOf: webWidgets)
        return list
    }

    /// Arama: yerel ve Ingilizce ad, aciklama ve kategori (Windows: GalleryFilter).
    private var visible: [WidgetDefinition] {
        allWidgets.filter { def in
            GalleryFilter.matches(query, [def.displayName, def.name, def.displayCategory, def.category,
                                          WidgetCatalog.info(def.id).summary, WidgetCatalog.englishSummary(def.id)])
        }
    }

    private var groups: [(String, [WidgetDefinition])] {
        let byCategory = Dictionary(grouping: visible, by: \.category)
        let order = WidgetCatalog.categoryOrder + byCategory.keys.filter { !WidgetCatalog.categoryOrder.contains($0) }.sorted()
        return order.compactMap { cat in byCategory[cat].map { (cat, $0) } }
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 22) {
                VStack(alignment: .leading, spacing: 6) {
                    HStack(spacing: 8) {
                        TextField(L.t("Search widgets"), text: $query)
                            .textFieldStyle(.roundedBorder)
                            .frame(maxWidth: 320)
                        Button {
                            pickAndInstallWidget()
                        } label: {
                            Label(L.t("Install widget…"), systemImage: "plus.rectangle.on.folder")
                        }
                        .help(L.t("Install a .dockwidget package (HTML/JavaScript widget)"))
                        Button {
                            openWidgetsFolder()
                        } label: {
                            Label(L.t("Open widgets folder"), systemImage: "folder")
                        }
                    }
                    RowNote(L.t("Press + or drag a card onto the dock. To change a widget's layout later, edit the dock."))
                }
                if groups.isEmpty {
                    Text(L.t("No widgets match “{0}”.", query)).foregroundStyle(.secondary)
                }
                ForEach(groups, id: \.0) { category, defs in
                    VStack(alignment: .leading, spacing: 10) {
                        Text(L.t(category)).font(.headline)
                        LazyVGrid(columns: [GridItem(.adaptive(minimum: 280), spacing: 12, alignment: .top)],
                                  alignment: .leading, spacing: 12) {
                            ForEach(defs) { def in card(def) }
                        }
                    }
                }
            }
            .padding(20)
        }
    }

    private func card(_ def: WidgetDefinition) -> some View {
        let info = WidgetCatalog.info(def.id)
        let variant = chosen[def.id] ?? def.defaultVariant
        let count = store.config.items.filter { $0.widget == def.id }.count
        return HStack(alignment: .top, spacing: 12) {
            Image(systemName: info.symbol)
                .font(.system(size: 20))
                .foregroundStyle(Color.accentColor)
                .frame(width: 40, height: 40)
                .background(RoundedRectangle(cornerRadius: 10).fill(Color.accentColor.opacity(0.12)))
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text(def.displayName).font(.body.weight(.semibold))
                    if count > 0 {
                        Text(L.t("On the dock ×{0}", count)).font(.caption2).foregroundStyle(.secondary)
                    }
                }
                Text(info.summary).font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                HStack(spacing: 8) {
                    if def.variants.count > 1 {
                        Picker(L.t("Layout"), selection: Binding(get: { variant }, set: { chosen[def.id] = $0 })) {
                            ForEach(def.variants) { Text($0.displayName).tag($0.id) }
                        }
                        .labelsHidden()
                        .fixedSize()
                    }
                    Spacer()
                    if def.id.hasPrefix("web.") {
                        Button(role: .destructive) {
                            let manifestId = String(def.id.dropFirst(4))
                            try? WebWidgetCatalog.shared.uninstall(id: manifestId)
                            refreshTrigger += 1
                        } label: {
                            Image(systemName: "trash")
                        }
                        .help(L.t("Remove"))
                    }
                    if justAdded == def.id {
                        Label(L.t("Added"), systemImage: "checkmark").font(.caption).foregroundStyle(.green)
                    }
                    Button {
                        store.addWidget(def.id, variant: variant)
                        justAdded = def.id
                        Task { @MainActor in
                            try? await Task.sleep(for: .seconds(1.5))
                            if justAdded == def.id { justAdded = nil }
                        }
                    } label: { Image(systemName: "plus") }
                    .help(L.t("Add {0} to the dock", def.displayName))
                }
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 10).fill(Color(nsColor: .controlBackgroundColor)))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(Color(nsColor: .separatorColor)))
        // Galeriden dock'a surukleme (Windows: NewWidgetFormat).
        .draggable(DockDrag.widget(def.id, variant: variant)) {
            Label(def.displayName, systemImage: info.symbol)
                .padding(8)
                .background(RoundedRectangle(cornerRadius: 8).fill(.regularMaterial))
        }
    }

    private func pickAndInstallWidget() {
        let panel = NSOpenPanel()
        panel.title = L.t("Install a widget")
        panel.allowedFileTypes = ["dockwidget", "zip"]
        panel.allowsMultipleSelection = false
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        if panel.runModal() == .OK, let url = panel.url {
            inspectAndPrompt(url: url)
        }
    }

    private func inspectAndPrompt(url: URL) {
        do {
            let (manifest, tempDir) = try WebWidgetCatalog.inspect(packageURL: url)
            defer { try? FileManager.default.removeItem(at: tempDir) }

            let alert = NSAlert()
            alert.messageText = String(format: L.t("Install “{0}”?"), manifest.name)

            var info = "\(manifest.description)\n\n"
            info += String(format: L.t("Version {0}"), manifest.version)
            if let author = manifest.author {
                info += " · \(author)"
            }

            var permissions: [String] = []
            if !manifest.permissions.network.isEmpty {
                permissions.append(String(format: L.t("Internet access to: {0}"), manifest.permissions.network.joined(separator: ", ")))
            }
            for key in manifest.permissions.networkFromSettings {
                let label = manifest.settings.first(where: { $0.key == key })?.label ?? key
                permissions.append(String(format: L.t("Internet access to the address you enter in “{0}” (also plain http, for servers on your network)"), label))
            }
            if manifest.permissions.notifications {
                permissions.append(L.t("Show notifications"))
            }
            if permissions.isEmpty {
                permissions.append(L.t("No internet access, no notifications"))
            }

            info += "\n\n" + L.t("This widget can use:") + "\n• " + permissions.joined(separator: "\n• ")

            alert.informativeText = info
            alert.addButton(withTitle: L.t("Install"))
            alert.addButton(withTitle: L.t("Cancel"))

            if alert.runModal() == .alertFirstButtonReturn {
                try WebWidgetCatalog.shared.install(manifest: manifest)
                refreshTrigger += 1
            }
        } catch {
            let errAlert = NSAlert()
            errAlert.messageText = L.t("Can't install this widget")
            errAlert.informativeText = error.localizedDescription
            errAlert.runModal()
        }
    }

    private func openWidgetsFolder() {
        let dir = AppPaths.widgetsDir
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        NSWorkspace.shared.open(dir)
    }
}
