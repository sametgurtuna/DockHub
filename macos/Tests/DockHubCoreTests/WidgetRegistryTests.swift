import Foundation
import XCTest
@testable import DockHubCore

/// Widget kimliklerinin Windows kayit defteriyle ayni oldugunu ve eski macOS
/// kimliklerinin tasindigini sinar (T16-WIDGET-KIMLIK, docs/WIDGET-SEMASI.md).
final class WidgetRegistryTests: XCTestCase {

    /// Windows v0.6.1 Widgets/WidgetRegistry.cs'den aynen: kimlik -> varyantlar (sirali).
    private let windows: [(String, [String])] = [
        ("clock", ["analog", "digital", "calendar"]),
        ("world-clock", ["single", "multi"]),
        ("stopwatch", ["default"]),
        ("focus", ["default"]),
        ("countdown", ["default"]),
        ("alarm", ["default"]),
        ("time-progress", ["bar", "ring"]),
        ("hydration", ["timer", "progress"]),
        ("reminders", ["list", "next", "count"]),
        ("notes", ["sticky"]),
        ("media", ["full", "compact", "mini"]),
        ("system", ["numbers", "rings", "bars"]),
        ("network", ["numbers", "chart"]),
        ("status", ["rings", "percent", "icons"]),
        ("weather", ["current", "conditions", "hourly"]),
        ("ai-usage", ["numbers", "rings", "bars"]),
        ("audio", ["compact", "slider"]),
        ("recycle-bin", ["icon", "details"]),
        ("battery-devices", ["single", "multi"]),
    ]

    /// Henuz cizilmeyen Windows varyantlari (WIDGET-SEMASI.md "eksik varyantlar").
    private let eksik: Set<String> = [
        "clock/calendar", "system/bars", "status/icons", "weather/hourly", "ai-usage/numbers",
    ]

    func testKimliklerVeVaryantlarWindowsIleAyni() throws {
        for (id, winVaryantlar) in windows {
            let tanim = try XCTUnwrap(WidgetRegistry.find(id), "\(id) kayit defterinde yok")
            let bizim = tanim.variants.map(\.id)
            let beklenen = winVaryantlar.filter { !eksik.contains("\(id)/\($0)") }
            XCTAssertEqual(bizim, beklenen, "\(id) varyantlari veya sirasi Windows'tan farkli")
        }
    }

    /// Gorunen adlar Windows v0.6.1'deki Ingilizce metinlerle ayni (d-arayuz-dili):
    /// kimlik -> (ad, kategori, [varyant adlari], eksik varyantlar haric).
    func testAdlarWindowsIleAyni() throws {
        let beklenen: [String: (String, String, [String])] = [
            "clock": ("Clock", "Clocks", ["Analog", "Digital"]),
            "world-clock": ("World clock", "Clocks", ["Single city", "Multiple cities"]),
            "stopwatch": ("Stopwatch", "Clocks", ["Stopwatch"]),
            "focus": ("Focus timer", "Clocks", ["Focus timer"]),
            "countdown": ("Countdown", "Clocks", ["Countdown"]),
            "alarm": ("Alarm", "Clocks", ["Alarm"]),
            "time-progress": ("Time progress", "Clocks", ["Bar", "Ring"]),
            "hydration": ("Hydration", "Reminders", ["Timer", "Daily goal"]),
            "reminders": ("Reminders", "Reminders", ["List", "Next", "Count"]),
            "notes": ("Sticky note", "Sticky notes", ["Sticky note"]),
            "media": ("Now playing", "Media", ["Full", "Compact", "Mini"]),
            "system": ("CPU and memory", "System", ["Numbers", "Rings"]),
            "network": ("Network speed", "System", ["Numbers only", "Chart"]),
            "status": ("Status", "System", ["Rings", "Percentage ring"]),
            "weather": ("Weather", "Weather", ["Current", "Conditions"]),
            "ai-usage": ("AI usage", "AI", ["Rings", "Bars"]),
            "audio": ("Audio device", "Media", ["Compact", "Slider"]),
            "recycle-bin": ("Recycle bin", "System", ["Icon only", "Detailed"]),
            "battery-devices": ("Device batteries", "System", ["Single device", "Multiple devices"]),
        ]
        for (id, (ad, kategori, varyantlar)) in beklenen {
            let t = try XCTUnwrap(WidgetRegistry.find(id))
            XCTAssertEqual(t.name, ad, id)
            XCTAssertEqual(t.category, kategori, id)
            XCTAssertEqual(t.variants.map(\.name), varyantlar, id)
        }
    }

