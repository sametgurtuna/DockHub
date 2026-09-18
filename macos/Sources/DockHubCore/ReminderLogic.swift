import Foundation

/// Hatirlatici siralama ve "sonraki" secimi. Gorunumden AYRI tutuldu ki
/// birim testle sinanabilsin; widget bu fonksiyonlari cagirir.
public struct Reminder: Sendable, Equatable {
    public let text: String
    public let hour: Int
    public let minute: Int

    public init(text: String, hour: Int, minute: Int) {
        self.text = text; self.hour = hour; self.minute = minute
    }
    public var minutesOfDay: Int { hour * 60 + minute }
    public var timeText: String { String(format: "%02d:%02d", hour, minute) }
}

public enum ReminderLogic {
    /// "HH:mm" metnini ayristirir; gecersizse nil.
    public static func parseTime(_ s: String) -> (hour: Int, minute: Int)? {
        let p = s.split(separator: ":")
        guard p.count == 2, let h = Int(p[0]), let m = Int(p[1]),
              (0...23).contains(h), (0...59).contains(m) else { return nil }
        return (h, m)
    }

    public static func sorted(_ list: [Reminder]) -> [Reminder] {
        list.sorted { $0.minutesOfDay < $1.minutesOfDay }
    }

    /// Su andan sonraki ilk hatirlatici. Gunun hepsi gecmisse bastaki
    /// (yarinki ilk) doner - liste bos degilse her zaman bir sonuc verir.
    public static func next(from list: [Reminder], nowMinutes: Int) -> Reminder? {
        let s = sorted(list)
        return s.first { $0.minutesOfDay > nowMinutes } ?? s.first
    }
}
