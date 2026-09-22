import SwiftUI
import Combine
import DockHubCore
import DockHubPlatform

@MainActor
public final class WeatherStore: ObservableObject {
    public static let shared = WeatherStore()
    @Published public private(set) var reading: WeatherReading?
    @Published public private(set) var hata: String?
    private var timer: Timer?
    private var lat: Double?, lon: Double?, place: String?
    private init() {}

    public func start(latitude: Double?, longitude: Double?, place: String?) {
        guard timer == nil else { return }
        lat = latitude; lon = longitude; self.place = place
        yenile()
        // Hava yavas degisir; 15 dakika. Open-Meteo'yu gereksiz yormayalim.
        timer = Timer.scheduledTimer(withTimeInterval: 900, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.yenile() }
        }
    }

    private func yenile() {
        guard let lat, let lon else {
            // UYDURMA DEGER YOK: konum yoksa durum acikca bildirilir.
            hata = "Konum ayarlanmamış"
            return
        }
        Task { @MainActor in
            do {
                reading = try await WeatherService.fetch(latitude: lat, longitude: lon, place: place)
                hata = nil
            } catch WeatherError.noLocation {
                hata = "Konum yok"
            } catch WeatherError.network(let m) {
                hata = "Ağ hatası"
                Log.error("Hava durumu alinamadi: \(m)")
            } catch {
                hata = "Veri okunamadı"
            }
        }
    }
}

/// Hava durumu widget'i. Varyantlar: anlik, durum.
/// Konum ayardan gelir (latitude/longitude/place); yoksa deger UYDURULMAZ.
struct WeatherWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var store = WeatherStore.shared

    var body: some View {
        Group {
            if let r = store.reading { icerik(r) }
            else { bilgiYok(store.hata ?? "Yükleniyor…") }
        }
        .onAppear {
            let lat = item.setting("latitude").flatMap { if case .number(let v) = $0 { v } else { nil } }
            let lon = item.setting("longitude").flatMap { if case .number(let v) = $0 { v } else { nil } }
            let place = item.setting("place").flatMap { if case .string(let v) = $0 { v } else { nil } }
            store.start(latitude: lat, longitude: lon, place: place)
        }
    }

    private func bilgiYok(_ mesaj: String) -> some View {
        HStack(spacing: 4) {
            Image(systemName: "cloud.slash").font(.system(size: style.iconSize * 0.5))
            Text(mesaj).font(.system(size: style.height * 0.17))
        }
        .foregroundStyle(.secondary)
    }

    @ViewBuilder private func icerik(_ r: WeatherReading) -> some View {
        HStack(spacing: 5) {
            Image(systemName: r.symbolName)
                .font(.system(size: style.iconSize * 0.58))
                .symbolRenderingMode(.multicolor)
            if item.effectiveVariant == "conditions" {
                VStack(alignment: .leading, spacing: 0) {
                    Text(r.description).font(.system(size: style.height * 0.2, weight: .medium))
                    if let p = r.place {
                        Text(p).font(.system(size: style.height * 0.16)).foregroundStyle(.secondary)
                    }
                }
            } else {
                VStack(alignment: .leading, spacing: 0) {
                    Text("\(Int(r.temperatureC.rounded()))°")
                        .font(.system(size: style.height * 0.28, weight: .semibold))
                        .monospacedDigit()
                    if let p = r.place {
                        Text(p).font(.system(size: style.height * 0.16)).foregroundStyle(.secondary)
                    }
                }
            }
        }
    }
}
