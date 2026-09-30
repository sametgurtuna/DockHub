import AppKit
import SwiftUI
import DockHubCore

/// Windows karsiligi: SettingsWindow.xaml "About" sayfasi.
struct AboutPage: View {
    @State private var checking = false
    @State private var statusMessage: String?
    @State private var availableUpdate: MacRelease?

    private var version: String {
        let b = Bundle.main
        let kisa = b.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.10.0"
        let yapi = b.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "1"
        return L.t("Version {0}", "\(kisa) (\(yapi))")
    }

    var body: some View {
        Form {
            Section {
                HStack(spacing: 16) {
                    Image(nsImage: NSApp.applicationIconImage)
                        .resizable().frame(width: 64, height: 64)
                    VStack(alignment: .leading, spacing: 4) {
                        Text(L.t("DockHub for macOS")).font(.title2.weight(.semibold))
                        Text(version).foregroundStyle(.secondary)
                        Text(L.t("A native macOS version of DockHub, the dock for Windows."))
                            .foregroundStyle(.secondary)
                    }
                }
                .padding(.vertical, 6)
            }

            Section(L.t("Updates")) {
                VStack(alignment: .leading, spacing: 8) {
                    HStack(spacing: 10) {
                        Button(L.t("Check for updates")) {
                            checkForUpdates()
                        }
                        .disabled(checking)

                        if checking {
                            ProgressView().controlSize(.small)
                        }
                    }
                    if let statusMessage {
                        Text(statusMessage).font(.caption).foregroundStyle(.secondary)
                    }
                    if let update = availableUpdate {
                        HStack(spacing: 8) {
                            if let dmgUrl = update.dmgUrl {
                                Button(L.t("Download and install")) {
                                    NSWorkspace.shared.open(dmgUrl)
                                }
                            }
                            Link(L.t("Project"), destination: update.pageUrl)
                        }
                    }
                }
            }

            Section {
                LabeledContent(L.t("Project")) {
                    Link("github.com/sametgurtuna/DockHub",
                         destination: URL(string: "https://github.com/sametgurtuna/DockHub")!)
                }
                LabeledContent(L.t("Settings file")) {
                    HStack {
                        Text((AppPaths.config.path as NSString).abbreviatingWithTildeInPath)
                            .lineLimit(1).truncationMode(.middle).foregroundStyle(.secondary)
                        Button(L.t("Show in Finder")) {
                            NSWorkspace.shared.activateFileViewerSelecting([AppPaths.config])
                        }
                    }
                }
            }
        }
        .formStyle(.grouped)
    }

    private func checkForUpdates() {
        checking = true
        statusMessage = nil
        Task { @MainActor in
            defer { checking = false }
            do {
                if let release = try await UpdateService.shared.checkLatestRelease() {
                    let current = UpdateService.currentVersion
                    if UpdateService.compareVersions(release.version, current) > 0 {
                        availableUpdate = release
                        statusMessage = String(format: L.t("Version {0}"), release.version)
                    } else {
                        availableUpdate = nil
                        statusMessage = String(format: L.t("DockHub {0} is the latest version."), current)
                    }
                } else {
                    statusMessage = L.t("Update failed")
                }
            } catch {
                statusMessage = L.t("Update failed")
            }
        }
    }
}
