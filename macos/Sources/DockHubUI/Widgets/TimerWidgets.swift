import SwiftUI
import DockHubCore
import DockHubPlatform

/// Sure bicimleyiciler
private func mmss(_ t: TimeInterval) -> String {
    let s = max(0, Int(t))
    return String(format: "%02d:%02d", s / 60, s % 60)
}
private func mmssd(_ t: TimeInterval) -> String {
    let s = max(0, t)
    return String(format: "%02d:%02d.%d", Int(s) / 60, Int(s) % 60, Int(s * 10) % 10)
}
private func hhmmss(_ t: TimeInterval) -> String {
    let s = max(0, Int(t))
    return s >= 3600
        ? String(format: "%d:%02d:%02d", s / 3600, (s % 3600) / 60, s % 60)
        : mmss(t)
}

// MARK: - Kronometre
/// Windows karsiligi: Widgets/Timers/StopwatchWidget.
/// Tiklama baslat/duraklat, sag tik sifirla - Windows ile ayni etkilesim.
struct StopwatchWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var store = TimerStore.shared

    var body: some View {
        let calisiyor = store.isRunning(item.id)
        TimelineView(.periodic(from: .now, by: calisiyor ? 0.1 : 3600)) { ctx in
            HStack(spacing: 5) {
                Image(systemName: calisiyor ? "pause.fill" : "stopwatch")
                    .font(.system(size: style.iconSize * 0.5))
                    .foregroundStyle(calisiyor ? Color(nsColor: .controlAccentColor) : .secondary)
                Text(mmssd(store.elapsed(item.id, now: ctx.date)))
                    .font(.system(size: style.height * 0.26, weight: .semibold))
                    .monospacedDigit()
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { store.toggle(item.id) }
        .contextMenu { Button("Sıfırla") { store.reset(item.id) } }
    }
}

// MARK: - Geri sayim
/// Windows karsiligi: Widgets/Timers/CountdownWidget.
/// Toplam sure ayardan (minutes); bitiste bildirim.
struct CountdownWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var store = TimerStore.shared

    private var toplam: TimeInterval { item.numberSetting("minutes", default: 5) * 60 }
    private var etiket: String {
        if case .string(let s)? = item.setting("label") { return s }
        return "Geri sayım"
    }

    var body: some View {
        let calisiyor = store.isRunning(item.id)
        TimelineView(.periodic(from: .now, by: calisiyor ? 1 : 3600)) { ctx in
            let kalan = toplam - store.elapsed(item.id, now: ctx.date)
            HStack(spacing: 5) {
                Image(systemName: kalan <= 0 ? "bell.fill"
                                : (calisiyor ? "pause.fill" : "timer"))
                    .font(.system(size: style.iconSize * 0.5))
                    .foregroundStyle(kalan <= 0 ? .orange
                                     : (calisiyor ? Color(nsColor: .controlAccentColor) : .secondary))
                VStack(alignment: .leading, spacing: 0) {
                    Text(kalan <= 0 ? "Bitti" : hhmmss(kalan))
                        .font(.system(size: style.height * 0.25, weight: .semibold))
                        .monospacedDigit()
                    Text(etiket)
                        .font(.system(size: style.height * 0.15))
                        .foregroundStyle(.secondary)
                }
            }
        }
        .contentShape(Rectangle())
        .onTapGesture {
            let kalan = toplam - store.elapsed(item.id)
            store.toggle(item.id, notifyAfter: kalan,
                         baslik: etiket, metin: "Geri sayım tamamlandı.")
        }
        .contextMenu { Button("Sıfırla") { store.reset(item.id) } }
    }
}

// MARK: - Odak zamanlayici (pomodoro)
/// Windows karsiligi: Widgets/Timers/FocusWidget.
/// Odak ve mola sureleri ayardan; asama gecen sureden HESAPLANIR, ayrica saklanmaz.
struct FocusWidget: View {
    let item: DockItem
    let style: DockStyle
    @ObservedObject private var store = TimerStore.shared

