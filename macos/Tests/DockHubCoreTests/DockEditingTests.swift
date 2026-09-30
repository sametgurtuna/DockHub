import Foundation
import XCTest
@testable import DockHubCore

/// Duzenleme modu, galeriden surukleme ve galeri aramasi (Windows: DockEditModeTests,
/// NewWidgetDragTests, GalleryFilterTests).
final class DockEditingTests: XCTestCase {
    private let items: [DockItem] = [
        DockItem(id: "a", kind: .app, path: "/A.app"),
        DockItem(id: "b", kind: .separator),
        DockItem(id: "c", kind: .widget, widget: "clock"),
    ]

    func testTasima() {
        XCTAssertEqual(DockEditing.move(items, "c", before: "a").map(\.id), ["c", "a", "b"])
        XCTAssertEqual(DockEditing.move(items, "a", before: "c").map(\.id), ["b", "a", "c"])
        XCTAssertEqual(DockEditing.move(items, "a", before: nil).map(\.id), ["b", "c", "a"])
        XCTAssertEqual(DockEditing.move(items, "a", before: "a").map(\.id), ["a", "b", "c"], "kendi onune")
        XCTAssertEqual(DockEditing.move(items, "x", before: "a").map(\.id), ["a", "b", "c"], "olmayan oge")
        XCTAssertEqual(DockEditing.move(items, "a", before: "x").map(\.id), ["a", "b", "c"], "olmayan hedef")
    }

    func testEkleme() {
        let new = DockItem(id: "n", kind: .separator)
        XCTAssertEqual(DockEditing.insert(items, new, before: "b").map(\.id), ["a", "n", "b", "c"])
        XCTAssertEqual(DockEditing.insert(items, new, before: nil).map(\.id), ["a", "b", "c", "n"])
        XCTAssertEqual(DockEditing.insert(items, new, before: "x").map(\.id), ["a", "b", "c", "n"])
    }

    func testSiradakiDuzen() throws {
        let clock = try XCTUnwrap(WidgetRegistry.find("clock"))          // analog, digital
        XCTAssertEqual(DockEditing.nextVariant(of: clock, current: "analog"), "digital")
        XCTAssertEqual(DockEditing.nextVariant(of: clock, current: "digital"), "analog", "basa doner")
        XCTAssertEqual(DockEditing.nextVariant(of: clock, current: "analog", step: -1), "digital")
        XCTAssertEqual(DockEditing.nextVariant(of: clock, current: "unknown"), "digital")
        let stopwatch = try XCTUnwrap(WidgetRegistry.find("stopwatch"))
        XCTAssertNil(DockEditing.nextVariant(of: stopwatch, current: "default"), "tek duzen")
    }

    func testSuruklemeVerisi() {
        let full = DockDrag.widget("clock", variant: "digital")
        XCTAssertEqual(DockDrag.decodeWidget(full)?.id, "clock")
        XCTAssertEqual(DockDrag.decodeWidget(full)?.variant, "digital")
        XCTAssertNil(DockDrag.decodeWidget(DockDrag.widget("clock", variant: nil))?.variant)
        XCTAssertNil(DockDrag.decodeWidget(DockDrag.widget("clock", variant: ""))?.variant)
        XCTAssertNil(DockDrag.decodeWidget("clock"), "oneksiz metin widget degildir")
        XCTAssertNil(DockDrag.decodeWidget("dockhub-widget:"))
        XCTAssertNil(DockDrag.decodeWidget(DockDrag.item("a")))

        XCTAssertEqual(DockDrag.decodeItem(DockDrag.item("a1")), "a1")
        XCTAssertNil(DockDrag.decodeItem("a1"))
        XCTAssertNil(DockDrag.decodeItem(DockDrag.widget("clock", variant: nil)))
    }

    func testGaleriAramasi() {
        XCTAssertTrue(GalleryFilter.matches("", ["Clock"]))
        XCTAssertTrue(GalleryFilter.matches("  ", ["Clock"]))
        XCTAssertTrue(GalleryFilter.matches("clo", ["Clock"]))
        XCTAssertTrue(GalleryFilter.matches("SAAT", ["Dünya saati"]))
        XCTAssertTrue(GalleryFilter.matches("dunya", ["Dünya saati"]), "aksan duyarsiz")
        XCTAssertTrue(GalleryFilter.matches("world cities", ["World clock", "Clocks for different cities."]))
        XCTAssertFalse(GalleryFilter.matches("world weather", ["World clock", "Clocks for different cities."]))
        XCTAssertFalse(GalleryFilter.matches("zzz", ["Clock"]))
    }
}
