import XCTest
@testable import DockHubCore

final class WebWidgetTests: XCTestCase {

    private var repoRoot: URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
    }

    func testSampleWidgetsHaveValidManifests() throws {
        let samplesDir = repoRoot.appendingPathComponent("samples/widgets")
        let helloWorldDir = samplesDir.appendingPathComponent("hello-world")
        let githubStarsDir = samplesDir.appendingPathComponent("github-stars")

        let hello = try WebWidgetCatalog.read(folder: helloWorldDir)
        XCTAssertEqual(hello.id, "dev.dockhub.hello-world")
        XCTAssertEqual(hello.widgetId, "web.dev.dockhub.hello-world")
        XCTAssertEqual(hello.name, "Hello world")
        XCTAssertEqual(hello.entry, "index.html")

        let stars = try WebWidgetCatalog.read(folder: githubStarsDir)
        XCTAssertEqual(stars.id, "dev.dockhub.github-stars")
        XCTAssertEqual(stars.widgetId, "web.dev.dockhub.github-stars")
        XCTAssertEqual(stars.permissions.network, ["api.github.com"])
    }

    func testInvalidIdsAndEntriesAreRejected() throws {
        // Gecersiz kimlik karakterleri
        let badIdJson = """
        {
            "id": "Bad Id!",
            "name": "Invalid",
            "entry": "index.html"
        }
        """
        XCTAssertThrowsError(try WebWidgetCatalog.parse(json: badIdJson))

        // Cok kisa kimlik (<3 karakter)
        let shortIdJson = """
        {
            "id": "a",
            "name": "Short",
            "entry": "index.html"
        }
        """
        XCTAssertThrowsError(try WebWidgetCatalog.parse(json: shortIdJson))

        // Bos ad
        let emptyNameJson = """
        {
            "id": "good.id",
            "name": "   ",
            "entry": "index.html"
        }
        """
        XCTAssertThrowsError(try WebWidgetCatalog.parse(json: emptyNameJson))

        // networkFromSettings varsayilan deger iceremez
        let serverWithDefault = """
        {
            "id": "test.server",
            "name": "Server",
            "entry": "index.html",
            "settings": [
                { "key": "serverUrl", "label": "Server", "type": "text", "default": "http://example.com" }
            ],
            "permissions": {
                "networkFromSettings": ["serverUrl"]
            }
        }
        """
        XCTAssertThrowsError(try WebWidgetCatalog.parse(json: serverWithDefault))

        // networkFromSettings metin tipi olmayan ayara baglanamaz
        let serverNotText = """
        {
            "id": "test.server2",
            "name": "Server",
            "entry": "index.html",
            "settings": [
                { "key": "port", "label": "Port", "type": "number" }
            ],
            "permissions": {
                "networkFromSettings": ["port"]
            }
        }
        """
        XCTAssertThrowsError(try WebWidgetCatalog.parse(json: serverNotText))
    }

    func testHostPermissionMatching() throws {
        let json = """
        {
            "id": "dev.dockhub.test",
            "name": "Test",
            "entry": "index.html",
            "permissions": {
                "network": ["api.github.com", "*.example.org"]
            }
        }
        """
        let manifest = try WebWidgetCatalog.parse(json: json)

        // Birebir eslesme
        XCTAssertTrue(WebWidgetCatalog.isHostAllowed(manifest: manifest, host: "api.github.com"))
        // Alt alan adi
        XCTAssertTrue(WebWidgetCatalog.isHostAllowed(manifest: manifest, host: "cdn.example.org"))
        // Ana alan adi
        XCTAssertTrue(WebWidgetCatalog.isHostAllowed(manifest: manifest, host: "example.org"))
        // Ic host adi
        XCTAssertTrue(WebWidgetCatalog.isHostAllowed(manifest: manifest, host: "dev-dockhub-test.widget.dockhub"))

        // Izin verilmeyenler
        XCTAssertFalse(WebWidgetCatalog.isHostAllowed(manifest: manifest, host: "evil-example.org"))
        XCTAssertFalse(WebWidgetCatalog.isHostAllowed(manifest: manifest, host: "github.com"))
        XCTAssertFalse(WebWidgetCatalog.isHostAllowed(manifest: manifest, host: "other.org"))
    }

    func testDynamicSettingsHosts() throws {
        let json = """
        {
            "id": "dev.dockhub.nas",
            "name": "NAS",
            "entry": "index.html",
            "settings": [
                { "key": "serverUrl", "label": "Server URL", "type": "text" }
            ],
            "permissions": {
                "networkFromSettings": ["serverUrl"]
            }
        }
        """
        let manifest = try WebWidgetCatalog.parse(json: json)
        let values: [String: JSONValue] = [
            "serverUrl": .string("http://nas.local:8080/api")
        ]
        let settingsHosts = WebWidgetCatalog.settingsHosts(manifest: manifest, values: values)
        XCTAssertEqual(settingsHosts, ["nas.local"])

        XCTAssertTrue(WebWidgetCatalog.isHostAllowed(manifest: manifest, host: "nas.local", settingsHosts: settingsHosts))
        XCTAssertFalse(WebWidgetCatalog.isHostAllowed(manifest: manifest, host: "other.local", settingsHosts: settingsHosts))
    }

    func testHttpRequestValidation() throws {
        // Izin verilmeyen host hata firlatir
        let disallowedArgs: [String: JSONValue] = [
            "url": .string("https://evil.com/data")
        ]
        XCTAssertThrowsError(try WebWidgetHttp.parse(args: disallowedArgs, isHostAllowed: { _ in false }))

        // Yasakli baslik (Cookie) hata firlatir
        let forbiddenHeaderArgs: [String: JSONValue] = [
            "url": .string("https://api.github.com/repos"),
            "headers": .object([
                "Cookie": .string("forbidden=1")
            ])
        ]
        XCTAssertThrowsError(try WebWidgetHttp.parse(args: forbiddenHeaderArgs, isHostAllowed: { _ in true }))

        // Gecerli istek kabul edilir
        let allowedArgs: [String: JSONValue] = [
            "url": .string("https://api.github.com/repos"),
            "method": .string("POST"),
            "headers": .object([
                "Content-Type": .string("application/json"),
                "Accept": .string("application/vnd.github+json")
            ]),
            "body": .string("{\"test\": true}"),
            "timeout": .number(20)
        ]
        let req = try WebWidgetHttp.parse(args: allowedArgs, isHostAllowed: { $0 == "api.github.com" })
        XCTAssertEqual(req.url.absoluteString, "https://api.github.com/repos")
        XCTAssertEqual(req.method, "POST")
        XCTAssertTrue(req.headers.contains { $0.0 == "Content-Type" && $0.1 == "application/json" })
        XCTAssertTrue(req.headers.contains { $0.0 == "Accept" && $0.1 == "application/vnd.github+json" })
        XCTAssertEqual(req.body, "{\"test\": true}")
    }

    func testWidgetDefinitionConversion() throws {
        let json = """
        {
            "id": "my.widget",
            "name": "My Widget",
            "entry": "index.html",
            "variants": [
                { "id": "standard", "name": "Standard", "size": "standard" },
                { "id": "wide", "name": "Wide", "size": "wide" }
            ]
        }
        """
        let manifest = try WebWidgetCatalog.parse(json: json)
        let def = WebWidgetCatalog.definition(for: manifest)

        XCTAssertEqual(def.id, "web.my.widget")
        XCTAssertEqual(def.displayName, "My Widget")
        XCTAssertEqual(def.category, "Web widgets")
        XCTAssertEqual(def.variants.count, 2)
        XCTAssertEqual(def.variants[0].id, "standard")
        XCTAssertEqual(def.variants[1].id, "wide")
    }
}
