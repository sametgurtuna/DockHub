// swift-tools-version:6.0
import PackageDescription

let package = Package(
    name: "DockHub",
    platforms: [.macOS("27.0")],          // d-macos-hedefi-v2
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
