import AppKit
import DockHubCore
import DockHubPlatform

/// Sistem Dock'unu gizleme ve her cikis yolunda geri yukleme (d-dock-geri-yukleme).
/// Windows karsiligi: Shell/TaskbarController.cs ve --restore-taskbar.
///
/// AppDelegate'ten ayri dosyada: T7-DOCK-DEVRALMA'nin kaniti bu davranisa
/// bagli, uygulama kablolamasi (ayarlar, menuler) degistikce eskimesin.
@MainActor
enum DockLifecycle {
    private static var signalSources: [DispatchSourceSignal] = []

    /// --restore-dock: uygulama calismasa da sistem Dock ayarini geri yukler.
    /// Argüman varsa isi yapip true doner; cagiran uygulamayi kapatmali.
    static func handleRestoreArgument() -> Bool {
        guard CommandLine.arguments.contains("--restore-dock") else { return false }
        let geri = SystemDock.restore()
        print(geri ? "macOS Dock setting restored."
                   : "Nothing to restore (session.json is empty or DockHub never changed the setting).")
        return true
    }

    /// taskbarMode Replace ise sistem Dock'u gizlenir ve bosalan kenar kullanilir.
    /// Bu bir DEVRALMA degildir; fare kenara gidince Apple'in Dock'u yine belirir.
    /// Onceki ayar session.json'a yazilir ve cikista geri yuklenir.
    static func apply(_ mode: TaskbarMode) {
        if mode == .replace {
            SystemDock.hideAndRemember()
            // Beklenmiyor: Dock yeniden baslayinca ekran duzeni degisir ve
            // didChangeScreenParameters gozlemcisi konumu duzeltir.
        } else {
            // Replace'ten ShowBoth'a gecildiyse birakilmis ayar geri yuklenir.
            _ = SystemDock.restore()
        }
    }

    /// Normal cikis (applicationWillTerminate).
    static func restoreOnExit() {
        _ = SystemDock.restore()
    }

    /// OLCULMUS EKSIK: pkill/kill (SIGTERM) AppKit'in applicationWillTerminate
    /// cagrisini TETIKLEMEZ; surec temiz kapanmadan olur ve Dock ayari gizli kalir.
    /// Bu gozlemle eklendi: sinyali yakalayip once ayari geri yukluyoruz.
    /// SIGKILL (kill -9) ve elektrik kesintisi hala yakalanamaz; o durumda
    /// --restore-dock veya bir sonraki normal kosum devreye girer.
    static func installSignalHandlers() {
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
}
