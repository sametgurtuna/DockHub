import SwiftUI
import DockHubCore
import DockHubPlatform

private func bugun() -> String {
    let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd"; return f.string(from: Date())
}

// MARK: - Su takibi
/// Windows karsiligi: Widgets/Hydration. Varyantlar: zamanlayici, gunluk hedef.
/// Sayac config.json icine yazilir; gun degisince kendiliginden sifirlanir.
struct HydrationWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel

    private var hedef: Int { Int(item.numberSetting("goal", default: 8)) }
    private var sayac: Int { Int(item.numberSetting("count", default: 0)) }
    private var kayitliGun: String {
        if case .string(let s)? = item.setting("date") { return s }
        return ""
    }

    var body: some View {
        HStack(spacing: 5) {
            Image(systemName: sayac >= hedef ? "drop.fill" : "drop")
                .font(.system(size: style.iconSize * 0.55))
                .foregroundStyle(sayac >= hedef ? .green : Color(nsColor: .controlAccentColor))
            if item.effectiveVariant == "progress" {
                VStack(alignment: .leading, spacing: 1) {
                    Text("\(sayac)/\(hedef)")
                        .font(.system(size: style.height * 0.24, weight: .semibold)).monospacedDigit()
                    GeometryReader { geo in
                        ZStack(alignment: .leading) {
                            Capsule().fill(Color.primary.opacity(0.18))
                            Capsule().fill(Color(nsColor: .controlAccentColor))
                                .frame(width: geo.size.width * min(Double(sayac) / Double(max(hedef,1)), 1))
                        }
                    }.frame(width: style.itemHeight * 1.1, height: 3)
                }
            } else {
                Text("\(sayac)/\(hedef)")
                    .font(.system(size: style.height * 0.24, weight: .semibold)).monospacedDigit()
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { ekle() }
        .contextMenu { Button("Sıfırla") { model.setSetting(item.id, "count", .number(0)) } }
        .onAppear { gunKontrol() }
        .help("Tıkla: bir bardak ekle")
    }

    /// Gun degistiyse sayaci sifirla. Windows surumu de gunluk sayiyor.
    private func gunKontrol() {
        let g = bugun()
        if kayitliGun != g {
            model.setSetting(item.id, "date", .string(g))
            model.setSetting(item.id, "count", .number(0))
        }
    }

    private func ekle() {
        gunKontrol()
        let yeni = sayac + 1
        model.setSetting(item.id, "count", .number(Double(yeni)))
        if yeni == hedef {
            Notifier.gonder(baslik: "Günlük su hedefi", metin: "\(hedef) bardak tamamlandı.")
        }
    }
}

// MARK: - Hatirlaticilar
/// Windows karsiligi: Widgets/Reminders. Varyantlar: liste, sonraki, sayi.
/// Hatirlaticilar ayardan: reminders = [{text, time "HH:mm"}]
struct RemindersWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel
    @State private var planlandi = false

    /// Ayristirma ve siralama DockHubCore/ReminderLogic icinde; burada yalniz cizim.
    private var liste: [Reminder] {
        guard case .array(let arr)? = item.setting("reminders") else { return [] }
        return ReminderLogic.sorted(arr.compactMap { v in
            guard case .object(let o) = v,
                  case .string(let text)? = o["text"],
                  case .string(let t)? = o["time"],
                  let hm = ReminderLogic.parseTime(t) else { return nil }
            return Reminder(text: text, hour: hm.hour, minute: hm.minute)
        })
    }

    private var sonraki: Reminder? {
        let c = Calendar.current.dateComponents([.hour, .minute], from: Date())
        return ReminderLogic.next(from: liste, nowMinutes: (c.hour ?? 0) * 60 + (c.minute ?? 0))
    }

    var body: some View {
        let l = liste
        Group {
            if l.isEmpty {
                HStack(spacing: 4) {
                    Image(systemName: "bell.slash")
                    Text("Hatırlatıcı yok").font(.system(size: style.height * 0.17))
                }.foregroundStyle(.secondary)
            } else {
                switch item.effectiveVariant {
                case "count": sayi(l)
                case "list":  listeGorunum(l)
                default:      sonrakiGorunum()
                }
            }
        }
        .onAppear { planla(l) }
    }

    private func sayi(_ l: [Reminder]) -> some View {
        HStack(spacing: 5) {
            Image(systemName: "bell.badge").font(.system(size: style.iconSize * 0.55))
            Text("\(l.count)").font(.system(size: style.height * 0.28, weight: .semibold)).monospacedDigit()
        }
    }

    private func listeGorunum(_ l: [Reminder]) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(l.prefix(2).enumerated()), id: \.offset) { _, h in
                HStack(spacing: 4) {
                    Text(h.timeText).font(.system(size: style.height * 0.16, weight: .semibold))
                        .monospacedDigit().foregroundStyle(.secondary)
                    Text(h.text).font(.system(size: style.height * 0.17)).lineLimit(1)
                }
            }
            if l.count > 2 {
                Text("+\(l.count - 2) daha")
                    .font(.system(size: style.height * 0.14)).foregroundStyle(.tertiary)
            }
        }
    }

    private func sonrakiGorunum() -> some View {
        HStack(spacing: 5) {
            Image(systemName: "bell").font(.system(size: style.iconSize * 0.5))
            VStack(alignment: .leading, spacing: 0) {
                Text(sonraki?.timeText ?? "--:--")
                    .font(.system(size: style.height * 0.24, weight: .semibold)).monospacedDigit()
                Text(sonraki?.text ?? "")
                    .font(.system(size: style.height * 0.15)).foregroundStyle(.secondary).lineLimit(1)
            }
        }
    }

    /// Her hatirlatici icin gunluk tekrarli bildirim; tekrari sistem yapar.
    private func planla(_ l: [Reminder]) {
        guard !planlandi else { return }
        planlandi = true
        for (i, h) in l.enumerated() {
            Notifier.planlaSaat(saat: h.hour, dakika: h.minute, tekrar: true,
                                baslik: "Hatırlatıcı", metin: h.text,
                                id: "\(item.id)-\(i)")
        }
    }
}

