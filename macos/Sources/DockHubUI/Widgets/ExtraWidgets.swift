import SwiftUI
import UniformTypeIdentifiers
import DockHubCore
import DockHubPlatform

// MARK: - Kisayol
/// macOS Kisayollar uygulamasindaki bir kisayolu tek tikla calistirir.
/// PARITE DISI EK: Windows surumunde karsiligi yoktur (origin: ek).
/// Kisayol adi ayardan gelir: name = "Kısayol Adı"
struct ShortcutWidget: View {
    let item: DockItem
    let style: DockStyle
    @State private var durum: Durum = .hazir

    private enum Durum: Equatable { case hazir, calisiyor, bitti, hata(String) }

    private var ad: String? {
        if case .string(let s)? = item.setting("name"), !s.isEmpty { return s }
        return nil
    }
    private var ikon: String {
        if case .string(let s)? = item.setting("icon") { return s }
        return "bolt.fill"
    }

    var body: some View {
        HStack(spacing: 5) {
            Image(systemName: gosterilenIkon)
                .font(.system(size: style.iconSize * 0.55))
                .foregroundStyle(renk)
                .symbolEffect(.pulse, isActive: durum == .calisiyor)
            VStack(alignment: .leading, spacing: 0) {
                Text(ad ?? "No shortcut selected")
                    .font(.system(size: style.height * 0.2, weight: .medium))
                    .lineLimit(1)
                if case .hata(let m) = durum {
                    Text(m).font(.system(size: style.height * 0.14))
                        .foregroundStyle(.red).lineLimit(1)
                }
            }
            .frame(maxWidth: style.itemHeight * 2, alignment: .leading)
        }
        .contentShape(Rectangle())
        .onTapGesture { calistir() }
        .help(ad.map { "Click to run \($0)" } ?? "Set a shortcut name in the widget settings")
    }

    private var gosterilenIkon: String {
        switch durum {
        case .calisiyor: "hourglass"
        case .bitti:     "checkmark.circle.fill"
        case .hata:      "exclamationmark.triangle.fill"
        case .hazir:     ikon
        }
    }
    private var renk: Color {
        switch durum {
        case .bitti: .green
        case .hata:  .red
        default:     Color(nsColor: .controlAccentColor)
        }
    }

    private func calistir() {
        guard let ad else { durum = .hata("no name"); return }
        guard ShortcutsService.kullanilabilir else { durum = .hata("Shortcuts unavailable"); return }
        durum = .calisiyor
        Task.detached {
            let s = ShortcutsService.run(ad)
            await MainActor.run {
                switch s {
                case .calisti:      durum = .bitti
                case .bulunamadi:   durum = .hata("CLI not found")
                case .zamanAsimi:   durum = .hata("timed out")
                case .hata(let m):  durum = .hata(String(m.prefix(30)))
                }
            }
            try? await Task.sleep(for: .seconds(3))
            await MainActor.run { if durum != .calisiyor { durum = .hazir } }
        }
    }
}

// MARK: - AirDrop
/// Birakilan dosyayi AirDrop ile gonderir; alicIyi macOS'un kendi penceresinde
/// KULLANICI secer. PARITE DISI EK (origin: ek).
struct AirDropWidget: View {
    let item: DockItem
    let style: DockStyle
    @State private var uzerinde = false
    @State private var mesaj: String?

    var body: some View {
        HStack(spacing: 5) {
            Image(systemName: "shareplay")
                .font(.system(size: style.iconSize * 0.6))
                .foregroundStyle(uzerinde ? Color(nsColor: .controlAccentColor) : .primary)
                .scaleEffect(uzerinde ? 1.15 : 1)
            if item.effectiveVariant != "icon" {
                Text(mesaj ?? "AirDrop")
                    .font(.system(size: style.height * 0.18))
                    .foregroundStyle(mesaj == nil ? .secondary : .primary)
                    .lineLimit(1)
            }
        }
        .contentShape(Rectangle())
        .onDrop(of: [.fileURL], isTargeted: $uzerinde) { providers in
            Task { @MainActor in gonder(providers) }
            return true
        }
        .help("Drop files, photos or links here; you choose the recipient")
    }

    private func gonder(_ providers: [NSItemProvider]) {
        var urls: [URL] = []
        let grup = DispatchGroup()
        for p in providers {
            grup.enter()
            _ = p.loadObject(ofClass: URL.self) { url, _ in
                if let url { urls.append(url) }
                grup.leave()
            }
        }
        grup.notify(queue: .main) {
            guard !urls.isEmpty else { return }
            mesaj = AirDropService.send(urls)
                ? "Sending \(urls.count) file(s)"
                : "AirDrop is unavailable"
            Task { @MainActor in
                try? await Task.sleep(for: .seconds(4))
                mesaj = nil
            }
        }
    }
}
