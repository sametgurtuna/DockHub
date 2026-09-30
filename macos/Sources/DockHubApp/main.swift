import AppKit
import DockHubCore
import DockHubUI

// Windows karsiligi: App.xaml.cs — yalniz baglama (wiring).
let app = NSApplication.shared
// LSUIElement karsiligi: Dock'ta ikon ve menu cubugu gostermez.
app.setActivationPolicy(.accessory)

let delegate = AppDelegate()
app.delegate = delegate
app.run()