// MARK: - Yapiskan not
/// Windows karsiligi: Widgets/Notes. Dock'ta onizleme, tiklayinca duzenleme.
/// Metin config.json icine yazilir; yeniden baslatmada korunur.
struct StickyNoteWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject var model: DockModel
    @State private var aciliyor = false
    @State private var taslak = ""

    private var metin: String {
        if case .string(let s)? = item.setting("text") { return s }
        return ""
    }
    private var renk: Color {
        switch item.setting("color") {
        case .string("pink")?:   .pink
        case .string("blue")?:   .blue
        case .string("green")?:  .green
        case .string("purple")?: .purple
        case .string("orange")?: .orange
        default:                 .yellow
        }
    }

    var body: some View {
        HStack(spacing: 5) {
            Image(systemName: "note.text")
                .font(.system(size: style.iconSize * 0.55))
                .foregroundStyle(renk)
            Text(metin.isEmpty ? "Not ekle" : metin)
                .font(.system(size: style.height * 0.19))
                .foregroundStyle(metin.isEmpty ? .secondary : .primary)
                .lineLimit(2)
                .frame(maxWidth: style.itemHeight * 2.4, alignment: .leading)
        }
        .contentShape(Rectangle())
        .onTapGesture { taslak = metin; aciliyor = true }
        .popover(isPresented: $aciliyor, arrowEdge: .top) { duzenleyici }
    }

    private var duzenleyici: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Yapışkan not").font(.headline)
            TextEditor(text: $taslak)
                .font(.system(size: 13))
                .frame(width: 260, height: 140)
                .overlay(RoundedRectangle(cornerRadius: 6).stroke(renk.opacity(0.5), lineWidth: 2))
            HStack {
                Spacer()
                Button("Kaydet") {
                    model.setSetting(item.id, "text", .string(taslak))
                    aciliyor = false
                }.keyboardShortcut(.defaultAction)
            }
        }
        .padding(12)
    }
}
