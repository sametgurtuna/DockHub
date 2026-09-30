import SwiftUI
import Combine
import CoreAudio
import DockHubCore
import DockHubPlatform

// MARK: - Ag izleyici
@MainActor
public final class NetworkStore: ObservableObject {
    public static let shared = NetworkStore()
    @Published public private(set) var sample = NetworkSample(downBytesPerSec: 0, upBytesPerSec: 0,
                                                       totalDown: 0, totalUp: 0)
    /// Grafik varyanti icin son degerler.
    @Published public private(set) var gecmis: [Double] = []
    private let metrics = NetworkMetrics()
    private var timer: Timer?
    private init() {}

    public func start(interval: TimeInterval) {
        guard timer == nil else { return }
        sample = metrics.sample()                       // ilk olcum: temel
        timer = Timer.scheduledTimer(withTimeInterval: max(1, interval), repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.sample = self.metrics.sample()
                self.gecmis.append(self.sample.downBytesPerSec)
                if self.gecmis.count > 24 { self.gecmis.removeFirst() }
            }
        }
    }
}

/// Ag widget'i. Windows karsiligi: Widgets/System/NetworkWidget.
struct NetworkWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var store = NetworkStore.shared

    var body: some View {
        HStack(spacing: 5) {
            VStack(alignment: .leading, spacing: 0) {
                satir("arrow.down", store.sample.downBytesPerSec, .blue)
                satir("arrow.up", store.sample.upBytesPerSec, .orange)
            }
            if item.effectiveVariant == "chart" { grafik }
        }
        .onAppear { store.start(interval: item.numberSetting("interval", default: 2)) }
    }

    private func satir(_ ikon: String, _ deger: Double, _ renk: Color) -> some View {
        HStack(spacing: 3) {
            Image(systemName: ikon)
                .font(.system(size: style.height * 0.14, weight: .bold))
                .foregroundStyle(renk)
            Text(NetworkMetrics.format(deger))
                .font(.system(size: style.height * 0.17, weight: .medium))
                .monospacedDigit()
        }
    }

    /// Basit sparkline: son olcumler en yuksek degere gore olceklenir.
    private var grafik: some View {
        let v = store.gecmis
        let enYuksek = max(v.max() ?? 1, 1)
        return Canvas { ctx, size in
            guard v.count > 1 else { return }
            var p = Path()
            for (i, d) in v.enumerated() {
                let x = size.width * Double(i) / Double(max(v.count - 1, 1))
                let y = size.height * (1 - min(d / enYuksek, 1))
                i == 0 ? p.move(to: CGPoint(x: x, y: y)) : p.addLine(to: CGPoint(x: x, y: y))
            }
            ctx.stroke(p, with: .color(.blue.opacity(0.9)),
                       style: StrokeStyle(lineWidth: 1.5, lineJoin: .round))
        }
        .frame(width: style.itemHeight * 1.3, height: style.itemHeight * 0.6)
    }
}

// MARK: - Durum (pil, disk, bellek, islemci)
/// Windows karsiligi: Widgets/System/StatusWidget. Varyantlar: halkalar, yuzde.
struct StatusWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var sys = SystemMonitor.shared
    @ObservedObject private var bat = BatteryMonitor.shared
    @State private var disk: Double = 0

    private struct Olcu: Identifiable { let id = UUID(); let ad: String; let deger: Double; let renk: Color }

    private var olculer: [Olcu] {
        var l: [Olcu] = []
        if bat.state.hasBattery {
            l.append(Olcu(ad: "BAT", deger: Double(bat.state.percent),
                          renk: bat.state.percent <= 20 ? .red : .green))
        }
        l.append(Olcu(ad: "DSK", deger: disk, renk: .purple))
        l.append(Olcu(ad: "RAM", deger: sys.sample.memoryPercent, renk: .blue))
        l.append(Olcu(ad: "CPU", deger: sys.sample.cpuPercent, renk: .orange))
        return l
    }

    var body: some View {
        HStack(spacing: style.gap) {
            ForEach(olculer) { o in
                if item.effectiveVariant == "percent" { yuzde(o) } else { halka(o) }
            }
        }
        .onAppear {
            sys.start(interval: item.numberSetting("interval", default: 2))
            bat.start()
            if let d = DiskInfo.usage(), d.total > 0 {
                disk = Double(d.used) / Double(d.total) * 100
            }
        }
    }

    private func halka(_ o: Olcu) -> some View {
        let d = style.itemHeight * 0.64
        return ZStack {
            Circle().stroke(Color.primary.opacity(0.18), lineWidth: 2.5)
            Circle().trim(from: 0, to: min(max(o.deger / 100, 0), 1))
                .stroke(o.renk, style: StrokeStyle(lineWidth: 2.5, lineCap: .round))
                .rotationEffect(.degrees(-90))
            Text(o.ad).font(.system(size: d * 0.24, weight: .bold)).foregroundStyle(.secondary)
        }
        .frame(width: d, height: d)
        .help("\(o.ad): \(Int(o.deger.rounded()))%")
    }

    private func yuzde(_ o: Olcu) -> some View {
        VStack(spacing: 0) {
            Text("\(Int(o.deger.rounded()))")
                .font(.system(size: style.height * 0.2, weight: .semibold)).monospacedDigit()
                .foregroundStyle(o.renk)
            Text(o.ad).font(.system(size: style.height * 0.13)).foregroundStyle(.secondary)
        }
    }
}

