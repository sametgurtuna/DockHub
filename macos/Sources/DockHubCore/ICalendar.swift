import Foundation

/// Takvimden bir etkinlik (tekrarlanan etkinligin tek bir tekrari).
/// Windows karsiligi: Services/CalendarService.cs -> CalendarEntry.
public struct CalendarEntry: Sendable, Equatable {
    public let title: String
    public let start: Date
    public let end: Date
    public let allDay: Bool
    public let location: String?
    /// Teams, Meet, Zoom ya da Webex toplanti baglantisi.
    public let joinURL: String?
}

/// iCal (.ics) okuyucu. Windows Ical.Net kullaniyor; Mac'te esi olmadigi icin
/// takvim aboneliklerinde gercekten gecen kisim burada: VEVENT, tum gun ve saatli
/// etkinlikler, TZID (IANA ve Windows adlari), DURATION, RRULE (DAILY, WEEKLY +
/// BYDAY, MONTHLY, YEARLY; INTERVAL, COUNT, UNTIL), EXDATE, RECURRENCE-ID ve
/// iptal edilen etkinlikler.
public enum ICalendar {
    // ---------------- Satirlar ve ozellikler

    /// RFC 5545 3.1: bosluk ya da sekmeyle baslayan satir oncekinin devamidir.
    static func unfold(_ text: String) -> [String] {
        var lines: [String] = []
        for raw in text.replacingOccurrences(of: "\r\n", with: "\n").split(separator: "\n", omittingEmptySubsequences: false) {
            if let first = raw.first, first == " " || first == "\t", !lines.isEmpty {
                lines[lines.count - 1] += raw.dropFirst()
            } else {
                lines.append(String(raw))
            }
        }
        return lines.filter { !$0.isEmpty }
    }

    struct Property {
        let name: String
        let params: [String: String]
        let value: String
    }

    /// "DTSTART;TZID=Europe/Istanbul:20261002T090000" -> ad, parametreler, deger. Tirnak icindeki : ve ; sayilmaz.
    static func property(_ line: String) -> Property? {
        var inQuotes = false
        var parts: [String] = []
        var current = ""
        var valueStart: String.Index?
        for index in line.indices {
            let c = line[index]
            if c == "\"" { inQuotes.toggle(); current.append(c); continue }
            if !inQuotes && c == ":" { parts.append(current); valueStart = line.index(after: index); break }
            if !inQuotes && c == ";" { parts.append(current); current = ""; continue }
            current.append(c)
        }
        guard let valueStart, let name = parts.first, !name.isEmpty else { return nil }
        var params: [String: String] = [:]
        for p in parts.dropFirst() {
            let kv = p.split(separator: "=", maxSplits: 1)
            guard kv.count == 2 else { continue }
            params[kv[0].uppercased()] = kv[1].trimmingCharacters(in: CharacterSet(charactersIn: "\""))
        }
        return Property(name: name.uppercased(), params: params, value: String(line[valueStart...]))
    }

    /// Metin degerindeki kacislar: \n, \, \; \\.
    static func unescape(_ text: String) -> String {
        var out = ""
        var escaping = false
        for c in text {
            if escaping {
                out.append(c == "n" || c == "N" ? "\n" : c)
                escaping = false
            } else if c == "\\" {
                escaping = true
            } else {
                out.append(c)
            }
        }
        return out
    }

    // ---------------- Tarih ve sure

    /// Outlook takvimlerinde gecen Windows saat dilimi adlari.
    static let windowsZones: [String: String] = [
        "Turkey Standard Time": "Europe/Istanbul", "GMT Standard Time": "Europe/London",
        "W. Europe Standard Time": "Europe/Berlin", "Romance Standard Time": "Europe/Paris",
        "Central Europe Standard Time": "Europe/Budapest", "Central European Standard Time": "Europe/Warsaw",
        "E. Europe Standard Time": "Europe/Chisinau", "FLE Standard Time": "Europe/Kiev",
        "GTB Standard Time": "Europe/Bucharest", "Russian Standard Time": "Europe/Moscow",
        "Eastern Standard Time": "America/New_York", "Central Standard Time": "America/Chicago",
        "Mountain Standard Time": "America/Denver", "Pacific Standard Time": "America/Los_Angeles",
        "India Standard Time": "Asia/Kolkata", "China Standard Time": "Asia/Shanghai",
        "Tokyo Standard Time": "Asia/Tokyo", "Arabian Standard Time": "Asia/Dubai",
        "AUS Eastern Standard Time": "Australia/Sydney", "UTC": "UTC",
    ]

