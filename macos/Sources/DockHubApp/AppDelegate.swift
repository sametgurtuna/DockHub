import AppKit
import DockHubCore
import DockHubPlatform
import DockHubUI

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var dock: DockPanel?
    private var configService: ConfigService?
    private var themeObserver: NSObjectProtocol?

    /// wf-cli iskeleti: yalniz gercekten calisan argumanlar.
    private var verifyMode: Bool { CommandLine.arguments.contains("--verify") }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let service = ConfigService()
        configService = service

        Log.info("Yapilandirma: \(service.url.path) (varsayilan yazildi: \(service.didCreateDefaults))")

        let panel = DockPanel(config: service.config)
        dock = panel
        panel.show()

        themeObserver = Appearance.observeSystemTheme {
            Log.info("Sistem temasi degisti")
        }

        if verifyMode {
            // Pencerenin ekrana yerlesmesi icin bir tur bekle, sonra gozlemi yaz.
            Task { @MainActor in
                try? await Task.sleep(for: .milliseconds(1500))
                self.printObservation(service: service, panel: panel)
                NSApp.terminate(nil)
            }
        }
    }

    private func printObservation(service: ConfigService, panel: DockPanel) {
        let c = service.config
        print("--- Gozlem ---")
        print("  configPath        : \(service.url.path)")
        print("  varsayilanYazildi : \(service.didCreateDefaults)")
        print("  edge              : \(c.edge.rawValue)")
        print("  size              : \(c.size.rawValue) (\(Int(c.size.thickness)) pt)")
        print("  layout            : \(c.layout.rawValue)")
        print("  widthMode         : \(c.widthMode.rawValue)")
        print("  alignment         : \(c.alignment.rawValue)")
        print("  theme             : \(c.theme.rawValue)")
        print("  backdrop          : \(c.backdrop.rawValue)")
        print("  edgeMargin        : \(Int(c.clampedEdgeMargin))")
        for (k, v) in panel.observation().sorted(by: { $0.key < $1.key }) {
            print("  \(k.padding(toLength: 18, withPad: " ", startingAt: 0)): \(v)")
        }
        let ok = panel.panel.isVisible && panel.panel.occlusionState.contains(.visible)
        print("  SONUC             : \(ok ? "DOCK EKRANDA" : "DOCK GORUNMUYOR")")
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ s: NSApplication) -> Bool { false }
}
