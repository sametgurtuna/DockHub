import AppKit
import DockHubCore
import DockHubPlatform
import DockHubUI

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var dock: DockPanel?
    private var configService: ConfigService?
    private var themeObserver: NSObjectProtocol?
    private var screenObserver: NSObjectProtocol?
    private var signalSources: [DispatchSourceSignal] = []

    private var verifyMode: Bool { CommandLine.arguments.contains("--verify") }

    /// --activate <ad>: dock'a TIKLAMA ile ayni kod yolunu (model.activate)
    /// calistirir ve sonucu yazar. Fare olayi disinda her sey ayni akistir.
    private var activateTarget: String? {
        guard let i = CommandLine.arguments.firstIndex(of: "--activate"),
              i + 1 < CommandLine.arguments.count else { return nil }
        return CommandLine.arguments[i + 1]
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // --restore-dock: uygulama calismasa da sistem Dock ayarini geri yukler.
        // Windows surumundeki --restore-taskbar acil cikisinin karsiligi.
        if CommandLine.arguments.contains("--restore-dock") {
            let geri = SystemDock.restore()
            print(geri ? "Sistem Dock ayari geri yuklendi."
                       : "Geri yuklenecek kayit yok (session.json bos veya biz hic degistirmedik).")
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

        // taskbarMode Replace ise sistem Dock'u gizlenir ve bosalan kenar kullanilir.
        // Bu bir DEVRALMA degildir; fare kenara gidince Apple'in Dock'u yine belirir.
        // Onceki ayar session.json'a yazilir ve cikista geri yuklenir.
        if service.config.taskbarMode == .replace {
            SystemDock.hideAndRemember()
            // Beklemiyoruz: Dock yeniden baslayinca ekran duzeni degisir ve
            // asagidaki didChangeScreenParameters gozlemcisi konumu duzeltir.
        } else {
            // Replace'ten ShowBoth'a gecildiyse birakilmis ayar geri yuklenir.
            _ = SystemDock.restore()
        }

        installSignalHandlers()

        let panel = DockPanel(config: service.config, items: service.config.items)
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
                // CPU yuzdesi iki olcum arasindaki farktan cikar; bir tik bekleniyor.
                try? await Task.sleep(for: .seconds(4))
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
        if let sc = NSScreen.main {
            print("  ekranVisibleFrame : y=\(Int(sc.visibleFrame.minY)) h=\(Int(sc.visibleFrame.height))"
                  + "  (tam ekran h=\(Int(sc.frame.height)))")
        }
        let b = BatteryMonitor.shared.state
        print("  pil               : " + (b.hasBattery
              ? "%\(b.percent)  sarj=\(b.isCharging)  fis=\(b.isPluggedIn)  kalan=\(b.minutesRemaining.map{String($0)+" dk"} ?? "bilinmiyor")"
              : "pil yok"))
        if let w = WeatherStore.shared.reading {
            print("  hava              : \(String(format: "%.1f", w.temperatureC))C  kod=\(w.weatherCode)  \(w.description)  yer=\(w.place ?? "-")")
        } else {
            print("  hava              : okunmadi -> \(WeatherStore.shared.hata ?? "yukleniyor")")
        }
        let s = SystemMonitor.shared.sample
        print("  sistemOlcumu      : CPU %\(String(format: "%.1f", s.cpuPercent))"
              + "  RAM %\(String(format: "%.1f", s.memoryPercent))"
              + "  (\(s.memoryUsedBytes / 1_048_576) MB / \(s.memoryTotalBytes / 1_048_576) MB)")
        print("  SONUC             : \(ok ? "DOCK EKRANDA" : "DOCK GORUNMUYOR")")
    }

    /// Normal cikista sistem Dock ayari geri yuklenir (d-dock-geri-yukleme).
    func applicationWillTerminate(_ notification: Notification) {
        _ = SystemDock.restore()
    }

    /// OLCULMUS EKSIK: pkill/kill (SIGTERM) AppKit'in applicationWillTerminate
    /// cagrisini TETIKLEMEZ; surec temiz kapanmadan olur ve Dock ayari gizli kalir.
    /// Bu gozlemle eklendi: sinyali yakalayip once ayari geri yukluyoruz.
    /// SIGKILL (kill -9) ve elektrik kesintisi hala yakalanamaz; o durumda
    /// --restore-dock veya bir sonraki normal kosum devreye girer.
    private func installSignalHandlers() {
        for sig in [SIGTERM, SIGINT, SIGHUP] {
            signal(sig, SIG_IGN)                       // varsayilan olumu kapat
            let src = DispatchSource.makeSignalSource(signal: sig, queue: .main)
            src.setEventHandler {
                MainActor.assumeIsolated {
                    Log.info("Sinyal alindi (\(sig)); Dock ayari geri yukleniyor")
                    _ = SystemDock.restore()
                    exit(0)
                }
            }
            src.resume()
            signalSources.append(src)
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ s: NSApplication) -> Bool { false }
}
