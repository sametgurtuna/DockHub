import AppKit
import SwiftUI
import DockHubCore

/// Windows karsiligi: SettingsWindow.xaml "About" sayfasi.
struct AboutPage: View {
    private var version: String {
        let b = Bundle.main
        let kisa = b.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "-"
        let yapi = b.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "-"
        return "Version \(kisa) (\(yapi))"
    }

    var body: some View {
        Form {
            Section {
                HStack(spacing: 16) {
                    Image(nsImage: NSApp.applicationIconImage)
                        .resizable().frame(width: 64, height: 64)
                    VStack(alignment: .leading, spacing: 4) {
                        Text("DockHub for macOS").font(.title2.weight(.semibold))
                        Text(version).foregroundStyle(.secondary)
                        Text("A native macOS version of DockHub, the dock for Windows.")
                            .foregroundStyle(.secondary)
                    }
                }
                .padding(.vertical, 6)
            }
            Section {
                LabeledContent("Project") {
                    Link("github.com/sametgurtuna/DockHub",
                         destination: URL(string: "https://github.com/sametgurtuna/DockHub")!)
                }
                LabeledContent("Settings file") {
                    HStack {
                        Text((AppPaths.config.path as NSString).abbreviatingWithTildeInPath)
                            .lineLimit(1).truncationMode(.middle).foregroundStyle(.secondary)
                        Button("Show in Finder") {
                            NSWorkspace.shared.activateFileViewerSelecting([AppPaths.config])
                        }
                    }
                }
            }
        }
        .formStyle(.grouped)
    }
}
