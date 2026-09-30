import Foundation
import XCTest
@testable import DockHubCore

/// Arayuz cevirisi: Windows'la ayni Strings_*.json dosyalari ve ayni kurallar (Core/Localizer.cs).
final class LocalizerTests: XCTestCase {
    private static let resources: URL = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent()      // DockHubCoreTests
        .deletingLastPathComponent()      // Tests
        .deletingLastPathComponent()      // macos
        .deletingLastPathComponent()      // depo koku
        .appendingPathComponent("src/CustomDock/Resources")

    override func tearDown() {
        L.load(.english, directory: nil)
        super.tearDown()
    }

    func testDilKoduSecimi() {
        XCTAssertEqual(L.code(for: .turkish, systemLanguages: ["en-US"]), "tr")
        XCTAssertEqual(L.code(for: .english, systemLanguages: ["tr-TR"]), "en")
        XCTAssertEqual(L.code(for: .system, systemLanguages: ["de-DE", "en-US"]), "de")
        XCTAssertEqual(L.code(for: .system, systemLanguages: ["fr-FR", "es-419"]), "es", "bilinmeyen dil atlanir, siradaki bilinen secilir")
        XCTAssertEqual(L.code(for: .system, systemLanguages: ["en-GB", "tr-TR"]), "en")
        XCTAssertEqual(L.code(for: .system, systemLanguages: ["fr-FR"]), "en")
        XCTAssertEqual(L.code(for: .system, systemLanguages: []), "en")
    }

    /// Dosyalarda yorum satirlari var; hepsi okunabilmeli.
    func testWindowsDosyalariOkunur() {
        for code in ["tr", "de", "es"] {
            let table = L.strings(code: code, in: Self.resources)
            XCTAssertGreaterThan(table.count, 1000, code)
            XCTAssertFalse(table["About"]?.isEmpty ?? true, code)
        }
        XCTAssertTrue(L.strings(code: "xx", in: Self.resources).isEmpty)
    }

    func testCeviriVeBicim() {
        L.load(.turkish, directory: Self.resources)
        XCTAssertEqual(L.code, "tr")
        XCTAssertEqual(L.t("About"), "Hakkında")
        XCTAssertEqual(L.t("{0} items", 3), "3 öğe")
        XCTAssertEqual(L.t("An English text nobody translated"), "An English text nobody translated")
        XCTAssertEqual(L.t(""), "")

        L.load(.english, directory: Self.resources)
        XCTAssertEqual(L.code, "en")
        XCTAssertEqual(L.t("About"), "About")
        XCTAssertEqual(L.t("{0} items", 3), "3 items")
    }

    /// C# string.Format'in kullandigimiz kadari.
    func testBicimKurallari() {
        XCTAssertEqual(L.format("{0} of {1}", ["2", "5"]), "2 of 5")
        XCTAssertEqual(L.format("{1}, {0}", ["a", "b"]), "b, a")
        XCTAssertEqual(L.format("{0:N0} MB", ["12"]), "12 MB")
        XCTAssertEqual(L.format("{{0}} is {0}", ["x"]), "{0} is x")
        XCTAssertEqual(L.format("{2} stays", ["a"]), "{2} stays", "olmayan arguman oldugu gibi kalir")
        XCTAssertEqual(L.format("no braces", []), "no braces")
        XCTAssertEqual(L.format("{ open", []), "{ open")
    }

    /// Widget ve varyant adlari da cevirilir (galeri, oge listesi).
    func testWidgetAdlariCevrilir() {
        L.load(.german, directory: Self.resources)
        for def in WidgetRegistry.all {
            XCTAssertNotEqual(def.displayName, "", def.id)
            XCTAssertNotEqual(def.displayCategory, "", def.id)
        }
        XCTAssertEqual(WidgetRegistry.find("recycle-bin")?.displayName, "Papierkorb")
    }
}
