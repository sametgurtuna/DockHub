import Foundation
import XCTest
@testable import DockHubCore

/// Geri alma yigini (Windows: ConfigHistoryTests).
final class ConfigHistoryTests: XCTestCase {
    private func config(_ items: [DockItem]) -> AppConfig {
        var c = AppConfig()
        c.items = items
        return c
    }

    func testGeriAlmaOgeleriGeriVerir() {
        let history = ConfigHistory()
        var c = config([.app("/Applications/Safari.app"), .separator()])
        XCTAssertFalse(history.canUndo)
        history.push(c, "Removed Safari", destructive: true)
        c.items.removeFirst()
        XCTAssertTrue(history.canUndo)

        let entry = history.undo(&c)
        XCTAssertEqual(entry?.description, "Removed Safari")
        XCTAssertEqual(entry?.destructive, true)
        XCTAssertEqual(c.items.count, 2)
        XCTAssertEqual(c.items[0].path, "/Applications/Safari.app")
        XCTAssertFalse(history.canUndo)
        XCTAssertNil(history.undo(&c))
    }

    /// Widget'in kendi ayarlari (su sayaci) geri almada eski haline donmez (C#: Reuse).
    func testWidgetAyarlariCanliKalir() {
        let history = ConfigHistory()
        var water = DockItem.widget("hydration")
        water.settings = .object(["count": .number(1)])
        var c = config([water, .app("/Applications/Mail.app")])
        history.push(c, "Moved Mail")
        c.items.swapAt(0, 1)
        c.items[1].settings = .object(["count": .number(5)])

        history.undo(&c)
        XCTAssertEqual(c.items[0].widget, "hydration")
        XCTAssertEqual(c.items[0].setting("count"), .number(5))
    }

    func testKapasiteYirmi() {
        let history = ConfigHistory()
        let c = config([])
        for i in 0..<25 { history.push(c, "step \(i)") }
        XCTAssertEqual(history.entries.count, ConfigHistory.capacity)
        XCTAssertEqual(history.entries.first?.description, "step 5")
    }

    /// Duzenleme oturumu: tek adim; hicbir sey degismediyse iz birakmaz.
    func testOturumTekAdim() {
        let history = ConfigHistory()
        var c = config([.app("/A.app"), .app("/B.app")])
        history.beginSession(c, "Edited the dock")
        XCTAssertNil(history.push(c, "ignored"), "oturum acikken yeni adim eklenmez")
        XCTAssertFalse(history.canUndo)
        XCTAssertFalse(history.endSession(c), "degisiklik yok")
        XCTAssertTrue(history.entries.isEmpty)

        history.beginSession(c, "Edited the dock")
        c.items.reverse()
        c.items[0].settings = .object(["x": .bool(true)])
        XCTAssertTrue(history.endSession(c))
        XCTAssertEqual(history.latest?.destructive, true)
        history.undo(&c)
        XCTAssertEqual(c.items.map(\.path), ["/A.app", "/B.app"])
    }

    /// Yalniz bir widget ayari degistiyse dock duzenlenmis sayilmaz.
    func testOturumAyarDegisikliginiSaymaz() {
        let history = ConfigHistory()
        var c = config([.widget("alarm")])
        history.beginSession(c, "Edited the dock")
        c.items[0].settings = .object(["enabled": .bool(false)])
        XCTAssertFalse(history.endSession(c))
    }

    /// Gorunum Windows'taki gibi PascalCase adlarla yakalanir ve geri konur.
    func testGorunumYakalamaVeGeriKoyma() {
        var c = AppConfig()
        c.edge = .left
        c.theme = .light
        c.widgetStyle = .seamless
        let captured = ConfigHistory.captureAppearance(c)
        XCTAssertEqual(captured["Edge"], .string("Left"))
        XCTAssertEqual(captured["WidgetStyle"], .string("Seamless"))
        XCTAssertEqual(captured.count, ConfigHistory.appearanceProperties.count)

        var other = AppConfig()
        other.extras["future"] = .bool(true)
        ConfigHistory.restoreAppearance(&other, captured)
        XCTAssertEqual(other.edge, .left)
        XCTAssertEqual(other.theme, .light)
        XCTAssertEqual(other.widgetStyle, .seamless)
        XCTAssertEqual(other.extras["future"], .bool(true), "bilinmeyen ayar korunur")

        // Goruntude olmayan ayar: missingAsDefault ile varsayilana doner.
        var third = c
        ConfigHistory.restoreAppearance(&third, ["Size": .string("Large")], missingAsDefault: true)
        XCTAssertEqual(third.size, .large)
        XCTAssertEqual(third.edge, .bottom)
        XCTAssertEqual(third.widgetStyle, .cards)
    }
}

