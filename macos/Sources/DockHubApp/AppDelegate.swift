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
    private var settingsStore: SettingsStore?
    private var settingsWindow: SettingsWindowController?
    private var themeObserver: NSObjectProtocol?
    private var screenObserver: NSObjectProtocol?
    private var configObserver: NSObjectProtocol?
    /// Son kurulan dock'un yerlesim imzasi ve kipi; degismediyse yeniden kurulmaz.
    private var builtSignature = Data()
    private var appliedMode: TaskbarMode?

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
        appliedMode = service.config.taskbarMode
        DockLifecycle.installSignalHandlers()
        Task { @MainActor in await Notifier.setup() }

        let panel = buildDock(service)
        settingsStore = SettingsStore(service: service)

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

        // Ayarlar penceresi config'i degistirdi (wf-settings-ui).
        configObserver = NotificationCenter.default.addObserver(
            forName: ConfigEvents.didChange, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.applyConfigChange() }
        }

        themeObserver = Appearance.observeSystemTheme {
            Log.info("Sistem temasi degisti")
        }

        if CommandLine.arguments.contains("--settings") { openSettings(.general) }

        if !SelfTests.start(service: service, panel: panel), let store = settingsStore {
            SelfTests.startSettingsTests(store: store, dock: { [weak self] in self?.dock },
                                         settings: { [weak self] in self?.settingsController() })
        }
    }

    /// Dock'u config'ten kurar ve gosterir; yerlesim imzasini kaydeder.
    @discardableResult
    private func buildDock(_ service: ConfigService) -> DockPanel {
        let panel = DockPanel(config: service.config, items: service.config.items, service: service)
        panel.onOpenSettings = { [weak self] page in self?.openSettings(page) }
        dock = panel
        builtSignature = service.config.layoutSignature()
        panel.show()
        return panel
    }

    /// Ayar degisikligini uygular. Yalniz yerlesimi etkileyen degisiklik
    /// dock'u yeniden kurar (AppConfig.layoutSignature); Windows'taki
    /// DockWindow.ApplySettings karsiligi.
    private func applyConfigChange() {
        guard let service = configService else { return }
        let cfg = service.config
        if cfg.taskbarMode != appliedMode {
            DockLifecycle.apply(cfg.taskbarMode)
            appliedMode = cfg.taskbarMode
        }
        guard cfg.layoutSignature() != builtSignature else { return }
        dock?.close()
        buildDock(service)
        Log.info("Ayarlar degisti, dock yeniden kuruldu")
    }

    private func settingsController() -> SettingsWindowController? {
        guard let store = settingsStore else { return nil }
        if settingsWindow == nil { settingsWindow = SettingsWindowController(store: store) }
        return settingsWindow
    }

    private func openSettings(_ page: SettingsPage) {
        settingsController()?.show(page)
    }

    /// Normal cikista sistem Dock ayari geri yuklenir (d-dock-geri-yukleme).
    func applicationWillTerminate(_ notification: Notification) {
        DockLifecycle.restoreOnExit()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ s: NSApplication) -> Bool { false }
}
