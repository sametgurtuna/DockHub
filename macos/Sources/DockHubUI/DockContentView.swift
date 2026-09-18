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
        ForEach(model.items) { item in
            DockItemView(item: item,
                         style: style,
                         isRunning: item.path.map { model.runningPaths.contains($0) } ?? false,
                         hoverEffect: model.config.hoverEffect) {
                model.activate(item)
            }
        }
    }
}
