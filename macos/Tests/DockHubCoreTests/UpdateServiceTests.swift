import XCTest
@testable import DockHubCore

final class UpdateServiceTests: XCTestCase {

    func testCompareVersions() {
        XCTAssertEqual(UpdateService.compareVersions("1.0.0", "0.10.0"), 1)
        XCTAssertEqual(UpdateService.compareVersions("0.10.0", "0.10.0"), 0)
        XCTAssertEqual(UpdateService.compareVersions("0.9.5", "0.10.0"), -1)
        XCTAssertEqual(UpdateService.compareVersions("1.0.1", "1.0.0"), 1)
        XCTAssertEqual(UpdateService.compareVersions("1.0.0", "1.0"), 0)
        XCTAssertEqual(UpdateService.compareVersions("1.0", "1.0.0"), 0)
        XCTAssertEqual(UpdateService.compareVersions("2.0.0", "1.9.9"), 1)
    }

    func testDecodeGitHubRelease() throws {
        let json = """
        {
            "tag_name": "v1.0.0",
            "name": "DockHub 1.0.0",
            "body": "First stable release for Windows and macOS.",
            "html_url": "https://github.com/sametgurtuna/DockHub/releases/tag/v1.0.0",
            "draft": false,
            "prerelease": false,
            "assets": [
                {
                    "name": "DockHub-Setup-1.0.0-x64.exe",
                    "size": 65000000,
                    "browser_download_url": "https://github.com/sametgurtuna/DockHub/releases/download/v1.0.0/DockHub-Setup-1.0.0-x64.exe"
                },
                {
                    "name": "DockHub-1.0.0.dmg",
                    "size": 45000000,
                    "browser_download_url": "https://github.com/sametgurtuna/DockHub/releases/download/v1.0.0/DockHub-1.0.0.dmg"
                }
            ]
        }
        """

        let data = json.data(using: .utf8)!
        let info = try JSONDecoder().decode(GitHubReleaseInfo.self, from: data)

        XCTAssertEqual(info.tagName, "v1.0.0")
        XCTAssertFalse(info.draft)
        XCTAssertEqual(info.assets.count, 2)

        let dmgAsset = info.assets.first { $0.name.lowercased().hasSuffix(".dmg") }
        XCTAssertNotNil(dmgAsset)
        XCTAssertEqual(dmgAsset?.name, "DockHub-1.0.0.dmg")
        XCTAssertEqual(dmgAsset?.size, 45000000)
    }
}