    static func timeZone(_ id: String?, fallback: TimeZone) -> TimeZone {
        guard let id, !id.isEmpty else { return fallback }
        if let zone = TimeZone(identifier: id) { return zone }
        if let mapped = windowsZones[id], let zone = TimeZone(identifier: mapped) { return zone }
        return fallback
    }

    /// "20261002" (tum gun), "20261002T090000" (TZID ya da yerel), "20261002T060000Z" (UTC).
    static func date(_ value: String, params: [String: String], local: TimeZone) -> (date: Date, allDay: Bool, zone: TimeZone)? {
        let v = value.trimmingCharacters(in: .whitespaces)
        let digits = v.filter(\.isNumber)
        func num(_ from: Int, _ count: Int) -> Int? {
            guard digits.count >= from + count else { return nil }
            let start = digits.index(digits.startIndex, offsetBy: from)
            return Int(digits[start..<digits.index(start, offsetBy: count)])
        }
        guard let y = num(0, 4), let mo = num(4, 2), let d = num(6, 2) else { return nil }
        let allDay = params["VALUE"]?.uppercased() == "DATE" || !v.contains("T")
        var components = DateComponents(year: y, month: mo, day: d)
        let zone: TimeZone
        if allDay {
            zone = local
        } else {
            guard let h = num(8, 2), let mi = num(10, 2) else { return nil }
            components.hour = h
            components.minute = mi
            components.second = num(12, 2) ?? 0
            zone = v.hasSuffix("Z") ? TimeZone(identifier: "UTC")! : timeZone(params["TZID"], fallback: local)
        }
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        guard let date = calendar.date(from: components) else { return nil }
        return (date, allDay, zone)
    }

    /// "PT1H30M", "P1D", "P2W", "-PT15M" -> saniye.
    static func duration(_ value: String) -> TimeInterval? {
        var text = value.trimmingCharacters(in: .whitespaces).uppercased()
        var sign: Double = 1
        if text.hasPrefix("-") { sign = -1; text.removeFirst() } else if text.hasPrefix("+") { text.removeFirst() }
        guard text.hasPrefix("P") else { return nil }
        text.removeFirst()
        var total: Double = 0
        var number = ""
        var inTime = false
        for c in text {
            if c.isNumber { number.append(c); continue }
            if c == "T" { inTime = true; continue }
            guard let n = Double(number) else { return nil }
            number = ""
            switch (c, inTime) {
            case ("W", false): total += n * 7 * 86_400
            case ("D", false): total += n * 86_400
            case ("H", true): total += n * 3_600
            case ("M", true): total += n * 60
            case ("S", true): total += n
            default: return nil
            }
        }
        return number.isEmpty ? sign * total : nil
    }

    // ---------------- Etkinlikler

    struct Event {
        var uid = ""
        var summary = ""
        var start: Date?
        var allDay = false
        var zone = TimeZone.current
        var end: Date?
        var duration: TimeInterval?
        var location: String?
        var description: String?
        var url: String?
        var rule: [String: String]?
        var exdates: [Date] = []
        var recurrenceId: Date?
        var cancelled = false

        var length: TimeInterval {
            if let end, let start { return max(0, end.timeIntervalSince(start)) }
            if let duration { return max(0, duration) }
            return allDay ? 86_400 : 3_600
        }
    }

