import Foundation
import XCTest
@testable import DockHubCore

/// Writes the settings a new Mac install saves, for CI's comparison with Windows' AppConfig
/// (Scripts/compare-config-schema.py). Does nothing unless DOCKHUB_SCHEMA_OUT names a file.
final class ConfigSchemaDumpTests: XCTestCase {
    func testWritesDefaultConfigForSchemaCheck() throws {
        guard let path = ProcessInfo.processInfo.environment["DOCKHUB_SCHEMA_OUT"], !path.isEmpty else { return }
        let data = try JSONStore.encoder.encode(AppConfig())
        try data.write(to: URL(fileURLWithPath: path))
    }
}
