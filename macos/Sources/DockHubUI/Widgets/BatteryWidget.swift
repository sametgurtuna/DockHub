import SwiftUI
import Combine
import DockHubCore
import DockHubPlatform

@MainActor
public final class BatteryMonitor: ObservableObject {
    public static let shared = BatteryMonitor()
    @Published public private(set) var state = BatteryState.none
    private var timer: Timer?
    private init() {}

    public func start() {
        guard timer == nil else { return }
        state = BatteryService.read()
        // Pil yavas degisir; 30 saniye yeterli, bosuna is yapilmaz.
        timer = Timer.scheduledTimer(withTimeInterval: 30, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.state = BatteryService.read() }
        }
    }
}

/// Pil widget'i. Varyantlar: yuzde, ikon.
struct BatteryWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var monitor = BatteryMonitor.shared

    private var showTime: Bool { item.boolSetting("showTime", default: true) }

    var body: some View {
        Group {
            if monitor.state.hasBattery { content } else { yok }
        }
        .onAppear { monitor.start() }
    }

    private var yok: some View {
        HStack(spacing: 4) {
            Image(systemName: "powerplug.fill")
            Text("Pil yok").font(.system(size: style.height * 0.18))
        }
        .foregroundStyle(.secondary)
    }

    @ViewBuilder private var content: some View {
        let s = monitor.state
        if item.effectiveVariant == "icon" {
            ZStack {
                Image(systemName: symbol(s))
                    .font(.system(size: style.iconSize * 0.62))
                    .foregroundStyle(renk(s))
            }
        } else {
            HStack(spacing: 5) {
                Image(systemName: symbol(s))
                    .font(.system(size: style.iconSize * 0.55))
                    .foregroundStyle(renk(s))
                VStack(alignment: .leading, spacing: 0) {
                    Text("\(s.percent)%")
                        .font(.system(size: style.height * 0.26, weight: .semibold))
                        .monospacedDigit()
                    if showTime, let m = s.minutesRemaining {
                        Text(sure(m))
                            .font(.system(size: style.height * 0.17))
                            .foregroundStyle(.secondary)
                    }
                }
            }
        }
    }

    private func symbol(_ s: BatteryState) -> String {
        if s.isCharging { return "battery.100percent.bolt" }
        switch s.percent {
        case ..<13:  return "battery.0percent"
        case ..<38:  return "battery.25percent"
        case ..<63:  return "battery.50percent"
        case ..<88:  return "battery.75percent"
        default:     return "battery.100percent"
        }
    }

    private func renk(_ s: BatteryState) -> Color {
        if s.isCharging { return .green }
        return s.percent <= 20 ? .red : .primary
    }

    private func sure(_ m: Int) -> String {
        m >= 60 ? "\(m / 60)sa \(m % 60)dk" : "\(m)dk"
    }
}
