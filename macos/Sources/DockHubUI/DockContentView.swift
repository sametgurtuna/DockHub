import SwiftUI
import DockHubCore

/// Dock'un icerigi: ogeler kenara gore yatay veya dikey dizilir.
public struct DockContentView: View {
    @ObservedObject var model: DockModel
    let style: DockStyle

    public init(model: DockModel, style: DockStyle) {
        self.model = model
        self.style = style
    }

    public var body: some View {
        Group {
            if model.config.edge.isVertical {
                VStack(spacing: style.gap) { content }
            } else {
                HStack(spacing: style.gap) { content }
            }
        }
        .padding(style.padding)
        .frame(maxWidth: .infinity, maxHeight: .infinity,
               alignment: alignment)
        .contentShape(Rectangle())
        // Galeriden birakilan widget ya da tasinan oge bos alana: sona.
        .dropDestination(for: String.self) { strings, _ in model.handleDrop(strings, before: nil) }
    }

    private var alignment: Alignment {
        switch (model.config.edge.isVertical, model.config.alignment) {
        case (false, .center): .center
        case (false, .start):  .leading
        case (true, .center):  .center
        case (true, .start):   .top
        }
    }

    @ViewBuilder private var content: some View {
        let seamless = model.config.widgetStyle == .seamless
        let vertical = model.config.edge.isVertical
        ForEach(Array(model.items.enumerated()), id: \.element.id) { index, item in
            // Kutusuz stilde art arda iki widget arasinda ince cizgi (Windows: WidgetStyle.Seamless).
            if seamless, index > 0, item.kind == .widget, model.items[index - 1].kind == .widget {
                Rectangle()
                    .fill(Color(nsColor: DockStyle.separatorColor))
                    .frame(width: vertical ? style.itemHeight * 0.5 : 1,
                           height: vertical ? 1 : style.itemHeight * 0.5)
            }
            DockItemView(item: item,
                         style: style,
                         model: model,
                         isRunning: item.path.map { model.runningPaths.contains($0) } ?? false,
                         hoverEffect: model.config.hoverEffect,
                         seamless: seamless) {
                model.activate(item)
            }
            .dropDestination(for: String.self) { strings, _ in model.handleDrop(strings, before: item.id) }
        }
        if model.isEditing {
            Button { model.onOpenGallery?() } label: {
                Image(systemName: "plus")
                    .font(.system(size: style.iconSize * 0.4, weight: .semibold))
                    .frame(width: style.itemHeight, height: style.itemHeight)
                    .overlay(RoundedRectangle(cornerRadius: style.itemRadius, style: .continuous)
                        .strokeBorder(style: StrokeStyle(lineWidth: 1, dash: [3, 3])))
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help(L.t("Add a widget"))
            Button(L.t("Done")) { model.endEditing() }
                .buttonStyle(.borderedProminent)
                .controlSize(.small)
        }
    }
}
