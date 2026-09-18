import AppKit
import DockHubCore
import DockHubPlatform
import DockHubUI

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var dock: DockPanel?
    private var configService: ConfigService?
    private var themeObserver: NSObjectProtocol?

    private var verifyMode: Bool { CommandLine.arguments.contains("--verify") }

    /// --activate <ad>: dock'a TIKLAMA ile ayni kod yolunu (model.activate)
    /// calistirir ve sonucu yazar. Fare olayi disinda her sey ayni akistir.
    private var activateTarget: String? {
        guard let i = CommandLine.arguments.firstIndex(of: "--activate"),
              i + 1 < CommandLine.arguments.count else { return nil }
        return CommandLine.arguments[i + 1]
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let service = ConfigService()
        configService = service
        Log.info("Yapilandirma: \(service.url.path)")

        // Ilk calistirmada dock bos olmasin: var olan varsayilan uygulamalar eklenir.
        if service.config.items.isEmpty {
            let defaults = DefaultItems.build()
            if !defaults.isEmpty {
                try? service.update { $0.items = defaults }
                Log.info("Varsayilan \(defaults.count) uygulama eklendi")
            }
        }

        let panel = DockPanel(config: service.config, items: service.config.items)
        dock = panel
        panel.show()

        themeObserver = Appearance.observeSystemTheme {
            Log.info("Sistem temasi degisti")
        }

        if let hedef = activateTarget {
            Task { @MainActor in
                try? await Task.sleep(for: .milliseconds(400))
                guard let item = panel.model.items.first(where: {
                    ($0.name ?? "").localizedCaseInsensitiveContains(hedef)
                }) else {
                    print("BULUNAMADI: \(hedef)"); NSApp.terminate(nil); return
                }
                let onceCalisiyor = item.path.map { panel.model.runningPaths.contains($0) } ?? false
                print("--- Tiklama testi ---")
                print("  hedef            : \(item.name ?? "-")  (\(item.path ?? "-"))")
                print("  once calisiyor mu: \(onceCalisiyor)")
                panel.model.activate(item)               // tiklamanin cagirdigi ayni fonksiyon
                try? await Task.sleep(for: .seconds(3))
                panel.model.refreshRunning()
                let sonra = item.path.map { panel.model.runningPaths.contains($0) } ?? false
                let on = item.path.flatMap { AppCatalog.runningApplication(forAppAt: $0) }?.isActive ?? false
                print("  sonra calisiyor mu: \(sonra)")
                print("  one geldi mi      : \(on)")
                print("  SONUC             : \(sonra ? "UYGULAMA ACILDI/ONE GELDI" : "ACILMADI")")
                NSApp.terminate(nil)
            }
            return
        }

        if verifyMode {
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
        print("  edge / size       : \(c.edge.rawValue) / \(c.size.rawValue)")
        print("  layout / theme    : \(c.layout.rawValue) / \(c.theme.rawValue)")
        for (k, v) in panel.observation().sorted(by: { $0.key < $1.key }) {
            print("  \(k.padding(toLength: 18, withPad: " ", startingAt: 0)): \(v)")
        }
        print("  ogeler            :")
        for i in panel.model.items {
            let running = i.path.map { panel.model.runningPaths.contains($0) } ?? false
            print("      \(i.kind.rawValue.padding(toLength: 10, withPad: " ", startingAt: 0)) "
                  + "\((i.name ?? i.widget ?? "-").padding(toLength: 18, withPad: " ", startingAt: 0)) "
                  + "\(running ? "[calisiyor]" : "")")
        }
        let ok = panel.panel.isVisible && panel.panel.occlusionState.contains(.visible)
        print("  SONUC             : \(ok ? "DOCK EKRANDA" : "DOCK GORUNMUYOR")")
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ s: NSApplication) -> Bool { false }
}
