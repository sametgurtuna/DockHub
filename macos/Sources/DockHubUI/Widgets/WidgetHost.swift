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
        case "calendar":       CalendarWidget(item: item, style: style, model: model)
        case "clipboard":      ClipboardWidget(item: item, style: style)
        case "stack":          StackWidget(item: item, style: style, model: model)
        case "currency":       CurrencyWidget(item: item, style: style, model: model)
        case "stocks":         StocksWidget(item: item, style: style, model: model)
        case "todo":           TodoWidget(item: item, style: style, model: model)
        case "screenshot":     ScreenshotWidget(item: item, style: style, model: model)
        case "ping":           PingWidget(item: item, style: style, model: model)
        default:
            if let w = item.widget, w.hasPrefix("web.") {
                WebWidgetView(item: item, style: style, model: model)
            } else {
                unknown
            }
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
