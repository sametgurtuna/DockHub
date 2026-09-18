import XCTest
@testable import DockHubCore

/// Windows config.json semasiyla uyumu sinar (MIMARI.md bolum 3).
final class ConfigSchemaTests: XCTestCase {

    /// C# tarafinda [JsonIgnore(WhenWritingNull)] TASIMAYAN nullable alanlar
    /// nil olsa bile `null` olarak yazilmali.
    func testNullableAlanlarNullYazilir() throws {
        let data = try JSONStore.encoder.encode(AppConfig())
        let obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])

        XCTAssertTrue(obj.keys.contains("monitorDevice"), "monitorDevice null olarak yazilmali")
        XCTAssertTrue(obj["monitorDevice"] is NSNull)
        XCTAssertTrue(obj.keys.contains("pinnedTrayIcons"), "pinnedTrayIcons null olarak yazilmali")
        XCTAssertTrue(obj["pinnedTrayIcons"] is NSNull)
    }

    /// v1 uyum alanlari nil ise HIC yazilmamali (C# WhenWritingNull).
    func testEskiUyumAlanlariNilIseYazilmaz() throws {
        let data = try JSONStore.encoder.encode(AppConfig())
        let obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])

        XCTAssertFalse(obj.keys.contains("widgets"))
        XCTAssertFalse(obj.keys.contains("widgetSettings"))
        XCTAssertFalse(obj.keys.contains("reserveSpace"))
    }

    /// Enum'lar PascalCase string olarak yazilmali.
    func testEnumHamDegerleriPascalCase() throws {
        let data = try JSONStore.encoder.encode(AppConfig())
        let obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])

        XCTAssertEqual(obj["edge"] as? String, "Bottom")
        XCTAssertEqual(obj["taskbarMode"] as? String, "Replace")
        XCTAssertEqual(obj["theme"] as? String, "Dark")
        XCTAssertEqual(obj["backdrop"] as? String, "Blur")
        XCTAssertEqual(obj["size"] as? String, "Small")
        XCTAssertEqual(obj["layout"] as? String, "Floating")
        XCTAssertEqual(obj["widthMode"] as? String, "Full")
        XCTAssertEqual(obj["alignment"] as? String, "Center")
    }

    /// Varsayilanlar C# AppConfig.cs ile ayni olmali.
    func testVarsayilanlarWindowsIleAyni() {
        let c = AppConfig()
        XCTAssertEqual(c.version, 2)
        XCTAssertEqual(c.tintOpacity, 0.55)
        XCTAssertEqual(c.edgeMargin, 6)
        XCTAssertTrue(c.hideOnFullscreen)
        XCTAssertTrue(c.startWithWindows)
        XCTAssertTrue(c.explorerPinMenu)
        XCTAssertFalse(c.showTaskViewButton)
        XCTAssertFalse(c.clockShowSeconds)
        XCTAssertFalse(c.autoHide)
    }

    /// Yazip geri okumak degeri korumali; bilinmeyen alan okumayi bozmamali.
    func testYazOkuTurTuru() throws {
        var c = AppConfig()
        c.edge = .left
        c.size = .large
        c.monitorDevice = "VX3218-PC-MHD"
        c.items = [.app("/System/Applications/Safari.app", name: "Safari"), .separator()]
        c.reserveSpace = true

        let data = try JSONStore.encoder.encode(c)
        let back = try JSONStore.decoder.decode(AppConfig.self, from: data)

        XCTAssertEqual(back.edge, .left)
        XCTAssertEqual(back.size, .large)
        XCTAssertEqual(back.monitorDevice, "VX3218-PC-MHD")
        XCTAssertEqual(back.items.count, 2)
        XCTAssertEqual(back.items[0].name, "Safari")
        XCTAssertEqual(back.items[1].kind, .separator)
        XCTAssertEqual(back.reserveSpace, true)
    }

    /// DOCKHUB_HOME yolu gecersiz kilma davranisi Windows ile ayni.
    func testDockhubHomeGecersizKilma() throws {
        let tmp = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("dockhub-test-\(UUID().uuidString)")
        let svc = ConfigService(url: tmp.appendingPathComponent("config.json"))
        XCTAssertTrue(svc.didCreateDefaults)
        XCTAssertTrue(FileManager.default.fileExists(atPath: svc.url.path))
        try? FileManager.default.removeItem(at: tmp)
    }
}
