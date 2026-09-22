import SwiftUI
import AppKit
import DockHubCore
import DockHubPlatform

/// Dock'taki tek bir oge: uygulama ikonu, widget kutucugu veya ayirici.
struct DockItemView: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel
    let isRunning: Bool
    let hoverEffect: Bool
    let onTap: () -> Void

    @State private var hovering = false

    var body: some View {
        switch item.kind {
        case .app:      appIcon
        case .widget:   widgetPill
        case .separator: separator
        case .group:    groupTile
        }
    }

    // ---- Klasor (grup): Windows'taki kapali hal (Dock/GroupItemView.cs) -
    // vurgu kenarli kutucuk, icinde ilk dort uygulamanin 2x2 ikonu, bossa
    // klasor simgesi. Acma, yeniden adlandirma ve renk secimi wf-dock-groups
    // gorevinde; burada yalniz icerigin dock'ta kaybolmamasi saglaniyor.
    private var groupTile: some View {
        let accent = DockStyle.groupAccent(item.groupAccent)
        let icons = (item.children ?? []).compactMap { child -> NSImage? in
            guard child.kind == .app, let path = child.path else { return nil }
            return AppCatalog.icon(forAppAt: path)
        }.prefix(4)
        let cell = style.iconSize * 0.36
        return ZStack {
            RoundedRectangle(cornerRadius: style.iconSize * 0.24, style: .continuous)
                .fill(Color(nsColor: DockStyle.itemBackground))
                .overlay(
                    RoundedRectangle(cornerRadius: style.iconSize * 0.24, style: .continuous)
                        .strokeBorder(Color(nsColor: accent).opacity(0.7), lineWidth: 1.2)
                )
            if icons.isEmpty {
                Image(systemName: "folder.fill")
                    .font(.system(size: style.iconSize * 0.42))
                    .foregroundStyle(Color(nsColor: accent))
            } else {
                LazyVGrid(columns: [GridItem(.fixed(cell), spacing: 2), GridItem(.fixed(cell), spacing: 2)],
                          spacing: 2) {
                    ForEach(Array(icons.enumerated()), id: \.offset) { _, icon in
                        Image(nsImage: icon).resizable().interpolation(.high)
                            .frame(width: cell, height: cell)
                    }
                }
            }
        }
        .frame(width: style.iconSize, height: style.iconSize)
        .frame(height: style.itemHeight)
        .help(item.groupName ?? "")
    }

    // ---- Uygulama: sade ikon, altinda calisiyor noktasi (macOS Dock gibi)
    private var appIcon: some View {
        VStack(spacing: 2) {
            Group {
                if let path = item.path, let icon = AppCatalog.icon(forAppAt: path) {
                    Image(nsImage: icon).resizable().interpolation(.high)
                } else {
                    Image(systemName: "questionmark.app.dashed").resizable()
                }
            }
            .frame(width: style.iconSize, height: style.iconSize)
            .scaleEffect(hoverEffect && hovering ? 1.12 : 1.0)
            .animation(.spring(response: 0.25, dampingFraction: 0.7), value: hovering)

            Circle()
                .fill(Color.primary.opacity(isRunning ? 0.55 : 0))
                .frame(width: style.runningDot, height: style.runningDot)
        }
        .frame(height: style.itemHeight)
        .contentShape(Rectangle())
        .onHover { hovering = $0 }
        .onTapGesture(perform: onTap)
        .help(item.name ?? item.path ?? "")
    }

    // ---- Widget: neredeyse gorunmez arka planli yuvarlak kutucuk
    private var widgetPill: some View {
        WidgetHost(item: item, style: style, model: model)
        .padding(.horizontal, style.gap * 1.6)
        .frame(height: style.itemHeight)
        .background(
            RoundedRectangle(cornerRadius: style.itemRadius, style: .continuous)
                .fill(Color(nsColor: DockStyle.itemBackground))
        )
        .onHover { hovering = $0 }
    }

    private var separator: some View {
        Rectangle()
            .fill(Color(nsColor: DockStyle.separatorColor))
            .frame(width: 1, height: style.itemHeight * 0.55)
            .padding(.horizontal, style.gap * 0.5)
    }
}
