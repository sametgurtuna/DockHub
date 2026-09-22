import SwiftUI
import DockHubCore

/// Windows karsiligi: Settings/SettingsWindow.Gallery.cs ("Widget gallery").
/// Windows kartlarda canli onizleme gosteriyor; burada simge ve aciklama var
/// (fark docs/AYARLAR.md'de).
struct GalleryPage: View {
    @ObservedObject var store: SettingsStore
    @State private var chosen: [String: String] = [:]
    @State private var justAdded: String?

    private var groups: [(String, [WidgetDefinition])] {
        let byCategory = Dictionary(grouping: WidgetRegistry.all, by: \.category)
        let order = WidgetCatalog.categoryOrder + byCategory.keys.filter { !WidgetCatalog.categoryOrder.contains($0) }.sorted()
        return order.compactMap { cat in byCategory[cat].map { (cat, $0) } }
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 22) {
                ForEach(groups, id: \.0) { category, defs in
                    VStack(alignment: .leading, spacing: 10) {
                        Text(category).font(.headline)
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
                    Text(def.name).font(.body.weight(.semibold))
                    if count > 0 {
                        Text("On dock: \(count)").font(.caption2).foregroundStyle(.secondary)
                    }
                }
                Text(info.summary).font(.caption).foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                HStack(spacing: 8) {
                    if def.variants.count > 1 {
                        Picker("Layout", selection: Binding(get: { variant }, set: { chosen[def.id] = $0 })) {
                            ForEach(def.variants) { Text($0.name).tag($0.id) }
                        }
                        .labelsHidden()
                        .fixedSize()
                    }
                    Spacer()
                    if justAdded == def.id {
                        Label("Added", systemImage: "checkmark").font(.caption).foregroundStyle(.green)
                    }
                    Button {
                        store.addWidget(def.id, variant: variant)
                        justAdded = def.id
                        Task { @MainActor in
                            try? await Task.sleep(for: .seconds(1.5))
                            if justAdded == def.id { justAdded = nil }
                        }
                    } label: { Image(systemName: "plus") }
                    .help("Add \(def.name) to the dock")
                }
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 10).fill(Color(nsColor: .controlBackgroundColor)))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(Color(nsColor: .separatorColor)))
    }
}
