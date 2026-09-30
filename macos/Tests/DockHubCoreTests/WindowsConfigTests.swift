import Foundation
import XCTest
@testable import DockHubCore

/// tests/fixtures/config-windows.json: Windows'un yazdigi, her ayari kullanilan
/// bir config.json (Windows tarafi ConfigFixtureTests ile ayni dosyayi denetler).
/// Mac bu dosyayi okuyabilmeli ve hicbir ayari kaybetmeden geri yazabilmeli.
final class WindowsConfigTests: XCTestCase {
    private static let fixture: URL = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent()      // DockHubCoreTests
        .deletingLastPathComponent()      // Tests
        .deletingLastPathComponent()      // macos
        .deletingLastPathComponent()      // depo koku
        .appendingPathComponent("tests/fixtures/config-windows.json")

    private func fixtureData() throws -> Data { try Data(contentsOf: Self.fixture) }

    private func object(_ data: Data) throws -> [String: Any] {
        try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])
    }

    func testWindowsDosyasiOkunur() throws {
        let c = try JSONStore.decoder.decode(AppConfig.self, from: fixtureData())

        XCTAssertEqual(c.edge, .left)
        XCTAssertEqual(c.backdrop, .transparent)
        XCTAssertEqual(c.widgetStyle, .seamless)
        XCTAssertEqual(c.combineButtons, .never)
        XCTAssertEqual(c.runningIndicator, .dots)
        XCTAssertEqual(c.microphoneIcon, .always)
        XCTAssertEqual(c.searchButtonAction, .launcher)
        XCTAssertEqual(c.motion, .reduced)
        XCTAssertEqual(c.language, .turkish)
        XCTAssertEqual(c.textScale, 1.25)
        XCTAssertEqual(c.hotkeys["launcher"], "Alt+Space")
        XCTAssertEqual(c.hotkeys["edit-dock"], "")
        XCTAssertEqual(c.syncFolder, #"C:\Users\me\OneDrive\DockHub"#)
        XCTAssertEqual(c.syncAppliedAt, "2026-09-30T10:15:00Z")
        XCTAssertTrue(c.welcomeShown)
        XCTAssertTrue(c.debugLogging)

        XCTAssertTrue(c.topBar.enabled)
        XCTAssertEqual(c.topBar.edge, .right)
        XCTAssertEqual(c.topBar.size, .medium)
        XCTAssertEqual(c.topBar.layout, .floating)
        XCTAssertEqual(c.topBar.backdrop, .acrylic)
        XCTAssertFalse(c.topBar.showClock)

        XCTAssertEqual(c.profiles.count, 2)
        XCTAssertEqual(c.profiles[0].autoDisplayCount, 2)
        XCTAssertEqual(c.profiles[0].autoApp, "steam")
        XCTAssertEqual(c.profiles[0].autoTimeFrom, "09:00")
        XCTAssertTrue(c.profiles[0].autoWeekdaysOnly)
        XCTAssertNil(c.profiles[1].items)
        XCTAssertNil(c.profiles[1].autoApp)
        XCTAssertEqual(c.activeProfileId, "p6e7f8a9b0")

        XCTAssertEqual(c.customPresets.first?.widgets.map(\.widget), ["clock", "weather"])
        XCTAssertNil(c.customPresets.first?.widgets[1].variant)

        XCTAssertEqual(c.items.count, 5)
        XCTAssertEqual(c.items[1].display, #"\\.\DISPLAY2"#)
        XCTAssertTrue(c.items[1].collapseWhenIdle)
        XCTAssertTrue(c.items[1].pinnedEnd)
        XCTAssertEqual(c.items[2].surface, DockItem.barSurface)
        XCTAssertEqual(c.items[4].children?.first?.name, "Claude")

        XCTAssertTrue(c.extras.isEmpty, "Windows'un her anahtari taninmali: \(c.extras.keys.sorted())")
        XCTAssertTrue(c.items.allSatisfy { $0.extras.isEmpty })
        XCTAssertTrue(c.topBar.extras.isEmpty)
        XCTAssertTrue(c.profiles.allSatisfy { $0.extras.isEmpty })
    }

    /// Okuyup yazmak hicbir seyi degistirmemeli (anahtar sirasi haric).
    func testWindowsDosyasiAynenGeriYazilir() throws {
        let data = try fixtureData()
        let c = try JSONStore.decoder.decode(AppConfig.self, from: data)
        let yazilan = try object(try JSONStore.encoder.encode(c))
        let beklenen = try object(data)

        XCTAssertEqual(Set(yazilan.keys), Set(beklenen.keys))
        for (key, value) in beklenen {
            XCTAssertEqual(yazilan[key].map { NSArray(object: $0) }, NSArray(object: value), key)
        }
    }

    /// Daha yeni bir surumun (ya da Windows'un ileride ekleyecegi) anahtarlar
    /// her duzeyde korunur.
    func testBilinmeyenAnahtarlarKorunur() throws {
        let json = #"""
        {"version":2,"futureSetting":{"a":[1,"b"]},"edge":"Top",
         "topBar":{"enabled":true,"futureBar":"x"},
         "profiles":[{"id":"p","name":"P","futureRule":true}],
         "customPresets":[{"id":"c","name":"C","widgets":[],"futurePreset":3}],
         "items":[{"id":"w","kind":"Widget","widget":"clock","futureItem":"y",
                   "children":null}]}
        """#
        let c = try JSONStore.decoder.decode(AppConfig.self, from: Data(json.utf8))
        XCTAssertEqual(c.extras["futureSetting"], .object(["a": .array([.number(1), .string("b")])]))

        var degisik = c
        degisik.size = .large
        let o = try object(try JSONStore.encoder.encode(degisik))

        XCTAssertEqual(o["size"] as? String, "Large")
        XCTAssertEqual(o["edge"] as? String, "Top")
        XCTAssertNotNil(o["futureSetting"])
        XCTAssertEqual((o["topBar"] as? [String: Any])?["futureBar"] as? String, "x")
        XCTAssertEqual((o["profiles"] as? [[String: Any]])?.first?["futureRule"] as? Bool, true)
        XCTAssertEqual((o["customPresets"] as? [[String: Any]])?.first?["futurePreset"] as? Int, 3)
        XCTAssertEqual((o["items"] as? [[String: Any]])?.first?["futureItem"] as? String, "y")
    }

    /// Tipi uymayan ya da bu surumun bilmedigi deger yalniz o alani varsayilana
    /// dusurur; dosyanin geri kalani okunur.
    func testTipiUymayanDegerVarsayilanOlur() throws {
        let json = #"""
        {"edge":"Diagonal","size":"Large","backdrop":42,"tintOpacity":"a lot",
         "topBar":{"edge":"Top","size":"Huge"},
         "items":[{"id":"x","kind":"Hologram"},{"id":"s","kind":"Separator"}]}
        """#
        let c = try JSONStore.decoder.decode(AppConfig.self, from: Data(json.utf8))
        XCTAssertEqual(c.edge, .bottom)
        XCTAssertEqual(c.size, .large)
        XCTAssertEqual(c.backdrop, .blur)
        XCTAssertEqual(c.tintOpacity, 0.55)
        XCTAssertEqual(c.topBar.size, .small)
        XCTAssertEqual(c.items.map(\.id), ["x", "s"])
        XCTAssertEqual(c.items[1].kind, .separator)
    }

    /// Yeni alanlarin varsayilanlari C# AppConfig.cs / TopBarSettings.cs ile ayni.
    func testYeniVarsayilanlarWindowsIleAyni() {
        let c = AppConfig()
        XCTAssertEqual(c.combineButtons, .always)
        XCTAssertEqual(c.widgetStyle, .cards)
        XCTAssertEqual(c.runningIndicator, .line)
        XCTAssertEqual(c.microphoneIcon, .whenInUse)
        XCTAssertEqual(c.searchButtonAction, .windowsSearch)
        XCTAssertEqual(c.motion, .system)
        XCTAssertEqual(c.language, .system)
        XCTAssertEqual(c.textScale, 0)
        XCTAssertTrue(c.alignWidgetWidths)
        XCTAssertTrue(c.previewPeek)
        XCTAssertTrue(c.checkForUpdates)
        XCTAssertFalse(c.includePrereleases)
        XCTAssertTrue(c.winNumberHotkeys)
        XCTAssertTrue(c.launcherFileSearch)
        XCTAssertFalse(c.smartAutoHide)
        XCTAssertFalse(c.topBar.enabled)
        XCTAssertEqual(c.topBar.edge, .top)
        XCTAssertEqual(c.topBar.size, .small)
        XCTAssertEqual(c.topBar.layout, .attached)
        XCTAssertNil(c.topBar.backdrop)
        XCTAssertTrue(c.topBar.showClock)
    }

    /// Yazma kurallari: nitelik yoksa null yazilir, WhenWritingNull/Default
    /// alanlari varsayilanda hic yazilmaz.
    func testVarsayilanYazimKurallari() throws {
        let o = try object(try JSONStore.encoder.encode(AppConfig()))
        for key in ["syncFolder", "syncDeviceId", "syncAppliedAt"] {
            XCTAssertTrue(o[key] is NSNull, "\(key) null yazilmali")
        }
        for key in ["welcomeShown", "debugLogging", "activeProfileId"] {
            XCTAssertNil(o[key], "\(key) yazilmamali")
        }
        let bar = try XCTUnwrap(o["topBar"] as? [String: Any])
        XCTAssertTrue(bar["backdrop"] is NSNull, "topBar.backdrop null yazilmali")

        let item = DockItem.widget("clock")
        let io = try object(try JSONStore.encoder.encode(item))
        for key in ["display", "surface", "collapseWhenIdle", "pinnedEnd"] {
            XCTAssertNil(io[key], "\(key) yazilmamali")
        }
        XCTAssertTrue(DockItem.widget("media").collapseWhenIdle, "C# CollapseByDefault")
    }

    /// C#: TopBarSettings.EdgeFor. Dock'la ayni kenar istenirse karsi kenar.
    func testBarKenari() {
        XCTAssertEqual(TopBarSettings.edge(for: .top, dockEdge: .bottom), .top)
        XCTAssertEqual(TopBarSettings.edge(for: .top, dockEdge: .top), .bottom)
        XCTAssertEqual(TopBarSettings.edge(for: .left, dockEdge: .left), .right)
        XCTAssertEqual(TopBarSettings.edge(for: .right, dockEdge: .right), .left)
    }
}
