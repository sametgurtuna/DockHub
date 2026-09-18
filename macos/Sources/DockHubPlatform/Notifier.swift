import Foundation
import UserNotifications
import DockHubCore

/// Yerel bildirimler. Windows karsiligi: Microsoft.Toolkit.Uwp.Notifications toast.
/// Eslestirme ag-notifications (mapped): UNUserNotificationCenter + UNNotificationAction.
///
/// ONEMLI: imzasiz bir .app paketinde izin istegi reddedilebilir veya merkez hic
/// calismayabilir. Bu durumda UYGULAMA COKMEZ; durum kaydedilir ve widget'lar
/// calismaya devam eder, yalniz bildirim gitmez.
@MainActor
public enum Notifier {
    public private(set) static var izinDurumu: String = "sorulmadi"
    public private(set) static var kullanilabilir = false

    /// Kategori kimlikleri; eylemli bildirimler icin.
    public static let hydrationCategory = "dockhub.hydration"
    public static let reminderCategory = "dockhub.reminder"

    public static func setup() async {
        guard Bundle.main.bundleIdentifier != nil else {
            izinDurumu = "paket yok"                   // ciplak ikilide merkez cokuyor
            return
        }
        let center = UNUserNotificationCenter.current()

        // Windows'taki "Su ictim" ve "10 dk ertele" dugmelerinin karsiligi
        let ictim = UNNotificationAction(identifier: "drank", title: "Su içtim", options: [])
        let ertele = UNNotificationAction(identifier: "snooze", title: "10 dk ertele", options: [])
        center.setNotificationCategories([
            UNNotificationCategory(identifier: hydrationCategory, actions: [ictim],
                                   intentIdentifiers: [], options: []),
            UNNotificationCategory(identifier: reminderCategory, actions: [ertele],
                                   intentIdentifiers: [], options: []),
        ])

        do {
            let ok = try await center.requestAuthorization(options: [.alert, .sound, .badge])
            kullanilabilir = ok
            izinDurumu = ok ? "verildi" : "reddedildi"
        } catch {
            kullanilabilir = false
            izinDurumu = "hata: \(error.localizedDescription)"
            Log.error("Bildirim izni alinamadi", error)
        }
    }

    /// Hemen veya belirli bir sure sonra bildirim gonderir.
    /// Bildirim gonderilemezse sessizce gecilir; cagiran taraf calismaya devam eder.
    public static func gonder(baslik: String, metin: String,
                              after seconds: TimeInterval = 0,
                              kategori: String? = nil,
                              kalici: Bool = false,
                              id: String = UUID().uuidString) {
        guard kullanilabilir else { return }
        let content = UNMutableNotificationContent()
        content.title = baslik
        content.body = metin
        content.sound = kalici ? .defaultCritical : .default
        // Alarmin kapatilana kadar kalmasi icin: Windows'taki kalici toast karsiligi
        content.interruptionLevel = kalici ? .timeSensitive : .active
        if let kategori { content.categoryIdentifier = kategori }

        let trigger: UNNotificationTrigger? = seconds > 0
            ? UNTimeIntervalNotificationTrigger(timeInterval: seconds, repeats: false)
            : nil
        UNUserNotificationCenter.current()
            .add(UNNotificationRequest(identifier: id, content: content, trigger: trigger))
    }

    /// Belirli bir saatte (gerekirse her gun tekrar) bildirim planlar.
    /// Alarm icin: UNCalendarNotificationTrigger, tekrar isini sistem yapar.
    public static func planlaSaat(saat: Int, dakika: Int, tekrar: Bool,
                                  baslik: String, metin: String, id: String) {
        guard kullanilabilir else { return }
        var comps = DateComponents()
        comps.hour = saat
        comps.minute = dakika

        let content = UNMutableNotificationContent()
        content.title = baslik
        content.body = metin
        content.sound = .defaultCritical
        content.interruptionLevel = .timeSensitive      // kapatilana kadar kalsin

        let trigger = UNCalendarNotificationTrigger(dateMatching: comps, repeats: tekrar)
        UNUserNotificationCenter.current()
            .add(UNNotificationRequest(identifier: id, content: content, trigger: trigger))
    }

    public static func iptal(id: String) {
        guard kullanilabilir else { return }
        UNUserNotificationCenter.current()
            .removePendingNotificationRequests(withIdentifiers: [id])
    }
}
