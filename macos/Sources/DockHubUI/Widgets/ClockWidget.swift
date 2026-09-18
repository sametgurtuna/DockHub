import SwiftUI
import DockHubCore

/// Saat widget'i. Windows karsiligi: Widgets/Clock/ClockWidget
/// Varyantlar: dijital, analog. Ayarlar ornek basina DockItem.settings icinde.
struct ClockWidget: View {
    let item: DockItem
    let style: DockStyle

    private var showSeconds: Bool { item.boolSetting("showSeconds", default: false) }
    private var showDate: Bool { item.boolSetting("showDate", default: true) }
    private var use24Hour: Bool { item.boolSetting("use24Hour", default: true) }

    var body: some View {
        TimelineView(.periodic(from: .now, by: showSeconds ? 1 : 30)) { ctx in
            if item.effectiveVariant == "analog" {
                analog(ctx.date)
            } else {
                digital(ctx.date)
            }
        }
    }

    // ---- Dijital
    private func digital(_ date: Date) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(timeText(date))
                .font(.system(size: style.height * 0.34, weight: .semibold))
                .monospacedDigit()
            if showDate {
                Text(dateText(date))
                    .font(.system(size: style.height * 0.19, weight: .regular))
                    .foregroundStyle(.secondary)
            }
        }
    }

    // ---- Analog
    private func analog(_ date: Date) -> some View {
        let d = style.itemHeight * 0.86
        return Canvas { ctx, size in
            let c = CGPoint(x: size.width / 2, y: size.height / 2)
            let r = min(size.width, size.height) / 2 - 1

            ctx.stroke(Path(ellipseIn: CGRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2)),
                       with: .color(.primary.opacity(0.35)), lineWidth: 1)

            let cal = Calendar.current
            let h = Double(cal.component(.hour, from: date) % 12)
            let m = Double(cal.component(.minute, from: date))
            let s = Double(cal.component(.second, from: date))

            func hand(angle: Double, length: Double, width: Double, opacity: Double) {
                let a = (angle - 90) * .pi / 180
                var p = Path()
                p.move(to: c)
                p.addLine(to: CGPoint(x: c.x + cos(a) * r * length, y: c.y + sin(a) * r * length))
                ctx.stroke(p, with: .color(.primary.opacity(opacity)),
                           style: StrokeStyle(lineWidth: width, lineCap: .round))
            }
            hand(angle: (h + m / 60) * 30, length: 0.52, width: 2.0, opacity: 0.9)
            hand(angle: (m + s / 60) * 6, length: 0.76, width: 1.5, opacity: 0.9)
            if showSeconds { hand(angle: s * 6, length: 0.82, width: 0.8, opacity: 0.55) }
        }
        .frame(width: d, height: d)
    }

    private func timeText(_ d: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale.current
        f.dateFormat = (use24Hour ? "HH:mm" : "h:mm") + (showSeconds ? ":ss" : "") + (use24Hour ? "" : " a")
        return f.string(from: d)
    }

    private func dateText(_ d: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale.current
        f.setLocalizedDateFormatFromTemplate("EEE d MMM")
        return f.string(from: d)
    }
}