/// Profil islemleri ve kurallari (Windows: ProfileServiceTests, ProfileRulesTests).
final class ProfileTests: XCTestCase {
    private let utc: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "UTC")!
        return c
    }()

    private func date(_ text: String) -> Date {
        let f = ISO8601DateFormatter()
        return f.date(from: text)!
    }

    func testKaydetVeGec() {
        var c = AppConfig()
        c.items = [.app("/Applications/Safari.app")]
        c.edge = .bottom

        let work = Profiles.saveCurrentAs("Work", &c)
        XCTAssertEqual(c.profiles.map(\.name).count, 2, "ilk kayit Varsayilan profili de olusturur")
        XCTAssertEqual(c.activeProfileId, work.id)
        XCTAssertNil(Profiles.active(c)?.items, "etkin profil ogelerin kopyasini tutmaz")

        // Work'te duzen degisir, Varsayilan'a gecilir: eski duzen geri gelir.
        c.items = [.app("/Applications/Xcode.app"), .separator()]
        c.edge = .left
        let defaultId = c.profiles[0].id
        XCTAssertTrue(Profiles.switchTo(defaultId, &c))
        XCTAssertEqual(c.items.map(\.path), ["/Applications/Safari.app"])
        XCTAssertEqual(c.edge, .bottom)
        XCTAssertEqual(c.activeProfileId, defaultId)

        // Work'e donus: Work'un duzeni saklanmisti.
        XCTAssertTrue(Profiles.switchTo(work.id, &c))
        XCTAssertEqual(c.items.count, 2)
        XCTAssertEqual(c.edge, .left)
        XCTAssertFalse(Profiles.switchTo(work.id, &c), "zaten etkin")
    }

    /// Windows'ta kaydedilmis bir profile Mac'te gecilebilir (PascalCase gorunum, TopBar).
    func testWindowsProfilineGecis() throws {
        let fixture = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent()
            .deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("tests/fixtures/config-windows.json")
        var c = try JSONStore.decoder.decode(AppConfig.self, from: Data(contentsOf: fixture))
        XCTAssertEqual(c.edge, .left)
        XCTAssertTrue(c.topBar.enabled)
        let office = try XCTUnwrap(c.profiles.first { $0.name == "Office" })

        XCTAssertTrue(Profiles.switchTo(office.id, &c))
        XCTAssertEqual(c.items.map(\.widget), [nil, "calendar"])
        XCTAssertEqual(c.edge, .bottom)
        XCTAssertEqual(c.size, .small)
        XCTAssertFalse(c.topBar.enabled, "profilin ust bari")
        // Onceki etkin profil (Home) canli duzeni saklar.
        let home = try XCTUnwrap(c.profiles.first { $0.name == "Home" })
        if case .object(let appearance)? = home.appearance {
            XCTAssertEqual(appearance["Edge"], .string("Left"))
            XCTAssertNotNil(appearance["TopBar"])
        } else {
            XCTFail("Home'un gorunumu saklanmali")
        }
        XCTAssertNotNil(home.items)
    }

    func testSilmeVeTekProfil() {
        var c = AppConfig()
        let second = Profiles.saveCurrentAs("Second", &c)
        let first = c.profiles[0].id
        Profiles.delete(second.id, &c)
        XCTAssertEqual(c.profiles.count, 2, "etkin profil silinmez")
        Profiles.switchTo(first, &c)
        Profiles.delete(second.id, &c)
        XCTAssertTrue(c.profiles.isEmpty, "tek profil hic yokmus gibi")
        XCTAssertNil(c.activeProfileId)
    }

    func testEkranSayisiTekProfile() {
        var c = AppConfig()
        let b = Profiles.saveCurrentAs("B", &c)
        let a = c.profiles[0].id
        Profiles.setAutoDisplayCount(a, 2, &c)
        Profiles.setAutoDisplayCount(b.id, 2, &c)
        XCTAssertNil(c.profiles[0].autoDisplayCount)
        XCTAssertEqual(c.profiles[1].autoDisplayCount, 2)
        XCTAssertNil(Profiles.forDisplayCount(2, c), "zaten etkin")
        Profiles.switchTo(a, &c)
        XCTAssertEqual(Profiles.forDisplayCount(2, c), b.id)
        XCTAssertEqual(Profiles.next(c), b.id)
    }

    func testSaatAyristirma() {
        XCTAssertEqual(ProfileRules.minutes("9:05"), 545)
        XCTAssertEqual(ProfileRules.minutes("09:05"), 545)
        XCTAssertEqual(ProfileRules.minutes(" 23:59 "), 1439)
        XCTAssertNil(ProfileRules.minutes("24:00"))
        XCTAssertNil(ProfileRules.minutes("9:5"))
        XCTAssertNil(ProfileRules.minutes("nine"))
        XCTAssertNil(ProfileRules.minutes(""))
        XCTAssertNil(ProfileRules.minutes(nil))
    }

    func testSaatAraligi() {
        var p = DockProfile(name: "Night")
        p.autoTimeFrom = "22:00"
        p.autoTimeTo = "06:00"
        // 2026-10-02 bir Cuma, 2026-10-03 Cumartesi.
        XCTAssertTrue(ProfileRules.inTimeWindow(p, now: date("2026-10-02T23:00:00Z"), calendar: utc))
        XCTAssertTrue(ProfileRules.inTimeWindow(p, now: date("2026-10-03T05:00:00Z"), calendar: utc))
        XCTAssertFalse(ProfileRules.inTimeWindow(p, now: date("2026-10-03T12:00:00Z"), calendar: utc))

        p.autoWeekdaysOnly = true
        // Cuma gecesi baslayan aralik Cuma'ya aittir: Cumartesi sabahi da gecerli.
        XCTAssertTrue(ProfileRules.inTimeWindow(p, now: date("2026-10-03T05:00:00Z"), calendar: utc))
        XCTAssertFalse(ProfileRules.inTimeWindow(p, now: date("2026-10-03T23:00:00Z"), calendar: utc))

        var day = DockProfile(name: "Work")
        day.autoTimeFrom = "09:00"
        day.autoTimeTo = "17:30"
        XCTAssertTrue(ProfileRules.inTimeWindow(day, now: date("2026-10-02T17:29:00Z"), calendar: utc))
        XCTAssertFalse(ProfileRules.inTimeWindow(day, now: date("2026-10-02T17:30:00Z"), calendar: utc))
    }

    func testUygulamaAdi() {
        XCTAssertEqual(ProfileRules.normalizeApp("Steam.exe"), "steam")
        XCTAssertEqual(ProfileRules.normalizeApp(#"C:\Games\Steam\steam.exe"#), "steam")
        XCTAssertEqual(ProfileRules.normalizeApp("/Applications/Steam.app"), "steam")
        XCTAssertEqual(ProfileRules.normalizeApp(" Xcode "), "xcode")
    }

    /// Uygulama kurali saat kuralindan once; kural bitince onceki profile donulur.
    func testKuralDurumu() {
        var c = AppConfig()
        let games = Profiles.saveCurrentAs("Games", &c)
        let work = Profiles.saveCurrentAs("Work", &c)
        let home = c.profiles[0].id
        Profiles.switchTo(home, &c)
        Profiles.setAutoApp(games.id, "Steam.app", &c)
        Profiles.setAutoTime(work.id, from: "09:00", to: "17:00", weekdaysOnly: false, &c)

        var state = ProfileRuleState()
        let noon = date("2026-10-02T12:00:00Z")
        XCTAssertEqual(state.evaluate(c, runningApps: ["steam"], now: noon, calendar: utc), games.id)
        Profiles.switchTo(games.id, &c)
        XCTAssertNil(state.evaluate(c, runningApps: ["steam"], now: noon, calendar: utc))
        // Steam kapandi, saat kurali hala gecerli: Work.
        XCTAssertEqual(state.evaluate(c, runningApps: [], now: noon, calendar: utc), work.id)
        Profiles.switchTo(work.id, &c)
        // Saat de bitti: kullanicinin ilk profili (Home).
        XCTAssertEqual(state.evaluate(c, runningApps: [], now: date("2026-10-02T18:00:00Z"), calendar: utc), home)
    }
}

/// Kisayol metni Windows'la ayni bicim (Windows: HotkeyGestureTests).
final class HotkeyGestureTests: XCTestCase {
    func testWindowsMetniOkunur() throws {
        let g = try XCTUnwrap(HotkeyGesture.parse("Ctrl+Alt+D"))
        XCTAssertEqual(g.modifiers, [.control, .option])
        XCTAssertEqual(g.key, "D")
        XCTAssertEqual(g.keyCode, 0x02)
        XCTAssertEqual(g.text, "Ctrl+Alt+D")
        XCTAssertEqual(g.symbols, "⌃⌥D")

        let win = try XCTUnwrap(HotkeyGesture.parse("win+shift+s"))
        XCTAssertEqual(win.modifiers, [.command, .shift])
        XCTAssertEqual(win.text, "Win+Shift+S")
        XCTAssertEqual(win.symbols, "⇧⌘S")

        XCTAssertEqual(HotkeyGesture.parse("Win+Alt+Space")?.keyCode, 0x31)
        XCTAssertEqual(HotkeyGesture.parse("Ctrl+D5")?.key, "5")
        XCTAssertEqual(HotkeyGesture.parse("Ctrl+5")?.keyCode, 0x17)
        XCTAssertEqual(HotkeyGesture.parse("Alt+F12")?.keyCode, 0x6F)
        XCTAssertEqual(HotkeyGesture.parse("Ctrl+Enter")?.key, "Return")
        XCTAssertEqual(HotkeyGesture.parse("Ctrl+Oem2")?.symbols, "⌃/")
    }

    func testGecersizler() {
        XCTAssertNil(HotkeyGesture.parse(nil))
        XCTAssertNil(HotkeyGesture.parse(""))
        XCTAssertNil(HotkeyGesture.parse("D"), "degistirici yok")
        XCTAssertNil(HotkeyGesture.parse("Shift+D"), "yalniz Shift yetmez")
        XCTAssertNil(HotkeyGesture.parse("Ctrl+Alt"), "tus yok")
        XCTAssertNil(HotkeyGesture.parse("Ctrl+D+E"), "iki tus")
        XCTAssertNil(HotkeyGesture.parse("Ctrl+Banana"))
    }

    func testTusKodundan() {
        XCTAssertEqual(HotkeyGesture.from(keyCode: 0x02, modifiers: [.command, .option])?.text, "Win+Alt+D")
        XCTAssertNil(HotkeyGesture.from(keyCode: 0x02, modifiers: [.shift]))
        XCTAssertNil(HotkeyGesture.from(keyCode: 0xFF, modifiers: [.command]))
    }

    func testEylemKisayollari() {
        var c = AppConfig()
        XCTAssertEqual(HotkeyActions.gesture(HotkeyActions.toggleDock, in: c)?.text, "Ctrl+Alt+D", "varsayilan")
        XCTAssertNil(HotkeyActions.gesture(HotkeyActions.nextProfile, in: c))

        HotkeyActions.set(HotkeyActions.toggleDock, nil, in: &c)
        XCTAssertEqual(c.hotkeys[HotkeyActions.toggleDock], "", "kapali: bos metin (Windows'la ayni)")
        XCTAssertNil(HotkeyActions.gesture(HotkeyActions.toggleDock, in: c))

        HotkeyActions.set(HotkeyActions.nextProfile, HotkeyGesture.parse("Win+Alt+P"), in: &c)
        XCTAssertEqual(c.hotkeys[HotkeyActions.nextProfile], "Win+Alt+P")
        HotkeyActions.reset(HotkeyActions.toggleDock, in: &c)
        XCTAssertEqual(HotkeyActions.gesture(HotkeyActions.toggleDock, in: c)?.text, "Ctrl+Alt+D")
    }
}
