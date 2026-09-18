import SwiftUI
import DockHubCore

/// Sistem widget'i: CPU ve bellek. Windows karsiligi: Widgets/System/SystemWidget
/// Varyantlar: sayilar, halkalar. Olcum SystemMonitor.shared uzerinden gelir.
struct SystemWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var monitor = SystemMonitor.shared

    private var interval: Double { item.numberSetting("interval", default: 2) }

    var body: some View {
        Group {
            if item.effectiveVariant == "rings" { rings } else { numbers }
        }
        .onAppear { monitor.start(interval: interval) }
    }

    private var numbers: some View {
        VStack(alignment: .leading, spacing: 1) {
            row("CPU", monitor.sample.cpuPercent)
            row("RAM", monitor.sample.memoryPercent)
        }
    }

    private func row(_ label: String, _ value: Double) -> some View {
        HStack(spacing: 4) {
            Text(label)
                .font(.system(size: style.height * 0.17, weight: .semibold))
                .foregroundStyle(.secondary)
            Text("\(Int(value.rounded()))%")
                .font(.system(size: style.height * 0.2, weight: .medium))
                .monospacedDigit()
        }
    }

    private var rings: some View {
        HStack(spacing: style.gap) {
            ring("CPU", monitor.sample.cpuPercent)
            ring("RAM", monitor.sample.memoryPercent)
        }
    }

    private func ring(_ label: String, _ value: Double) -> some View {
        let d = style.itemHeight * 0.72
        return ZStack {
            Circle().stroke(Color.primary.opacity(0.18), lineWidth: 3)
            Circle()
                .trim(from: 0, to: min(max(value / 100, 0), 1))
                .stroke(Color(nsColor: .controlAccentColor),
                        style: StrokeStyle(lineWidth: 3, lineCap: .round))
                .rotationEffect(.degrees(-90))
            Text(label)
                .font(.system(size: d * 0.26, weight: .bold))
                .foregroundStyle(.secondary)
        }
        .frame(width: d, height: d)
        .animation(.easeOut(duration: 0.4), value: value)
    }
}