    static func events(_ ics: String, local: TimeZone) -> [Event] {
        var events: [Event] = []
        var current: Event?
        var depth = 0          // VEVENT icindeki VALARM gibi alt bilesenler
        for line in unfold(ics) {
            guard let p = property(line) else { continue }
            switch (p.name, p.value.uppercased()) {
            case ("BEGIN", "VEVENT"):
                current = Event(zone: local)
                depth = 0
                continue
            case ("END", "VEVENT"):
                if let e = current, e.start != nil { events.append(e) }
                current = nil
                continue
            case ("BEGIN", _) where current != nil:
                depth += 1
                continue
            case ("END", _) where current != nil:
                depth = max(0, depth - 1)
                continue
            default:
                break
            }
            guard var e = current, depth == 0 else { continue }
            switch p.name {
            case "UID": e.uid = p.value
            case "SUMMARY": e.summary = unescape(p.value)
            case "LOCATION": e.location = unescape(p.value)
            case "DESCRIPTION": e.description = unescape(p.value)
            case "URL": e.url = p.value
            case "STATUS": e.cancelled = p.value.uppercased() == "CANCELLED"
            case "DTSTART":
                if let d = date(p.value, params: p.params, local: local) { e.start = d.date; e.allDay = d.allDay; e.zone = d.zone }
            case "DTEND":
                e.end = date(p.value, params: p.params, local: local)?.date
            case "DURATION":
                e.duration = duration(p.value)
            case "RRULE":
                var rule: [String: String] = [:]
                for part in p.value.split(separator: ";") {
                    let kv = part.split(separator: "=", maxSplits: 1)
                    if kv.count == 2 { rule[kv[0].uppercased()] = String(kv[1]) }
                }
                e.rule = rule
            case "EXDATE":
                for v in p.value.split(separator: ",") {
                    if let d = date(String(v), params: p.params, local: local) { e.exdates.append(d.date) }
                }
            case "RECURRENCE-ID":
                e.recurrenceId = date(p.value, params: p.params, local: local)?.date
            default:
                break
            }
            current = e
        }
        return events
    }

    /// `from` ile `to` arasina dusen tekrarlar (baslangica gore sirali).
    public static func occurrences(_ ics: String, from: Date, to: Date, local: TimeZone = .current) -> [CalendarEntry] {
        let all = events(ics, local: local)
        // RECURRENCE-ID: tekrarlarin degistirilmis halleri; asil tekrar atlanir.
        var overridden = Set<String>()
        for e in all where e.recurrenceId != nil { overridden.insert("\(e.uid)|\(e.recurrenceId!.timeIntervalSince1970)") }

        var out: [CalendarEntry] = []
        for e in all where !e.cancelled {
            guard let start = e.start else { continue }
            let starts: [Date]
            if e.recurrenceId == nil, let rule = e.rule {
                starts = expand(rule, start: start, zone: e.zone, until: to)
                    .filter { s in !e.exdates.contains(s) && !overridden.contains("\(e.uid)|\(s.timeIntervalSince1970)") }
            } else {
                starts = [start]
            }
            for s in starts {
                let end = s.addingTimeInterval(e.length)
                guard s < to, end > from || (e.length == 0 && s >= from) else { continue }
                let text = [e.location, e.description, e.url].compactMap { $0 }.joined(separator: " ")
                let title = e.summary.trimmingCharacters(in: .whitespacesAndNewlines)
                out.append(CalendarEntry(title: title.isEmpty ? L.t("(No title)") : title,
                                         start: s, end: end, allDay: e.allDay,
                                         location: e.location.flatMap { $0.trimmingCharacters(in: .whitespaces).isEmpty ? nil : $0 },
                                         joinURL: joinLink(text)))
            }
        }
        return out.sorted { $0.start < $1.start }
    }

