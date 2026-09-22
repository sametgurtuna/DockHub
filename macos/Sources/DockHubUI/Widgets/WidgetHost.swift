import SwiftUI
import DockHubCore

/// Widget kimligini gorunume baglar. Kayit defteri DockHubCore'da, gorunum
/// ureticisi burada; boylece Core katmani AppKit/SwiftUI bilmemeye devam eder.
struct WidgetHost: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel

    var body: some View {
        switch item.widget {
        case "clock":  ClockWidget(item: item, style: style)
        case "system":  SystemWidget(item: item, style: style)
        case "battery": BatteryWidget(item: item, style: style)
        case "weather": WeatherWidget(item: item, style: style)
        case "world-clock":   WorldClockWidget(item: item, style: style)
        case "stopwatch":     StopwatchWidget(item: item, style: style)
        case "focus":         FocusWidget(item: item, style: style)
        case "countdown":     CountdownWidget(item: item, style: style)
        case "alarm":         AlarmWidget(item: item, style: style)
        case "time-progress": TimeProgressWidget(item: item, style: style)
        case "hydration":     HydrationWidget(item: item, style: style, model: model)
        case "reminders":     RemindersWidget(item: item, style: style, model: model)
        case "notes":         StickyNoteWidget(item: item, style: style, model: model)
        case "network":        NetworkWidget(item: item, style: style)
        case "status":         StatusWidget(item: item, style: style)
        case "audio":          AudioWidget(item: item, style: style)
        case "battery-devices": DeviceBatteryWidget(item: item, style: style)
        case "recycle-bin":    TrashWidget(item: item, style: style)
        case "media":          NowPlayingWidget(item: item, style: style)
        case "ai-usage":       AIUsageWidget(item: item, style: style)
        case "shortcut":       ShortcutWidget(item: item, style: style)
        case "airdrop":        AirDropWidget(item: item, style: style)
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
