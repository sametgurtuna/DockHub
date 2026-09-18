import SwiftUI
import Combine
import UniformTypeIdentifiers
import DockHubCore
import DockHubPlatform

// MARK: - Cop kutusu
@MainActor
public final class TrashStore: ObservableObject {
    public static let shared = TrashStore()
    @Published public private(set) var state = TrashState(itemCount: 0, totalBytes: 0, accessible: true)
    private var timer: Timer?
    private init() {}
    public func start() {
        guard timer == nil else { return }
        state = TrashService.read()
        timer = Timer.scheduledTimer(withTimeInterval: 10, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.state = TrashService.read() }
        }
    }
    public func yenile() { state = TrashService.read() }
}

/// Cop kutusu widget'i. Windows karsiligi: Widgets/RecycleBin.
/// Varyantlar: yalnizca ikon, detayli.
struct TrashWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var store = TrashStore.shared
    @State private var uzerinde = false

    var body: some View {
        HStack(spacing: 5) {
            Image(systemName: !store.state.accessible ? "trash.slash"
                              : (store.state.isEmpty ? "trash" : "trash.fill"))
                .font(.system(size: style.iconSize * 0.6))
                .foregroundStyle(uzerinde ? Color(nsColor: .controlAccentColor) : .primary)
                .scaleEffect(uzerinde ? 1.15 : 1)
            if item.effectiveVariant == "details" {
                VStack(alignment: .leading, spacing: 0) {
                    Text(!store.state.accessible ? "Erişim yok"
                         : (store.state.isEmpty ? "Boş" : "\(store.state.itemCount) öğe"))
                        .font(.system(size: style.height * 0.2, weight: .semibold))
                        .foregroundStyle(store.state.accessible ? .primary : .secondary)
                    if store.state.accessible && !store.state.isEmpty {
                        Text(boyut(store.state.totalBytes))
                            .font(.system(size: style.height * 0.15))
                            .foregroundStyle(.secondary)
                    }
                }
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { TrashService.open() }
        .onAppear { store.start() }
        // Surukle-birak ile silme: FileManager.trashItem
        .onDrop(of: [.fileURL], isTargeted: $uzerinde) { providers in
            for p in providers {
                _ = p.loadObject(ofClass: URL.self) { url, _ in
                    guard let url else { return }
                    Task { @MainActor in
                        TrashService.moveToTrash(url)
                        store.yenile()
                    }
                }
            }
            return true
        }
        .contextMenu {
            Button("Çöp kutusunu aç") { TrashService.open() }
            // Bosaltmanin genel API karsiligi yok; kullaniciyi yaniltmamak icin
            // dugme koymuyoruz, Finder'a yonlendiriyoruz.
            Text("Boşaltmak için Finder'ı kullanın").font(.caption)
        }
        .help(!store.state.accessible
              ? "Çöp kutusu okunamıyor — Sistem Ayarları ▸ Gizlilik ve Güvenlik ▸ Tam Disk Erişimi"
              : (store.state.isEmpty ? "Çöp kutusu boş"
                 : "\(store.state.itemCount) öğe · \(boyut(store.state.totalBytes))"))
    }

    private func boyut(_ b: UInt64) -> String {
        let f = ByteCountFormatter()
        f.countStyle = .file
        return f.string(fromByteCount: Int64(b))
    }
}

// MARK: - Calan medya
@MainActor
public final class NowPlayingStore: ObservableObject {
    public static let shared = NowPlayingStore()
    @Published public private(set) var sonuc: NowPlayingService.Sonuc = .hicbiriCalmiyor
    private var timer: Timer?
    private init() {}
    public func start() {
        guard timer == nil else { return }
        sonuc = NowPlayingService.read()
        timer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.sonuc = NowPlayingService.read() }
        }
    }
}

