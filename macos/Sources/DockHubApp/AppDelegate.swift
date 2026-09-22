import AppKit
import DockHubCore
import DockHubPlatform
import DockHubUI

/// Yalniz baglama (MIMARI.md bolum 2). Sistem Dock'u yasam dongusu
/// DockLifecycle'da, komut satiri oz-testleri SelfTests'te.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var dock: DockPanel?
    private var configService: ConfigService?
    private var themeObserver: NSObjectProtocol?
    private var screenObserver: NSObjectProtocol?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Windows surumundeki --restore-taskbar acil cikisinin karsiligi.
        if DockLifecycle.handleRestoreArgument() {
            NSApp.terminate(nil)
            return
        }

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

        DockLifecycle.apply(service.config.taskbarMode)
        DockLifecycle.installSignalHandlers()
        Task { @MainActor in await Notifier.setup() }

        let panel = DockPanel(config: service.config, items: service.config.items, service: service)
        dock = panel
        panel.show()

        // Ekran duzeni degisimi: Dock gizlenmesi, cozunurluk, monitor takma/cikarma
        screenObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification,
            object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, let dock = self.dock, let cfg = self.configService?.config else { return }
                if dock.reposition(config: cfg) {
                    Log.info("Ekran duzeni degisti, dock yeniden konumlandi")
                }
            }
        }

        themeObserver = Appearance.observeSystemTheme {
            Log.info("Sistem temasi degisti")
        }

        _ = SelfTests.start(service: service, panel: panel)
    }

    /// Normal cikista sistem Dock ayari geri yuklenir (d-dock-geri-yukleme).
    func applicationWillTerminate(_ notification: Notification) {
        DockLifecycle.restoreOnExit()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ s: NSApplication) -> Bool { false }
}
