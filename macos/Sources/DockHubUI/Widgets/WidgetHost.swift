import SwiftUI
import DockHubCore

/// Widget kimligini gorunume baglar. Kayit defteri DockHubCore'da, gorunum
/// ureticisi burada; boylece Core katmani AppKit/SwiftUI bilmemeye devam eder.
struct WidgetHost: View {
    let item: DockItem
    let style: DockStyle

    var body: some View {
        switch item.widget {
        case "clock":  ClockWidget(item: item, style: style)
        case "system":  SystemWidget(item: item, style: style)
        case "battery": BatteryWidget(item: item, style: style)
        case "weather": WeatherWidget(item: item, style: style)
        default:       unknown
        }
    }

    private var unknown: some View {
        HStack(spacing: 4) {
            Image(systemName: "questionmark.square.dashed")
            Text(item.widget ?? "?")
                .font(.system(size: style.height * 0.18))
        }
        .foregroundStyle(.secondary)
    }
}