/// Calan medya widget'i. Windows karsiligi: Widgets/Media.
/// KAPSAM: yalniz Music ve Spotify (d-media-kapsami). Tarayici ve VLC goremez.
struct NowPlayingWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var store = NowPlayingStore.shared

    var body: some View {
        Group {
            switch store.sonuc {
            case .calan(let n): calan(n)
            case .hicbiriCalmiyor: bilgi("music.note", "Çalmıyor")
            case .izinYok: bilgi("lock", "Otomasyon izni yok")
            }
        }
        .onAppear { store.start() }
        .help("Kapsam: Music ve Spotify. Tarayıcıdaki medya macOS'ta görülemez.")
    }

    private func bilgi(_ ikon: String, _ metin: String) -> some View {
        HStack(spacing: 4) {
            Image(systemName: ikon).font(.system(size: style.iconSize * 0.5))
            Text(metin).font(.system(size: style.height * 0.17))
        }.foregroundStyle(.secondary)
    }

    private func calan(_ n: NowPlaying) -> some View {
        HStack(spacing: 5) {
            Image(systemName: "music.note")
                .font(.system(size: style.iconSize * 0.5))
                .foregroundStyle(Color(nsColor: .controlAccentColor))
            VStack(alignment: .leading, spacing: 0) {
                Text(n.title).font(.system(size: style.height * 0.19, weight: .medium)).lineLimit(1)
                Text(n.artist).font(.system(size: style.height * 0.15))
                    .foregroundStyle(.secondary).lineLimit(1)
            }
            .frame(maxWidth: style.itemHeight * 2.2, alignment: .leading)
            if item.effectiveVariant != "mini" {
                HStack(spacing: 3) {
                    dugme("backward.fill", n.app, "previous track")
                    dugme("playpause.fill", n.app, "playpause")
                    dugme("forward.fill", n.app, "next track")
                }
            }
        }
    }

    private func dugme(_ ikon: String, _ app: String, _ eylem: String) -> some View {
        Image(systemName: ikon)
            .font(.system(size: style.height * 0.17))
            .contentShape(Rectangle())
            .onTapGesture { NowPlayingService.komut(app, eylem) }
    }
}

// MARK: - AI kullanimi
@MainActor
public final class AIUsageStore: ObservableObject {
    public static let shared = AIUsageStore()
    @Published public private(set) var sonuc: AIUsageService.Sonuc = .cliYok
    private var timer: Timer?
    private init() {}
    public func start() {
        guard timer == nil else { return }
        yenile()
        // CLI cagrisi pahali; 10 dakikada bir yeter.
        timer = Timer.scheduledTimer(withTimeInterval: 600, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.yenile() }
        }
    }
    private func yenile() {
        Task.detached {
            let s = AIUsageService.read()
            await MainActor.run { self.sonuc = s }
        }
    }
}

/// AI kullanim widget'i. Windows karsiligi: Widgets/AI/AIUsageWidget.
/// Varyantlar: cubuklar, halkalar.
struct AIUsageWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var store = AIUsageStore.shared

    var body: some View {
        Group {
            switch store.sonuc {
            case .veri(let u):
                if item.effectiveVariant == "rings" { halkalar(u) } else { cubuklar(u) }
            case .cliYok:
                bilgi("terminal", "claude CLI yok")
            case .okunamadi(let m):
                bilgi("exclamationmark.triangle", m)
            }
        }
        .onAppear { store.start() }
    }

    private func bilgi(_ ikon: String, _ metin: String) -> some View {
        HStack(spacing: 4) {
            Image(systemName: ikon).font(.system(size: style.iconSize * 0.5))
            Text(metin).font(.system(size: style.height * 0.16)).lineLimit(1)
        }.foregroundStyle(.secondary)
    }

    private func cubuklar(_ u: AIUsage) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            cubuk("Oturum", u.sessionPercent, .orange)
            cubuk("Hafta", u.weekPercent, .red)
        }
    }

    private func cubuk(_ ad: String, _ p: Double, _ renk: Color) -> some View {
        HStack(spacing: 4) {
            Text(ad).font(.system(size: style.height * 0.14, weight: .semibold))
                .foregroundStyle(.secondary).frame(width: style.height * 0.6, alignment: .leading)
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.primary.opacity(0.18))
                    Capsule().fill(renk).frame(width: geo.size.width * min(p / 100, 1))
                }
            }.frame(width: style.itemHeight * 1.1, height: 3)
            Text("%\(Int(p))").font(.system(size: style.height * 0.14)).monospacedDigit()
        }
    }

    private func halkalar(_ u: AIUsage) -> some View {
        HStack(spacing: style.gap) {
            halka("OTR", u.sessionPercent, .orange)
            halka("HFT", u.weekPercent, .red)
        }
    }

    private func halka(_ ad: String, _ p: Double, _ renk: Color) -> some View {
        let d = style.itemHeight * 0.64
        return ZStack {
            Circle().stroke(Color.primary.opacity(0.18), lineWidth: 2.5)
            Circle().trim(from: 0, to: min(max(p / 100, 0), 1))
                .stroke(renk, style: StrokeStyle(lineWidth: 2.5, lineCap: .round))
                .rotationEffect(.degrees(-90))
            Text(ad).font(.system(size: d * 0.22, weight: .bold)).foregroundStyle(.secondary)
        }.frame(width: d, height: d)
    }
}