    /// Windows'un kayit defteri kaynagindan okunur (src/CustomDock/Widgets/WidgetRegistry.cs):
    /// her iki tarafta olan widget'in adi, kategorisi ve Mac'teki varyantlari Windows'takilerle
    /// ayni sirada ve adla bulunmali. Windows'a yeni widget ya da varyant eklenince Mac kendiliginden
    /// denetlenir; sabit liste eskimez.
    func testWindowsKaynagiylaAyni() throws {
        let source = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("src/CustomDock/Widgets/WidgetRegistry.cs")
        let text = try String(contentsOf: source, encoding: .utf8)
        let categories = ["Clocks": "Clocks", "Reminders": "Reminders", "Notes": "Sticky notes", "Media": "Media",
                          "System": "System", "Weather": "Weather", "AI": "AI", "Productivity": "Productivity"]
        let blocks = text.components(separatedBy: "new()").dropFirst()
        var windowsIds = Set<String>()
        for block in blocks {
            guard let id = firstMatch(#"Id = "([^"]+)""#, in: block) else { continue }
            windowsIds.insert(id)
            guard let mac = WidgetRegistry.find(id) else { continue }
            XCTAssertEqual(mac.name, firstMatch(#"Name = "([^"]+)""#, in: block), id)
            let category = firstMatch(#"Category = WidgetCategories\.(\w+)"#, in: block).flatMap { categories[$0] }
            XCTAssertEqual(mac.category, category, id)
            let variants = allMatches(#"new WidgetVariant\("([^"]+)", "([^"]+)""#, in: block)
            let windowsOrder = variants.map(\.0)
            let macOrder = mac.variants.map(\.id)
            XCTAssertEqual(macOrder, windowsOrder.filter { macOrder.contains($0) }, "\(id): Mac varyantlari Windows sirasinda olmali")
            XCTAssertTrue(Set(macOrder).isSubset(of: windowsOrder), "\(id): Windows'ta olmayan varyant")
            for v in mac.variants {
                XCTAssertEqual(v.name, variants.first { $0.0 == v.id }?.1, "\(id)/\(v.id)")
            }
        }
        XCTAssertGreaterThan(windowsIds.count, 30, "Windows kayit defteri okunamadi")
        let macOnly = Set(WidgetRegistry.all.map(\.id)).subtracting(windowsIds)
        XCTAssertEqual(macOnly, ["battery", "shortcut", "airdrop"], "Mac'e ozgu widget'lar")
    }

    private func firstMatch(_ pattern: String, in text: String) -> String? {
        allMatches(pattern, in: text).first?.0
    }

    private func allMatches(_ pattern: String, in text: String) -> [(String, String)] {
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return [] }
        return regex.matches(in: text, range: NSRange(text.startIndex..., in: text)).map { m in
            let one = Range(m.range(at: 1), in: text).map { String(text[$0]) } ?? ""
            let two = m.numberOfRanges > 2 ? (Range(m.range(at: 2), in: text).map { String(text[$0]) } ?? "") : ""
            return (one, two)
        }
    }

    /// Varsayilan varyant Windows'taki ilk varyantla ayni (eksik olan haric).
    func testVarsayilanVaryantWindowsIleAyni() {
        for (id, v) in windows where !eksik.contains("\(id)/\(v[0])") {
            XCTAssertEqual(WidgetRegistry.find(id)?.defaultVariant, v[0], id)
        }
    }

    func testEskiKimliklerTasinir() {
        let eski: [DockItem] = [
            DockItem(kind: .widget, widget: "sticky-note", variant: "single"),
            DockItem(kind: .widget, widget: "now-playing", variant: "compact"),
            DockItem(kind: .widget, widget: "trash", variant: "details"),
            DockItem(kind: .widget, widget: "device-battery", variant: "multi"),
            DockItem(kind: .widget, widget: "stopwatch", variant: "single"),
            DockItem(kind: .widget, widget: "hydration", variant: "goal"),
            DockItem(kind: .widget, widget: "network", variant: "graph"),
            DockItem(kind: .widget, widget: "weather", variant: "condition"),
            .group("Klasor", children: [DockItem(kind: .widget, widget: "trash", variant: "icon")]),
        ]
        let (yeni, degisti) = WidgetRegistry.migrateLegacyIds(eski)
        XCTAssertTrue(degisti)
        XCTAssertEqual(yeni.map(\.widget), ["notes", "media", "recycle-bin", "battery-devices",
                                            "stopwatch", "hydration", "network", "weather", nil])
        XCTAssertEqual(yeni.map(\.variant), ["sticky", "compact", "details", "multi",
                                             "default", "progress", "chart", "conditions", nil])
        XCTAssertEqual(yeni[8].children?.first?.widget, "recycle-bin", "klasor ici de tasinmali")

        let (ikinci, yineDegisti) = WidgetRegistry.migrateLegacyIds(yeni)
        XCTAssertFalse(yineDegisti, "ikinci tasima hicbir seyi degistirmemeli")
        XCTAssertEqual(ikinci.map(\.variant), yeni.map(\.variant))
    }

    /// Windows'un yazdigi kimlik ve varyantlara dokunulmaz; world-clock/single ve
    /// battery-devices/single Windows'ta da gecerli.
    func testWindowsConfigineDokunulmaz() {
        let win = windows.flatMap { id, vs in vs.map { DockItem(kind: .widget, widget: id, variant: $0) } }
        XCTAssertFalse(WidgetRegistry.migrateLegacyIds(win).changed)
    }

    /// Varsayilan uygulamalarin eski Turkce adlari yalniz ayni yol + ayni adla
    /// eslesirse silinir; kullanicinin verdigi ad ve baska yoldaki ayni ad korunur.
    func testEskiVarsayilanAdlarTemizlenir() {
        let items: [DockItem] = [
            .app("/System/Applications/Notes.app", name: "Notlar"),
            .app("/System/Applications/Music.app", name: "Benim müziğim"),
            .app("/Applications/Notlar.app", name: "Notlar"),
            .group("K", children: [.app("/System/Applications/System Settings.app", name: "Sistem Ayarları")]),
        ]
        let (yeni, degisti) = DefaultItems.migrateLegacyNames(items)
        XCTAssertTrue(degisti)
        XCTAssertNil(yeni[0].name)
        XCTAssertEqual(yeni[1].name, "Benim müziğim")
        XCTAssertEqual(yeni[2].name, "Notlar")
        XCTAssertNil(yeni[3].children?.first?.name)
        XCTAssertFalse(DefaultItems.migrateLegacyNames(yeni).changed)
        XCTAssertTrue(DefaultItems.build { _ in true }.allSatisfy { $0.name == nil }, "varsayilanlar ad yazmaz")
    }

    /// ConfigService eski kimlikli dosyayi yuklerken tasir ve bir kez yazar.
    func testConfigServiceTasiyipBirKezYazar() throws {
        let klasor = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("dockhub-tasima-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: klasor) }
        let url = klasor.appendingPathComponent("config.json")
        var c = AppConfig()
        c.items = [DockItem(kind: .widget, widget: "now-playing", variant: "mini")]
        try JSONStore.save(c, to: url)

        let ilk = ConfigService(url: url)
        XCTAssertEqual(ilk.config.items.first?.widget, "media")
        let diskte = try String(contentsOf: url, encoding: .utf8)
        XCTAssertTrue(diskte.contains("\"media\""))
        XCTAssertFalse(diskte.contains("now-playing"))

        let once = try Data(contentsOf: url)
        _ = ConfigService(url: url)
        XCTAssertEqual(try Data(contentsOf: url), once, "ikinci yukleme dosyayi degistirmemeli")
    }
}
