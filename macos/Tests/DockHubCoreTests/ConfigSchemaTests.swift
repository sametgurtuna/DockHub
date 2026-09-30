import XCTest
@testable import DockHubCore
@testable import DockHubPlatform

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

    // ---------------- Upstream v0.6.1 alanlari

    /// Yeni alanlari olmayan eski dosya okunabilmeli; eksikler varsayilan olur.
    /// Onceden sentezlenen init(from:) burada hata atiyordu ve ConfigService
    /// dosyayi bozuk sayip varsayilanlara donuyordu.
    func testEksikAnahtarlarVarsayilanlaOkunur() throws {
        let eski = #"{"version":2,"edge":"Left","size":"Large","items":[{"id":"a1","kind":"Separator"}]}"#
        let c = try JSONStore.decoder.decode(AppConfig.self, from: Data(eski.utf8))

        XCTAssertEqual(c.edge, .left)
        XCTAssertEqual(c.size, .large)
        XCTAssertEqual(c.items.first?.kind, .separator)
        XCTAssertFalse(c.showOnAllDisplays)
        XCTAssertTrue(c.runningAppsOnOwnDisplay, "C# varsayilani true")
        XCTAssertTrue(c.displaySizes.isEmpty)
        XCTAssertEqual(c.tintOpacity, 0.55)
        XCTAssertTrue(c.hideOnFullscreen)
        XCTAssertNil(c.monitorDevice)
    }

    /// v0.6 ust duzey alanlari C# gibi her zaman yazilmali; displaySizes bossa {}.
    func testV06AlanlariHerZamanYazilir() throws {
        let data = try JSONStore.encoder.encode(AppConfig())
        let obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])

        XCTAssertEqual(obj["showOnAllDisplays"] as? Bool, false)
        XCTAssertEqual(obj["runningAppsOnOwnDisplay"] as? Bool, true)
        XCTAssertEqual((obj["displaySizes"] as? [String: Any])?.count, 0)
    }

    /// Windows'un yazdigi grup ve pinnedEnd iceren oge listesi okunup ayni
    /// kurallarla geri yazilmali: pinnedEnd false ise yok, nil grup alanlari yok.
    func testGrupVeSagKenarTurTuru() throws {
        let windows = #"""
        {"version":2,"items":[
          {"id":"g1","kind":"Group","groupName":"AI","groupAccent":"AccentPurpleBrush","children":[
            {"id":"c1","kind":"App","path":"/Applications/Claude.app","name":"Claude"},
            {"id":"c2","kind":"Widget","widget":"clock","variant":"digital"}]},
          {"id":"w1","kind":"Widget","widget":"network","pinnedEnd":true},
          {"id":"w2","kind":"Widget","widget":"clock"}],
         "displaySizes":{"DELL U2720Q":"Large"}}
        """#
        let c = try JSONStore.decoder.decode(AppConfig.self, from: Data(windows.utf8))
        XCTAssertEqual(c.items.count, 3)
        XCTAssertEqual(c.items[0].kind, .group)
        XCTAssertEqual(c.items[0].groupName, "AI")
        XCTAssertEqual(c.items[0].children?.count, 2)
        XCTAssertEqual(c.items[0].children?[1].widget, "clock")
        XCTAssertTrue(c.items[1].pinnedEnd)
        XCTAssertFalse(c.items[2].pinnedEnd)
        XCTAssertEqual(c.displaySize(of: "DELL U2720Q"), .large)

        let data = try JSONStore.encoder.encode(c)
        let obj = try XCTUnwrap(try JSONSerialization.jsonObject(with: data) as? [String: Any])
        let items = try XCTUnwrap(obj["items"] as? [[String: Any]])
        XCTAssertEqual(items[0]["kind"] as? String, "Group")
        XCTAssertEqual(items[0]["groupAccent"] as? String, "AccentPurpleBrush")
        let cocuk = try XCTUnwrap(items[0]["children"] as? [[String: Any]])
        XCTAssertEqual(cocuk[0]["path"] as? String, "/Applications/Claude.app")
        XCTAssertFalse(cocuk[0].keys.contains("pinnedEnd"), "false yazilmamali (WhenWritingDefault)")
        XCTAssertEqual(items[1]["pinnedEnd"] as? Bool, true)
        XCTAssertFalse(items[2].keys.contains("pinnedEnd"))
        XCTAssertFalse(items[2].keys.contains("groupName"), "nil yazilmamali (WhenWritingNull)")
        XCTAssertFalse(items[2].keys.contains("children"))
        XCTAssertEqual((obj["displaySizes"] as? [String: String])?["DELL U2720Q"], "Large")
    }

    /// C# DisplaySizes StringComparer.OrdinalIgnoreCase kullaniyor.
    func testEkranBoyutuBuyukKucukHarfDuyarsiz() {
        var c = AppConfig()
        c.setDisplaySize(.medium, of: "Built-in Retina Display")
        XCTAssertEqual(c.displaySize(of: "built-in retina display"), .medium)
        c.setDisplaySize(.large, of: "BUILT-IN RETINA DISPLAY")
        XCTAssertEqual(c.displaySizes.count, 1, "ayni ekran iki kez tutulmamali")
        XCTAssertEqual(c.displaySize(of: "Built-in Retina Display"), .large)
        c.setDisplaySize(nil, of: "built-in retina display")
        XCTAssertNil(c.displaySize(of: "Built-in Retina Display"))
    }

    /// Windows v0.6.0 olculeri: ContentDip 40/46/54 + 2 * ZonesMargin(5).
    func testBoyutKalinliklariWindowsIleAyni() {
        XCTAssertEqual(DockSize.small.thickness, 50)
        XCTAssertEqual(DockSize.medium.thickness, 56)
        XCTAssertEqual(DockSize.large.thickness, 64)
    }

    /// Grup fabrikasi C# DockItem.Group ile ayni varsayilanlari kullanir.
    func testGrupFabrikasi() {
        let g = DockItem.group("Yeni", children: [.app("/Applications/Safari.app")])
        XCTAssertEqual(g.kind, .group)
        XCTAssertEqual(g.groupAccent, "AccentBlueBrush")
        XCTAssertEqual(g.children?.count, 1)
    }
}

/// Hatirlatici siralama ve "sonraki" secimi (ReminderLogic).
final class ReminderLogicTests: XCTestCase {
    private let liste = [
        Reminder(text: "İlaç", hour: 21, minute: 0),
        Reminder(text: "Toplantı", hour: 14, minute: 0),
        Reminder(text: "Spor", hour: 18, minute: 30),
    ]

    func testSaateGoreSiralanir() {
        XCTAssertEqual(ReminderLogic.sorted(liste).map(\.text),
                       ["Toplantı", "Spor", "İlaç"])
    }

    func testSonrakiDogruSecilir() {
        // 17:47 -> bir sonraki 18:30 Spor
        XCTAssertEqual(ReminderLogic.next(from: liste, nowMinutes: 17 * 60 + 47)?.text, "Spor")
        // 13:00 -> ilk sira Toplanti
        XCTAssertEqual(ReminderLogic.next(from: liste, nowMinutes: 13 * 60)?.text, "Toplantı")
        // 20:00 -> Ilac
        XCTAssertEqual(ReminderLogic.next(from: liste, nowMinutes: 20 * 60)?.text, "İlaç")
    }

    func testGunSonundaBastakiDoner() {
        // 23:30 -> bugun kalan yok, yarinki ilk (Toplanti) gosterilir
        XCTAssertEqual(ReminderLogic.next(from: liste, nowMinutes: 23 * 60 + 30)?.text, "Toplantı")
    }

    func testBosListeNilDoner() {
        XCTAssertNil(ReminderLogic.next(from: [], nowMinutes: 600))
    }

    func testGecersizSaatReddedilir() {
        XCTAssertNil(ReminderLogic.parseTime("25:00"))
        XCTAssertNil(ReminderLogic.parseTime("12:70"))
        XCTAssertNil(ReminderLogic.parseTime("abc"))
        XCTAssertNil(ReminderLogic.parseTime("12"))
        XCTAssertEqual(ReminderLogic.parseTime("18:30")?.hour, 18)
    }
}

/// claude CLI /usage ciktisinin ayristirilmasi.
/// Windows surumundeki regex'lerin ayni davrandigini sinar.
final class AIUsageParseTests: XCTestCase {
    func testTipikCiktiAyristirilir() throws {
        let metin = """
        Claude Code usage

        Current session: 45% used, resets 3pm (in 2 hours)
        Current week: 12% used, resets Monday (in 4 days)
        """
        let u = try XCTUnwrap(AIUsageService.parse(metin))
        XCTAssertEqual(u.sessionPercent, 45)
        XCTAssertEqual(u.sessionResets, "3pm")
        XCTAssertEqual(u.weekPercent, 12)
        XCTAssertEqual(u.weekResets, "Monday")
    }

    func testBuyukKucukHarfFarkEtmez() throws {
        let u = try XCTUnwrap(AIUsageService.parse("CURRENT SESSION: 7% USED, RESETS noon"))
        XCTAssertEqual(u.sessionPercent, 7)
    }

    func testYalnizHaftaVarsaOturumSifir() throws {
        let u = try XCTUnwrap(AIUsageService.parse("Current week: 88% used, resets Sunday"))
        XCTAssertEqual(u.weekPercent, 88)
        XCTAssertEqual(u.sessionPercent, 0)
    }

    func testIlgisizCiktiNilDoner() {
        XCTAssertNil(AIUsageService.parse("command not found: claude"))
        XCTAssertNil(AIUsageService.parse(""))
    }
}
