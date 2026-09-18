import SwiftUI
import AppKit
import DockHubCore
import DockHubPlatform

/// Dock'taki tek bir oge: uygulama ikonu, widget kutucugu veya ayirici.
struct DockItemView: View {
    let item: DockItem
    let style: DockStyle
    let isRunning: Bool
    let hoverEffect: Bool
    let onTap: () -> Void

    @State private var hovering = false

    var body: some View {
        switch item.kind {
        case .app:      appIcon
        case .widget:   widgetPill
        case .separator: separator
        }
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
        HStack(spacing: 6) {
            Image(systemName: "square.dashed")
                .font(.system(size: style.iconSize * 0.5, weight: .medium))
            Text(item.widget ?? "widget")
                .font(.system(size: max(9, style.height * 0.23), weight: .medium))
        }
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
