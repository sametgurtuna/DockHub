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
    /// Kayitli kisayollar hangi ayarla kaydedildi; degismediyse yeniden kaydedilmez.
    private var registeredHotkeys: [String: String]?
    /// Uygulama ve saat kurallari (Windows: ProfileService.EvaluateRules).
    private var ruleState = ProfileRuleState()
    private var ruleTimer: Timer?
    private var ruleObservers: [NSObjectProtocol] = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Windows surumundeki --restore-taskbar acil cikisinin karsiligi.
        if DockLifecycle.handleRestoreArgument() {
            NSApp.terminate(nil)
            return
        }

        let service = ConfigService()
        configService = service
        Log.info("Yapilandirma: \(service.url.path)")

        // Arayuz dili: arayuz kurulmadan once, bir kez (Windows: L.Initialize).
        L.load(service.config.language)
        Log.info("Arayuz dili: \(L.code)")

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
        registerHotkeys()

        // Ekran duzeni degisimi: Dock gizlenmesi, cozunurluk, monitor takma/cikarma
        screenObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification,
            object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, let dock = self.dock, let cfg = self.configService?.config else { return }
                // Bu kadar ekran icin ayarlanmis profil (Windows: ApplyDisplayRule).
                if let id = Profiles.forDisplayCount(NSScreen.screens.count, cfg) {
                    Log.info("\(NSScreen.screens.count) ekran: profil degisiyor")
                    self.switchProfile(id, manual: false)
                    return
                }
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

        // Profil kurallari gozlemciler kurulduktan sonra: acilista bir kural gecis yaparsa dock yeniden kurulur.
        startProfileRules()

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
        panel.undoTitle = { [weak self] in
            guard let history = self?.configService?.history, history.canUndo else { return nil }
            return history.latest?.description
        }
        panel.onUndo = { [weak self] in self?.performUndo() }
        panel.profiles = { [weak self] in
            let config = self?.configService?.config
            return (list: config?.profiles ?? [], active: config?.activeProfileId)
        }
        panel.onSwitchProfile = { [weak self] id in self?.switchProfile(id, manual: true) }
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
        registerHotkeys()
        if cfg.taskbarMode != appliedMode {
            DockLifecycle.apply(cfg.taskbarMode)
            appliedMode = cfg.taskbarMode
        }
        guard cfg.layoutSignature() != builtSignature else { return }
        dock?.close()
        buildDock(service)
        Log.info("Ayarlar degisti, dock yeniden kuruldu")
    }

    // ---------------- Geri alma, profiller, kisayollar

    private func performUndo() {
        guard let service = configService else { return }
        do {
            if try service.undo() != nil {
                NotificationCenter.default.post(name: ConfigEvents.didChange, object: nil)
            }
        } catch {
            Log.error("Geri alinamadi", error)
        }
    }

    /// Profile gecer. Elle secim kurallardan once gelir (kurallar yeniden degisene kadar).
    private func switchProfile(_ id: String, manual: Bool) {
        guard let service = configService else { return }
        if manual { ruleState.manualSwitch() }
        var switched = false
        do {
            try service.update { switched = Profiles.switchTo(id, &$0) }
        } catch {
            Log.error("Profil kaydedilemedi", error)
        }
        if switched { NotificationCenter.default.post(name: ConfigEvents.didChange, object: nil) }
    }

    /// Uygulama acilip kapaninca ve dakikada bir kurallar yeniden degerlendirilir.
    private func startProfileRules() {
        let workspace = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.didLaunchApplicationNotification, NSWorkspace.didTerminateApplicationNotification] {
            ruleObservers.append(workspace.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.evaluateProfileRules() }
            })
        }
        ruleObservers.append(NotificationCenter.default.addObserver(
            forName: SettingsStore.profileSwitched, object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated { self?.ruleState.manualSwitch() }
        })
        ruleTimer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.evaluateProfileRules() }
        }
        if let config = configService?.config, let id = Profiles.forDisplayCount(NSScreen.screens.count, config) {
            switchProfile(id, manual: false)
        }
        evaluateProfileRules()
    }

    private func evaluateProfileRules() {
        guard let config = configService?.config else { return }
        if let id = ruleState.evaluate(config, runningApps: Self.runningAppNames(), now: Date()) {
            Log.info("Profil kurali: gecis")
            switchProfile(id, manual: false)
        }
    }

    /// Calisan uygulamalarin adlari, kural karsilastirmasi icin sadelestirilmis ("steam").
    private static func runningAppNames() -> Set<String> {
        var names = Set<String>()
        for app in NSWorkspace.shared.runningApplications where app.activationPolicy == .regular {
            if let name = app.localizedName { names.insert(ProfileRules.normalizeApp(name)) }
            if let url = app.bundleURL { names.insert(ProfileRules.normalizeApp(url.lastPathComponent)) }
            if let url = app.executableURL { names.insert(ProfileRules.normalizeApp(url.lastPathComponent)) }
        }
        return names
    }

    /// Kisayollari ayardan kaydeder; ayar degismediyse dokunmaz.
    private func registerHotkeys() {
        guard let config = configService?.config, config.hotkeys != registeredHotkeys else { return }
        registeredHotkeys = config.hotkeys
        GlobalHotkeys.shared.unregisterAll()
        for action in HotkeyActions.all {
            guard let gesture = HotkeyActions.gesture(action.id, in: config) else { continue }
            let id = action.id
            GlobalHotkeys.shared.register(gesture) { [weak self] in self?.perform(id) }
        }
    }

    private func perform(_ action: String) {
        switch action {
        case HotkeyActions.toggleDock:
            guard let dock else { return }
            if dock.panel.isVisible { dock.panel.orderOut(nil) } else { dock.show() }
        case HotkeyActions.openSettings:
            openSettings(.general)
        case HotkeyActions.toggleMute:
            if let id = AudioDevices.defaultOutputID() {
                AudioDevices.setMuted(!(AudioDevices.isMuted(id) ?? false), of: id)
            }
        case HotkeyActions.volumeUp, HotkeyActions.volumeDown:
            if let id = AudioDevices.defaultOutputID() {
                let step: Float = action == HotkeyActions.volumeUp ? 0.0625 : -0.0625
                AudioDevices.setVolume(min(max((AudioDevices.volume(of: id) ?? 0) + step, 0), 1), of: id)
            }
        case HotkeyActions.nextProfile:
            if let config = configService?.config, let id = Profiles.next(config) { switchProfile(id, manual: true) }
        default:
            break
        }
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
