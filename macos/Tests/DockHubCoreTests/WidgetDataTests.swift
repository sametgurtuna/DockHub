import Foundation
import XCTest
@testable import DockHubCore

/// iCal okuyucu (Windows: CalendarServiceTests, Ical.Net).
final class ICalendarTests: XCTestCase {
    private let utc = TimeZone(identifier: "UTC")!

    private func date(_ text: String) -> Date { ISO8601DateFormatter().date(from: text)! }

    private func ics(_ events: String) -> String {
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + events + "END:VCALENDAR\r\n"
    }

    func testTekEtkinlikVeKatlanmisSatir() {
        let text = ics("""
        BEGIN:VEVENT\r
        UID:1\r
        SUMMARY:Weekly sync\\, team\r
        DTSTART:20261002T090000Z\r
        DTEND:20261002T093000Z\r
        LOCATION:Room 4\r
        DESCRIPTION:Join https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc\r
         /0?context=x\r
        BEGIN:VALARM\r
        TRIGGER:-PT15M\r
        DESCRIPTION:Reminder\r
        END:VALARM\r
        END:VEVENT\r

        """)
        let all = ICalendar.occurrences(text, from: date("2026-10-01T00:00:00Z"), to: date("2026-10-09T00:00:00Z"), local: utc)
        XCTAssertEqual(all.count, 1)
        XCTAssertEqual(all[0].title, "Weekly sync, team")
        XCTAssertEqual(all[0].start, date("2026-10-02T09:00:00Z"))
        XCTAssertEqual(all[0].end, date("2026-10-02T09:30:00Z"))
        XCTAssertEqual(all[0].location, "Room 4")
        XCTAssertEqual(all[0].joinURL, "https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc/0?context=x",
                       "katlanmis satir birlesir, VALARM aciklamasi etkinligi ezmez")
        XCTAssertFalse(all[0].allDay)
    }

    func testTumGunVeSure() {
        let text = ics("""
        BEGIN:VEVENT\r
        UID:2\r
        SUMMARY:Holiday\r
        DTSTART;VALUE=DATE:20261029\r
        END:VEVENT\r
        BEGIN:VEVENT\r
        UID:3\r
        SUMMARY:Call\r
        DTSTART;TZID=Turkey Standard Time:20261005T140000\r
        DURATION:PT45M\r
        END:VEVENT\r

        """)
        let all = ICalendar.occurrences(text, from: date("2026-10-01T00:00:00Z"), to: date("2026-11-01T00:00:00Z"), local: utc)
        XCTAssertEqual(all.map(\.title), ["Call", "Holiday"])
        XCTAssertEqual(all[0].start, date("2026-10-05T11:00:00Z"), "Windows saat dilimi adi (Istanbul, UTC+3)")
        XCTAssertEqual(all[0].end.timeIntervalSince(all[0].start), 45 * 60)
        XCTAssertTrue(all[1].allDay)
        XCTAssertEqual(all[1].end.timeIntervalSince(all[1].start), 86_400)
    }

    func testHaftalikTekrarIstisnaVeDegisiklik() {
        // Pazartesi ve Carsamba, 4 kez; 7 Ekim atlaniyor, 12 Ekim bir saat kaydirilmis.
        let text = ics("""
        BEGIN:VEVENT\r
        UID:w\r
        SUMMARY:Standup\r
        DTSTART:20260928T080000Z\r
        DTEND:20260928T081500Z\r
        RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=4\r
        EXDATE:20261007T080000Z\r
        END:VEVENT\r
        BEGIN:VEVENT\r
        UID:w\r
        RECURRENCE-ID:20261005T080000Z\r
        SUMMARY:Standup (moved)\r
        DTSTART:20261005T090000Z\r
        DTEND:20261005T091500Z\r
        END:VEVENT\r

        """)
        let all = ICalendar.occurrences(text, from: date("2026-09-01T00:00:00Z"), to: date("2026-12-01T00:00:00Z"), local: utc)
        XCTAssertEqual(all.map(\.start), [date("2026-09-28T08:00:00Z"), date("2026-09-30T08:00:00Z"),
                                          date("2026-10-05T09:00:00Z")])
        XCTAssertEqual(all[2].title, "Standup (moved)")
    }

    func testGunlukUntilVeIptal() {
        let text = ics("""
        BEGIN:VEVENT\r
        UID:d\r
        SUMMARY:Gym\r
        DTSTART:20261001T180000Z\r
        RRULE:FREQ=DAILY;INTERVAL=2;UNTIL=20261007\r
        END:VEVENT\r
        BEGIN:VEVENT\r
        UID:c\r
        SUMMARY:Cancelled\r
        STATUS:CANCELLED\r
        DTSTART:20261002T100000Z\r
        END:VEVENT\r

        """)
        let all = ICalendar.occurrences(text, from: date("2026-10-01T00:00:00Z"), to: date("2026-10-31T00:00:00Z"), local: utc)
        XCTAssertEqual(all.map(\.start), [date("2026-10-01T18:00:00Z"), date("2026-10-03T18:00:00Z"),
                                          date("2026-10-05T18:00:00Z"), date("2026-10-07T18:00:00Z")],
                       "yalniz tarih olan UNTIL o gunu kapsar")
        XCTAssertEqual(all[0].end.timeIntervalSince(all[0].start), 3600, "bitisi olmayan saatli etkinlik bir saat")
    }

