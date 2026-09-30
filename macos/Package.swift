// swift-tools-version:6.0
import PackageDescription

let package = Package(
    name: "DockHub",
    platforms: [.macOS(.v15)],            // DockHub for Mac needs macOS 15 (Sequoia) or later
    targets: [
        // Platformdan bagimsiz: AppKit import etmez, test edilebilir.
        .target(name: "DockHubCore"),
        // macOS API sarmalayicilari.
        .target(name: "DockHubPlatform", dependencies: ["DockHubCore"]),
        // AppKit + SwiftUI gorunumler.
        .target(name: "DockHubUI", dependencies: ["DockHubCore", "DockHubPlatform"]),
        // Executable: yalniz baglama.
        .executableTarget(name: "DockHubApp", dependencies: ["DockHubUI"]),
        .testTarget(name: "DockHubCoreTests", dependencies: ["DockHubCore", "DockHubPlatform"]),
    ]
)
