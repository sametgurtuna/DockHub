import SwiftUI
import DockHubCore

// MARK: - Dunya saati
/// Windows karsiligi: Widgets/WorldClock. Varyantlar: tek sehir, coklu sehir.
/// Sehirler ayardan gelir: cities = [{name, timezone}]
struct WorldClockWidget: View {
    let item: DockItem
    let style: DockStyle

    private struct Sehir: Identifiable {
        let id = UUID(); let name: String; let tz: TimeZone
    }

    private var sehirler: [Sehir] {
        guard case .array(let arr)? = item.setting("cities") else { return [] }
        return arr.compactMap { v in
            guard case .object(let o) = v,
                  case .string(let name)? = o["name"],
                  case .string(let tzid)? = o["timezone"],
                  let tz = TimeZone(identifier: tzid) else { return nil }
            return Sehir(name: name, tz: tz)
        }
    }

    var body: some View {
        let list = sehirler
        if list.isEmpty {
            HStack(spacing: 4) {
                Image(systemName: "globe")
                Text("Şehir eklenmemiş").font(.system(size: style.height * 0.17))
            }.foregroundStyle(.secondary)
        } else {
            TimelineView(.periodic(from: .now, by: 30)) { ctx in
                let gosterilecek = item.effectiveVariant == "single"
                    ? Array(list.prefix(1)) : list
                HStack(spacing: style.gap * 1.5) {
                    ForEach(gosterilecek) { s in sehir(s, ctx.date) }
                }
            }
        }
    }

    private func sehir(_ s: Sehir, _ date: Date) -> some View {
        let f = DateFormatter()
        f.timeZone = s.tz
        f.dateFormat = "HH:mm"
        let saat = Calendar.current.dateComponents(in: s.tz, from: date).hour ?? 12
        let gunduz = (7..<19).contains(saat)
        return VStack(alignment: .leading, spacing: 0) {
            HStack(spacing: 3) {
                Image(systemName: gunduz ? "sun.max.fill" : "moon.fill")
                    .font(.system(size: style.height * 0.15))
                    .foregroundStyle(gunduz ? .yellow : .indigo)
                Text(f.string(from: date))
                    .font(.system(size: style.height * 0.24, weight: .semibold))
                    .monospacedDigit()
            }
            Text(s.name)
                .font(.system(size: style.height * 0.15))
                .foregroundStyle(.secondary)
        }
    }
}

// MARK: - Zaman ilerlemesi
/// Windows karsiligi: Widgets/TimeProgress. Varyantlar: cubuk, halka.
/// Kapsam ayardan: scope = day | week | month | year
struct TimeProgressWidget: View {
    let item: DockItem
    let style: DockStyle

    private var scope: String {
        if case .string(let s)? = item.setting("scope") { return s }
        return "day"
    }

    var body: some View {
        TimelineView(.periodic(from: .now, by: 60)) { ctx in
            let p = oran(ctx.date)
            if item.effectiveVariant == "ring" { halka(p) } else { cubuk(p) }
        }
    }

    /// Gecen sure / toplam sure. Takvim sinirlari Calendar'dan alinir,
    /// 30 gun gibi sabitler kullanilmaz - ay uzunluklari ve yil farklari dogru olsun.
    func oran(_ now: Date) -> Double {
        let cal = Calendar.current
        let unit: Calendar.Component = switch scope {
        case "week": .weekOfYear
        case "month": .month
        case "year": .year
        default: .day
        }
        guard let aralik = cal.dateInterval(of: unit, for: now) else { return 0 }
        let gecen = now.timeIntervalSince(aralik.start)
        return min(max(gecen / aralik.duration, 0), 1)
    }

    private var etiket: String {
        switch scope {
        case "week": "Hafta"
        case "month": "Ay"
        case "year": "Yıl"
        default: "Gün"
        }
    }

    private func cubuk(_ p: Double) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 4) {
                Text(etiket).font(.system(size: style.height * 0.17, weight: .semibold))
                    .foregroundStyle(.secondary)
                Text("%\(Int((p * 100).rounded()))")
                    .font(.system(size: style.height * 0.19, weight: .medium)).monospacedDigit()
            }
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.primary.opacity(0.18))
                    Capsule().fill(Color(nsColor: .controlAccentColor))
                        .frame(width: geo.size.width * p)
                }
            }
            .frame(width: style.itemHeight * 1.6, height: 4)
        }
    }

    private func halka(_ p: Double) -> some View {
        let d = style.itemHeight * 0.72
        return ZStack {
            Circle().stroke(Color.primary.opacity(0.18), lineWidth: 3)
            Circle().trim(from: 0, to: p)
                .stroke(Color(nsColor: .controlAccentColor),
                        style: StrokeStyle(lineWidth: 3, lineCap: .round))
                .rotationEffect(.degrees(-90))
            Text("\(Int((p * 100).rounded()))")
                .font(.system(size: d * 0.3, weight: .bold)).monospacedDigit()
        }
        .frame(width: d, height: d)
    }
}
