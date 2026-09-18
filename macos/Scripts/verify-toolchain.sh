#!/bin/bash
# DockHub macOS — arac zinciri dogrulamasi (T0-ORTAM).
# Gecici bir SPM paketi kurar, AppKit + SwiftUI hedefi derler, .app paketine
# cevirir, calistirir ve pencerenin gercekten ekranda oldugunu bildirir.
# Kullanim: macos/Scripts/verify-toolchain.sh
set -euo pipefail

echo "=== Arac zinciri ==="
sw_vers | sed 's/^/  /'
echo "  xcodebuild: $(xcodebuild -version 2>/dev/null | head -1 || echo YOK)"
echo "  xcode-select: $(xcode-select -p)"
echo "  swift: $(swift --version 2>&1 | head -1)"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/Sources/Probe"

cat > "$WORK/Package.swift" <<'EOF'
// swift-tools-version:6.0
import PackageDescription
let package = Package(
    name: "Probe",
    platforms: [.macOS("27.0")],
    targets: [.executableTarget(name: "Probe", path: "Sources/Probe")]
)
EOF

cat > "$WORK/Sources/Probe/main.swift" <<'EOF'
import AppKit
import SwiftUI

// SwiftUI gorunumu -> NSHostingView -> NSPanel: DockHub'in kullanacagi zincir.
struct ProbeView: View {
    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "square.grid.2x2.fill").font(.title2)
            Text("DockHub arac zinciri sondasi").font(.headline)
        }
        .padding(.horizontal, 18).padding(.vertical, 12)
    }
}

final class Delegate: NSObject, NSApplicationDelegate {
    var panel: NSPanel!
    func applicationDidFinishLaunching(_ n: Notification) {
        let host = NSHostingView(rootView: ProbeView())
        host.frame = NSRect(x: 0, y: 0, width: 360, height: 56)

        panel = NSPanel(contentRect: host.frame,
                        styleMask: [.nonactivatingPanel, .titled, .fullSizeContentView],
                        backing: .buffered, defer: false)
        panel.titlebarAppearsTransparent = true
        panel.titleVisibility = .hidden
        panel.isFloatingPanel = true
        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .stationary]
        panel.isMovableByWindowBackground = true

        let fx = NSVisualEffectView(frame: host.frame)
        fx.material = .hudWindow
        fx.blendingMode = .behindWindow
        fx.state = .active
        fx.addSubview(host)
        panel.contentView = fx

        // Ekranin alt kenarina yerlestir (DockHub'in varsayilan konumu)
        if let s = NSScreen.main {
            let x = s.frame.midX - host.frame.width / 2
            panel.setFrameOrigin(NSPoint(x: x, y: s.visibleFrame.minY + 24))
        }
        panel.orderFrontRegardless()

        // Pencerenin gercekten ekranda oldugunu gozle
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) {
            let visible = self.panel.isVisible
            let onScreen = self.panel.occlusionState.contains(.visible)
            let f = self.panel.frame
            print("--- Gozlem ---")
            print("  bundleIdentifier : \(Bundle.main.bundleIdentifier ?? "YOK")")
            print("  SwiftUI+NSHostingView : kuruldu")
            print("  panel.isVisible : \(visible)")
            print("  occlusionState .visible : \(onScreen)")
            print("  frame : x=\(Int(f.minX)) y=\(Int(f.minY)) w=\(Int(f.width)) h=\(Int(f.height))")
            print("  screen : \(NSScreen.main?.localizedName ?? "-") scale \(NSScreen.main?.backingScaleFactor ?? 0)")
            print("  SONUC : \(visible && onScreen ? "PENCERE EKRANDA" : "PENCERE GORUNMUYOR")")
            NSApp.terminate(nil)
        }
    }
}

let app = NSApplication.shared
app.setActivationPolicy(.accessory)   // LSUIElement karsiligi
let d = Delegate()
app.delegate = d
app.run()
EOF

echo
echo "=== swift build ==="
cd "$WORK"
swift build 2>&1 | grep -E "error:|warning: search path|Build complete" || true

echo
echo "=== .app paketi ==="
APP="$WORK/Probe.app"
mkdir -p "$APP/Contents/MacOS"
cp "$WORK/.build/debug/Probe" "$APP/Contents/MacOS/Probe"
cat > "$APP/Contents/Info.plist" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Probe</string>
  <key>CFBundleExecutable</key><string>Probe</string>
  <key>CFBundleIdentifier</key><string>com.dockhub.probe</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>LSMinimumSystemVersion</key><string>27.0</string>
  <key>LSUIElement</key><true/>
</dict></plist>
EOF
echo "  olusturuldu: Probe.app"
echo
echo "=== calistir ==="
"$APP/Contents/MacOS/Probe"
