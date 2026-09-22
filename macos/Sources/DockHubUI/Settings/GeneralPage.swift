import AppKit
import SwiftUI
import DockHubCore
import DockHubPlatform

/// Windows karsiligi: SettingsWindow.xaml "General" sayfasi.
/// Windows'taki "Start with Windows", "Explorer Pin to DockHub", "Hide in full
/// screen apps", "Show on all displays" ve ekran basina boyut satirlari yok:
/// macOS'ta henuz uygulanmiyorlar (docs/AYARLAR.md).
struct GeneralPage: View {
    @ObservedObject var store: SettingsStore
    @State private var restored: Bool?

    var body: some View {
        Form {
            Section("macOS Dock") {
                Picker("Replace the Dock", selection: store.binding(\.taskbarMode)) {
                    Text("DockHub").tag(TaskbarMode.replace)
                    Text("Show both").tag(TaskbarMode.showBoth)
                }
                .pickerStyle(.segmented)
                RowNote("DockHub turns on auto-hide for the macOS Dock while it runs and restores your setting when it quits. The macOS Dock still appears when the pointer reaches the screen edge.")
            }

            Section("Display") {
                Picker("Main display", selection: store.binding(\.monitorDevice)) {
                    Text("Primary display").tag(String?.none)
                    ForEach(displayNames, id: \.self) { ad in
                        Text(ad).tag(String?.some(ad))
                    }
                }
                RowNote("The display the dock appears on.")
            }

            Section("Maintenance") {
                LabeledContent {
                    HStack {
                        if let restored {
                            Text(restored ? "Restored" : "Nothing to restore").foregroundStyle(.secondary)
                        }
                        Button("Restore") { restored = SystemDock.restore() }
                    }
                } label: {
                    Text("Restore macOS Dock")
                    RowNote("Brings back the Dock setting DockHub changed, in case the Dock stays hidden.")
                }
                LabeledContent("Settings and data folder") {
                    Button("Open Folder") { NSWorkspace.shared.open(AppPaths.root) }
                }
                LabeledContent("Restart DockHub") {
                    Button("Restart") { Self.restart() }
                }
                LabeledContent("Quit DockHub") {
                    Button("Quit") { NSApp.terminate(nil) }
                }
            }
        }
        .formStyle(.grouped)
    }

    /// Kayitli ekran bagli degilse de listede kalir, secim kaybolmasin.
    private var displayNames: [String] {
        var adlar = NSScreen.screens.map(\.localizedName)
        if let kayitli = store.config.monitorDevice, !adlar.contains(kayitli) { adlar.append(kayitli) }
        return adlar
    }

    /// Yeni kopya, bu kopya kapanip sistem Dock ayarini geri yukledikten
    /// SONRA acilmali; yoksa eskinin geri yuklemesi yeninin gizlemesini bozar.
    private static func restart() {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = ["-c", "sleep 1; /usr/bin/open \"$0\"", Bundle.main.bundlePath]
        try? p.run()
        NSApp.terminate(nil)
    }
}
