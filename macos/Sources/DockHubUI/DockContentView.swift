import SwiftUI
import DockHubCore

/// Iskelet icerik: gercek oge/widget yerlesimi sonraki gorevlerin isi.
/// Simdilik yapilandirmadan okunan degerleri gosterir ki konum/boyut/tema
/// gercekten uygulandigi gorulebilsin.
public struct DockContentView: View {
    let config: AppConfig
    let screenName: String

    public init(config: AppConfig, screenName: String) {
        self.config = config
        self.screenName = screenName
    }

    public var body: some View {
        let vertical = config.edge.isVertical
        Group {
            if vertical {
                VStack(spacing: 8) { badges }
            } else {
                HStack(spacing: 12) { badges }
            }
        }
        .padding(vertical ? .vertical : .horizontal, 12)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .foregroundStyle(.primary)
    }

    @ViewBuilder private var badges: some View {
        Image(systemName: "square.grid.2x2.fill")
            .font(.system(size: 18, weight: .semibold))
            .foregroundStyle(Color(nsColor: .controlAccentColor))
        if !config.edge.isVertical {
            Text("DockHub").font(.headline)
            Divider().frame(height: 18)
            Text("\(config.edge.rawValue) · \(config.size.rawValue) · \(config.layout.rawValue)")
                .font(.caption).monospaced()
            Text(screenName).font(.caption2).foregroundStyle(.secondary)
            Spacer(minLength: 0)
        }
    }
}
