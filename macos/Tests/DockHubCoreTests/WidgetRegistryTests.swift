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