    private var odak: TimeInterval { item.numberSetting("focusMinutes", default: 25) * 60 }
    private var mola: TimeInterval { item.numberSetting("breakMinutes", default: 5) * 60 }

    /// Gecen sureye gore hangi asamadayiz ve o asamadan ne kadar kaldi.
    private func asama(_ gecen: TimeInterval) -> (molada: Bool, kalan: TimeInterval) {
        let tur = odak + mola
        let icinde = gecen.truncatingRemainder(dividingBy: tur)
        return icinde < odak ? (false, odak - icinde) : (true, tur - icinde)
    }

    var body: some View {
        let calisiyor = store.isRunning(item.id)
        TimelineView(.periodic(from: .now, by: calisiyor ? 1 : 3600)) { ctx in
            let (molada, kalan) = asama(store.elapsed(item.id, now: ctx.date))
            HStack(spacing: 5) {
                Image(systemName: molada ? "cup.and.saucer.fill" : "brain.head.profile")
                    .font(.system(size: style.iconSize * 0.5))
                    .foregroundStyle(molada ? .green : Color(nsColor: .controlAccentColor))
                VStack(alignment: .leading, spacing: 0) {
                    Text(mmss(kalan))
                        .font(.system(size: style.height * 0.25, weight: .semibold))
                        .monospacedDigit()
                    Text(molada ? "Mola" : "Odak")
                        .font(.system(size: style.height * 0.15))
                        .foregroundStyle(.secondary)
                }
            }
        }
        .contentShape(Rectangle())
        .onTapGesture {
            let (molada, kalan) = asama(store.elapsed(item.id))
            store.toggle(item.id, notifyAfter: kalan,
                         baslik: molada ? "Mola bitti" : "Odak bitti",
                         metin: molada ? "Yeni odak turu başlıyor." : "Mola zamanı.")
        }
        .contextMenu { Button("Sıfırla") { store.reset(item.id) } }
    }
}

// MARK: - Alarm
/// Windows karsiligi: Widgets/Timers/AlarmWidget.
/// Saat, etiket ve gunluk tekrar ayardan. Tekrari SISTEM yapar
/// (UNCalendarNotificationTrigger), biz zamanlayici dondurmeyiz.
struct AlarmWidget: View {
    let item: DockItem
    let style: DockStyle
    @State private var planlandi = false

    private var saatDk: (Int, Int) {
        if case .string(let s)? = item.setting("time") {
            let p = s.split(separator: ":").compactMap { Int($0) }
            if p.count == 2 { return (p[0], p[1]) }
        }
        return (7, 0)
    }
    private var etiket: String {
        if case .string(let s)? = item.setting("label") { return s }
        return "Alarm"
    }
    private var tekrar: Bool { item.boolSetting("repeatDaily", default: true) }
    private var acik: Bool { item.boolSetting("enabled", default: true) }

    var body: some View {
        let (h, m) = saatDk
        HStack(spacing: 5) {
            Image(systemName: acik ? "alarm.fill" : "alarm")
                .font(.system(size: style.iconSize * 0.5))
                .foregroundStyle(acik ? .orange : .secondary)
            VStack(alignment: .leading, spacing: 0) {
                Text(String(format: "%02d:%02d", h, m))
                    .font(.system(size: style.height * 0.25, weight: .semibold))
                    .monospacedDigit()
                Text(acik ? (tekrar ? "Her gün · \(etiket)" : etiket) : "Kapalı")
                    .font(.system(size: style.height * 0.15))
                    .foregroundStyle(.secondary)
            }
        }
        .onAppear {
            guard acik, !planlandi else { return }
            planlandi = true
            Notifier.planlaSaat(saat: h, dakika: m, tekrar: tekrar,
                                baslik: etiket, metin: "Alarm çalıyor.", id: item.id)
        }
    }
}
