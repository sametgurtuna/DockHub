import XCTest
@testable import DockHubCore

/// Ayarlar penceresinin dayandigi saf mantik (T18-AYARLAR).
final class SettingsLogicTests: XCTestCase {

    private func ornek() -> AppConfig {
        var c = AppConfig()
        c.items = [
            .app("/Applications/Safari.app"),
            DockItem(id: "su", kind: .widget, widget: "hydration", variant: "timer",
                     settings: .object(["count": .number(1)])),
            .group("K", children: [DockItem(id: "not", kind: .widget, widget: "notes",
                                            settings: .object(["text": .string("a")]))]),
        ]
        return c
    }

    /// Widget'in kendi ayari degisince dock yeniden kurulmamali.
    func testWidgetAyariImzayiDegistirmez() {
        let a = ornek()
        var b = a
        b.items[1].settings = .object(["count": .number(5)])
        b.items[2].children?[0].settings = .object(["text": .string("baska")])
        XCTAssertEqual(a.layoutSignature(), b.layoutSignature())
    }

    /// Yerlesimi etkileyen her degisiklik imzayi degistirmeli.
    func testYerlesimDegisikligiImzayiDegistirir() {
        let a = ornek()
        let degisiklikler: [(String, (inout AppConfig) -> Void)] = [
            ("kenar", { $0.edge = .left }),
            ("boyut", { $0.size = .large }),
            ("tema", { $0.theme = .light }),
            ("ton", { $0.tintOpacity = 0.3 }),
            ("sira", { $0.items.swapAt(0, 1) }),
            ("varyant", { $0.items[1].variant = "progress" }),
            ("ad", { $0.items[0].name = "Tarayici" }),
            ("argumen", { $0.items[0].arguments = "--new-window" }),
            ("silme", { $0.items.removeLast() }),
            ("mod", { $0.taskbarMode = .showBoth }),
            ("ekran", { $0.monitorDevice = "DELL" }),
        ]
        for (ad, degis) in degisiklikler {
            var b = a
            degis(&b)
            XCTAssertNotEqual(a.layoutSignature(), b.layoutSignature(), ad)
        }
    }

    func testArgumanlarKabukGibiBolunur() {
        XCTAssertEqual(LaunchArguments.split(""), [])
        XCTAssertEqual(LaunchArguments.split("   "), [])
        XCTAssertEqual(LaunchArguments.split("--new-window"), ["--new-window"])
        XCTAssertEqual(LaunchArguments.split("-n  --profile x"), ["-n", "--profile", "x"])
        XCTAssertEqual(LaunchArguments.split(#"--dir "My Folder" 'a b'"#), ["--dir", "My Folder", "a b"])
        XCTAssertEqual(LaunchArguments.split(#"a\ b c"#), ["a b", "c"])
        XCTAssertEqual(LaunchArguments.split(#"--empty """#), ["--empty", ""])
    }
}