    func testAylikOtuzBir() {
        let text = ics("""
        BEGIN:VEVENT\r
        UID:m\r
        SUMMARY:Rent\r
        DTSTART:20260131T090000Z\r
        RRULE:FREQ=MONTHLY;COUNT=4\r
        END:VEVENT\r

        """)
        let all = ICalendar.occurrences(text, from: date("2026-01-01T00:00:00Z"), to: date("2026-12-31T00:00:00Z"), local: utc)
        XCTAssertEqual(all.map(\.start), [date("2026-01-31T09:00:00Z"), date("2026-03-31T09:00:00Z"),
                                          date("2026-05-31T09:00:00Z"), date("2026-07-31T09:00:00Z")],
                       "31'i olmayan aylar atlanir")
    }

    func testYardimcilar() {
        XCTAssertEqual(ICalendar.duration("PT1H30M"), 5400)
        XCTAssertEqual(ICalendar.duration("P1D"), 86_400)
        XCTAssertEqual(ICalendar.duration("P2W"), 14 * 86_400)
        XCTAssertEqual(ICalendar.duration("-PT15M"), -900)
        XCTAssertNil(ICalendar.duration("1H"))
        XCTAssertEqual(ICalendar.feedURL("webcal://calendar.google.com/x.ics")?.absoluteString, "https://calendar.google.com/x.ics")
        XCTAssertNil(ICalendar.feedURL("file:///etc/passwd"))
        XCTAssertEqual(ICalendar.joinLink("see https://us02web.zoom.us/j/123?pwd=a). ok"), "https://us02web.zoom.us/j/123?pwd=a")
        XCTAssertNil(ICalendar.joinLink("https://example.com/meet"))
    }
}

/// Pano gecmisi, hisse, doviz, ping ve yapilacaklar verisi.
final class WidgetDataTests: XCTestCase {
    func testPanoGecmisi() {
        var h = ClipboardHistory()
        h.add(ClipboardEntry(text: "one"))
        h.add(ClipboardEntry(text: "two"))
        h.add(ClipboardEntry(text: "   "))
        XCTAssertEqual(h.entries.map(\.text), ["two", "one"])
        h.add(ClipboardEntry(text: "one"))
        XCTAssertEqual(h.entries.map(\.text), ["one", "two"], "tekrar kopyalanan one gelir")

        h.togglePin(h.entries[1].id)                     // "two" sabitlenir
        XCTAssertEqual(h.entries.map(\.text), ["two", "one"])
        for i in 0..<30 { h.add(ClipboardEntry(text: "item \(i)")) }
        XCTAssertEqual(h.entries.count, ClipboardHistory.capacity)
        XCTAssertEqual(h.entries.first?.text, "two", "sabitlenen dusmez, basta kalir")
        XCTAssertEqual(h.entries[1].text, "item 29")
        h.clear()
        XCTAssertEqual(h.entries.map(\.text), ["two"])

        XCTAssertFalse(ClipboardHistory.shouldRecord(types: ["public.utf8-plain-text", "org.nspasteboard.ConcealedType"]))
        XCTAssertTrue(ClipboardHistory.shouldRecord(types: ["public.utf8-plain-text"]))
    }

    func testHisse() throws {
        XCTAssertEqual(StockData.splitSymbols("aapl, MSFT; ^spx aapl bad!"), ["AAPL", "MSFT", "^SPX"])
        XCTAssertEqual(StockData.stooqSymbol("AAPL"), "aapl.us")
        XCTAssertEqual(StockData.stooqSymbol("thyao.tr"), "thyao.tr")
        XCTAssertNil(StockData.stooqSymbol("a b"))
        let csv = "Date,Open,High,Low,Close,Volume\n2026-09-28,1,1,1,210.5,100\n2026-09-29,1,1,1,212.0,100\n"
        let q = try XCTUnwrap(StockData.parse("aapl", csv: csv))
        XCTAssertEqual(q.symbol, "AAPL")
        XCTAssertEqual(q.close, 212.0)
        XCTAssertEqual(q.previousClose, 210.5)
        XCTAssertEqual(q.history, [210.5, 212.0])
        XCTAssertEqual(try XCTUnwrap(q.changePercent), 0.7126, accuracy: 0.001)
        XCTAssertNil(StockData.parse("x", csv: "No data"))
    }