// MARK: - Ses aygiti
@MainActor
public final class AudioStore: ObservableObject {
    public static let shared = AudioStore()
    @Published public private(set) var aygitlar: [AudioDevice] = []
    @Published public private(set) var seviye: Float = 0
    @Published public private(set) var sessiz = false
    private var timer: Timer?
    private init() {}

    public func start() {
        guard timer == nil else { return }
        yenile()
        timer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.yenile() }
        }
    }

    func yenile() {
        aygitlar = AudioDevices.outputDevices()
        if let id = AudioDevices.defaultOutputID() {
            seviye = AudioDevices.volume(of: id) ?? 0
            sessiz = AudioDevices.isMuted(id) ?? false
        }
    }

    func sec(_ d: AudioDevice) { AudioDevices.setDefaultOutput(d.id); yenile() }
    func sessizDegistir() {
        guard let id = AudioDevices.defaultOutputID() else { return }
        AudioDevices.setMuted(!sessiz, of: id); yenile()
    }
    func seviyeAyarla(_ v: Float) {
        guard let id = AudioDevices.defaultOutputID() else { return }
        AudioDevices.setVolume(v, of: id); yenile()
    }
}

/// Ses aygiti widget'i. Windows karsiligi: Widgets/Audio.
/// Varyantlar: kompakt, cubuklu.
struct AudioWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var store = AudioStore.shared

    private var aktif: AudioDevice? { store.aygitlar.first(where: \.isDefault) }

    var body: some View {
        HStack(spacing: 5) {
            Image(systemName: ikon)
                .font(.system(size: style.iconSize * 0.55))
                .foregroundStyle(store.sessiz ? .red : Color(nsColor: .controlAccentColor))
                .onTapGesture { store.sessizDegistir() }
            if item.effectiveVariant == "slider" {
                Slider(value: Binding(get: { Double(store.seviye) },
                                      set: { store.seviyeAyarla(Float($0)) }), in: 0...1)
                    .controlSize(.mini)
                    .frame(width: style.itemHeight * 1.6)
            } else {
                VStack(alignment: .leading, spacing: 0) {
                    Text("\(Int((store.seviye * 100).rounded()))%")
                        .font(.system(size: style.height * 0.22, weight: .semibold)).monospacedDigit()
                    Text(aktif?.name ?? "No device")
                        .font(.system(size: style.height * 0.14))
                        .foregroundStyle(.secondary).lineLimit(1)
                        .frame(maxWidth: style.itemHeight * 1.6, alignment: .leading)
                }
            }
        }
        .onAppear { store.start() }
        .contextMenu {
            ForEach(store.aygitlar) { d in
                Button { store.sec(d) } label: {
                    Text(d.isDefault ? "✓ \(d.name)" : d.name)
                }
            }
        }
        .help(aktif.map { "Output: \($0.name)" } ?? "No output device")
    }

    private var ikon: String {
        if store.sessiz { return "speaker.slash.fill" }
        switch store.seviye {
        case ..<0.01: return "speaker.fill"
        case ..<0.34: return "speaker.wave.1.fill"
        case ..<0.67: return "speaker.wave.2.fill"
        default:      return "speaker.wave.3.fill"
        }
    }
}

// MARK: - Aygit pilleri
@MainActor
public final class PeripheralStore: ObservableObject {
    public static let shared = PeripheralStore()
    @Published public private(set) var cihazlar: [PeripheralBattery] = []
    private var timer: Timer?
    private init() {}
    public func start() {
        guard timer == nil else { return }
        cihazlar = DeviceBattery.read()
        timer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.cihazlar = DeviceBattery.read() }
        }
    }
}

/// Aygit pilleri widget'i. Windows karsiligi: Widgets/BatteryDevices.
/// Bluetooth cevre birimleri okunur; saticiya ozel dongle'lar bu surumde kapsam disi.
struct DeviceBatteryWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var store = PeripheralStore.shared

    var body: some View {
        Group {
            if store.cihazlar.isEmpty {
                HStack(spacing: 4) {
                    Image(systemName: "keyboard").font(.system(size: style.iconSize * 0.5))
                    Text("No battery devices").font(.system(size: style.height * 0.16))
                }.foregroundStyle(.secondary)
            } else {
                let g = item.effectiveVariant == "single"
                    ? Array(store.cihazlar.prefix(1)) : store.cihazlar
                HStack(spacing: style.gap) { ForEach(g) { cihaz($0) } }
            }
        }
        .onAppear { store.start() }
    }

    private func cihaz(_ c: PeripheralBattery) -> some View {
        HStack(spacing: 3) {
            Image(systemName: c.percent <= 20 ? "battery.25percent" : "battery.100percent")
                .font(.system(size: style.height * 0.2))
                .foregroundStyle(c.percent <= 20 ? .red : .primary)
            VStack(alignment: .leading, spacing: 0) {
                Text("\(c.percent)%")
                    .font(.system(size: style.height * 0.2, weight: .semibold)).monospacedDigit()
                Text(c.name).font(.system(size: style.height * 0.13))
                    .foregroundStyle(.secondary).lineLimit(1)
                    .frame(maxWidth: style.itemHeight * 1.5, alignment: .leading)
            }
        }
    }
}
