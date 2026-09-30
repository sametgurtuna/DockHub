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
            Section(L.t("macOS Dock")) {
                Picker(L.t("Replace the Dock"), selection: store.binding(\.taskbarMode)) {
                    Text(verbatim: "DockHub").tag(TaskbarMode.replace)
                    Text(L.t("Show both")).tag(TaskbarMode.showBoth)
                }
                .pickerStyle(.segmented)
                RowNote(L.t("DockHub turns on auto-hide for the macOS Dock while it runs and restores your setting when it quits. The macOS Dock still appears when the pointer reaches the screen edge."))
            }

            Section(L.t("Display")) {
                Picker(L.t("Main display"), selection: store.binding(\.monitorDevice)) {
                    Text(L.t("Primary display")).tag(String?.none)
                    ForEach(displayNames, id: \.self) { ad in
                        Text(ad).tag(String?.some(ad))
                    }
                }
                RowNote(L.t("The display the dock appears on."))
            }

            Section(L.t("Language")) {
                Picker(L.t("Interface language"), selection: store.binding(\.language)) {
                    Text(L.t("System")).tag(UiLanguage.system)
                    ForEach(L.languages, id: \.code) { info in
                        Text(verbatim: info.nativeName).tag(info.language)
                    }
                }
                VStack(alignment: .leading, spacing: 6) {
                    RowNote(L.t("System follows your macOS language. Restart DockHub to switch."))
                    if L.code(for: store.config.language, systemLanguages: Locale.preferredLanguages) != L.code {
                        Button(L.t("Restart now")) { Self.restart() }
                    }
                }
            }

            Section(L.t("Maintenance")) {
                LabeledContent {
                    HStack {
                        if let restored {
                            Text(restored ? L.t("Restored") : L.t("Nothing to restore")).foregroundStyle(.secondary)
                        }
                        Button(L.t("Restore")) { restored = SystemDock.restore() }
                    }
                } label: {
                    Text(L.t("Restore macOS Dock"))
                    RowNote(L.t("Brings back the Dock setting DockHub changed, in case the Dock stays hidden."))
                }
                LabeledContent(L.t("Settings and data folder")) {
                    Button(L.t("Open folder")) { NSWorkspace.shared.open(AppPaths.root) }
                }
                LabeledContent(L.t("Restart DockHub")) {
                    Button(L.t("Restart")) { Self.restart() }
                }
                LabeledContent(L.t("Quit DockHub")) {
                    Button(L.t("Quit")) { NSApp.terminate(nil) }
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