    func testDoviz() throws {
        XCTAssertEqual(CurrencyData.targets("try, EUR; btc usd try", base: "USD"), ["TRY", "EUR", "BTC"])
        let fiat = Data(#"{"rates":{"2026-09-29":{"TRY":41.5,"EUR":0.85},"2026-09-28":{"TRY":41.0,"EUR":0.86}}}"#.utf8)
        let quotes = CurrencyData.fiatQuotes(base: "USD", targets: ["TRY", "EUR", "GBP"], json: fiat)
        XCTAssertEqual(quotes.map(\.target), ["TRY", "EUR"])
        XCTAssertEqual(quotes[0].rate, 41.5)
        XCTAssertEqual(quotes[0].previous, 41.0)
        XCTAssertEqual(quotes[0].history, [41.0, 41.5])

        let chart = Data(#"{"prices":[[1790000000000,60000],[1790050000000,61000],[1790086400000,63000]]}"#.utf8)
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = TimeZone(identifier: "UTC")!
        let coin = try XCTUnwrap(CurrencyData.cryptoQuote(coin: "btc", currency: "usd", json: chart, calendar: utc))
        XCTAssertEqual(coin.symbol, "BTC")
        XCTAssertEqual(coin.rate, 63000)
        XCTAssertEqual(coin.previous, 60000, "24 saat onceki fiyat")
        XCTAssertTrue(coin.isCrypto)
        XCTAssertEqual(CurrencyData.coinGeckoURL(coin: "ETH", currency: "EUR")?.absoluteString,
                       "https://api.coingecko.com/api/v3/coins/ethereum/market_chart?vs_currency=eur&days=14")
    }

    func testPing() {
        var s = PingStats()
        let replies: [Double?] = [20, nil, 40, 30]
        replies.forEach { s.add($0) }
        XCTAssertEqual(s.average, 30)
        XCTAssertEqual(s.loss, 25)
        XCTAssertEqual(s.max, 40)
        XCTAssertEqual(s.history, [20, 40, 40, 30])
        XCTAssertFalse(s.lastLost)
        for _ in 0..<40 { s.add(nil) }
        XCTAssertEqual(s.samples.count, PingStats.window)
        XCTAssertTrue(s.lastLost)
        XCTAssertNil(s.latest)

        XCTAssertEqual(PingStats.parse("64 bytes from 1.1.1.1: icmp_seq=0 ttl=57 time=12.345 ms"), 12.345)
        XCTAssertNil(PingStats.parse("Request timeout for icmp_seq 0"))
        XCTAssertEqual(PingStats.quality(20), .good)
        XCTAssertEqual(PingStats.quality(100), .fair)
        XCTAssertEqual(PingStats.quality(nil), .poor)
        XCTAssertEqual(PingStats.target(" example.com "), "example.com")
        XCTAssertEqual(PingStats.target("-f 1.1.1.1"), PingStats.defaultTarget, "secenek gibi gorunen hedef reddedilir")
        XCTAssertEqual(PingStats.target("a;rm"), PingStats.defaultTarget)
        XCTAssertEqual(PingStats.target(nil), PingStats.defaultTarget)
    }

    func testYapilacaklar() throws {
        let json = Data(#"{"results":[{"id":"1","content":"Later","checked":false,"due":{"date":"2026-10-03"}},{"id":"2","content":"Done","checked":true},{"id":"3","content":"Now","due":{"date":"2026-10-01","datetime":"2026-10-01T09:00:00Z"}},{"id":"4","content":"No date"}],"next_cursor":""}"#.utf8)
        let page = TodoistData.page(json)
        XCTAssertNil(page.cursor)
        XCTAssertEqual(TodoistData.sorted(page.tasks).map(\.text), ["Now", "Later", "No date"])

        // Windows'un yazdigi yerel liste okunur.
        let windows = Data(#"{"items":[{"id":"ab","text":"Buy milk","createdAt":"2026-09-30T10:00:00.1234567+03:00"}]}"#.utf8)
        let list = try JSONDecoder().decode(LocalTodoList.self, from: windows)
        XCTAssertEqual(list.items.map(\.text), ["Buy milk"])
    }

    func testKlasorYigini() {
        let now = Date()
        let files = [
            StackFile(url: URL(fileURLWithPath: "/d/b.txt"), name: "b.txt", modified: now, isDirectory: false),
            StackFile(url: URL(fileURLWithPath: "/d/.DS_Store"), name: ".DS_Store", modified: now, isDirectory: false),
            StackFile(url: URL(fileURLWithPath: "/d/a10.txt"), name: "a10.txt", modified: now.addingTimeInterval(-60), isDirectory: false),
            StackFile(url: URL(fileURLWithPath: "/d/a2.txt"), name: "a2.txt", modified: now.addingTimeInterval(-30), isDirectory: false),
        ]
        XCTAssertEqual(FolderStack.sorted(files, by: "date").map(\.name), ["b.txt", "a2.txt", "a10.txt"])
        XCTAssertEqual(FolderStack.sorted(files, by: "name").map(\.name), ["a2.txt", "a10.txt", "b.txt"])
        XCTAssertEqual(FolderStack.sorted(files, by: "date", limit: 1).count, 1)
    }
}