    /// Bir RRULE'un baslangiclari, `until`'e kadar (COUNT bastan sayilir).
    static func expand(_ rule: [String: String], start: Date, zone: TimeZone, until limit: Date) -> [Date] {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        calendar.firstWeekday = 2                                   // RRULE varsayilani WKST=MO
        let frequency = rule["FREQ"]?.uppercased() ?? ""
        let interval = max(1, Int(rule["INTERVAL"] ?? "") ?? 1)
        let count = Int(rule["COUNT"] ?? "")
        // UNTIL dahildir; yalniz tarihse o gunun sonuna kadar.
        let until = rule["UNTIL"].flatMap { date($0, params: [:], local: zone) }
            .map { $0.allDay ? $0.date.addingTimeInterval(86_400) : $0.date.addingTimeInterval(1) }
        let end = min(limit, until ?? limit)
        let weekdays: [Int] = (rule["BYDAY"] ?? "").split(separator: ",").compactMap { day in
            // "MO" ya da "1MO" gibi; haftalikta yalniz gun adi kullanilir.
            let code = String(day.suffix(2)).uppercased()
            return ["SU": 1, "MO": 2, "TU": 3, "WE": 4, "TH": 5, "FR": 6, "SA": 7][code]
        }

        var out: [Date] = []
        var produced = 0
        func take(_ d: Date) -> Bool {
            // COUNT dolduysa ya da sinir gecildiyse dur.
            if let count, produced >= count { return false }
            guard d < end else { return false }
            produced += 1
            out.append(d)
            return true
        }

        switch frequency {
        case "WEEKLY" where !weekdays.isEmpty:
            let time = calendar.dateComponents([.hour, .minute, .second], from: start)
            guard var weekStart = calendar.dateInterval(of: .weekOfYear, for: start)?.start else { return [] }
            for _ in 0..<2000 {
                var days: [Date] = []
                for weekday in weekdays {
                    // Haftanin o gunu, baslangic saatiyle.
                    let offset = (weekday - calendar.component(.weekday, from: weekStart) + 7) % 7
                    guard let day = calendar.date(byAdding: .day, value: offset, to: weekStart),
                          let at = calendar.date(bySettingHour: time.hour ?? 0, minute: time.minute ?? 0,
                                                 second: time.second ?? 0, of: day) else { continue }
                    if at >= start { days.append(at) }
                }
                for d in days.sorted() where !take(d) { return out }
                guard let next = calendar.date(byAdding: .weekOfYear, value: interval, to: weekStart), next < end else { break }
                weekStart = next
            }
        case "DAILY", "WEEKLY", "MONTHLY", "YEARLY":
            let units: [String: Calendar.Component] = ["DAILY": .day, "WEEKLY": .weekOfYear, "MONTHLY": .month, "YEARLY": .year]
            let unit = units[frequency] ?? .day
            let day = calendar.component(.day, from: start)
            for n in 0..<5000 {
                guard let d = calendar.date(byAdding: unit, value: n * interval, to: start) else { break }
                if d >= end { break }
                // 31'inde tekrarlayan aylik etkinlik 30 gunluk ayda olmaz (RFC 5545; Takvim de boyle).
                if (unit == .month || unit == .year) && calendar.component(.day, from: d) != day { continue }
                if !take(d) { break }
            }
        default:
            _ = take(start)
        }
        return out
    }

    /// Toplanti baglantisi (Windows'taki MeetingLinkRx ile ayni).
    public static func joinLink(_ text: String) -> String? {
        let pattern = #"https://(?:teams\.microsoft\.com|teams\.live\.com|meet\.google\.com|[\w.-]*zoom\.us|[\w.-]*webex\.com)/[^\s"'<>)\]]+"#
        guard let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
              let match = regex.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)),
              let range = Range(match.range, in: text) else { return nil }
        return String(text[range])
    }

    /// Gosterilecekler: bitmemis olanlar ve bugunun tum gun etkinlikleri (Windows: GetUpcomingAsync).
    public static func upcoming(_ entries: [CalendarEntry], now: Date, calendar: Calendar = .current) -> [CalendarEntry] {
        entries.filter { $0.end > now || ($0.allDay && calendar.isDate($0.start, inSameDayAs: now)) }
            .sorted { $0.start < $1.start }
    }

    /// webcal:// baglantilari duz https'tir.
    public static func feedURL(_ link: String) -> URL? {
        var text = link.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.lowercased().hasPrefix("webcal://") { text = "https://" + text.dropFirst("webcal://".count) }
        guard let url = URL(string: text), url.scheme == "https" || url.scheme == "http" else { return nil }
        return url
    }
}
