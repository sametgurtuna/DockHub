import AppKit
import DockHubCore

/// Uygulama ikonu, baslatma ve calisiyor bilgisi.
/// Windows karsiligi: Native/ShellIcons.cs + Services/AppLauncher.cs + Shell/RunningAppsService.cs
/// Eslestirmeler: ag-app-icons (mapped), ag-app-launch (mapped)
@MainActor
public enum AppCatalog {

    /// ag-app-icons: NSWorkspace.icon(forFile:)
    public static func icon(forAppAt path: String) -> NSImage? {
        guard FileManager.default.fileExists(atPath: path) else { return nil }
        return NSWorkspace.shared.icon(forFile: path)
    }

    /// Paketin bundle kimligi. Calisan uygulamayla eslestirmede kullanilir;
    /// Windows'taki AppUserModelId eslestirmesinden daha guvenilir.
    public static func bundleIdentifier(forAppAt path: String) -> String? {
        Bundle(url: URL(fileURLWithPath: path))?.bundleIdentifier
    }

    /// ag-running-apps'in izin gerektirmeyen kismi: uygulama duzeyi.
    /// Pencere duzeyi denetim (AXUIElement) bu gorevin kapsaminda degil.
    public static func runningApplication(forAppAt path: String) -> NSRunningApplication? {
        guard let id = bundleIdentifier(forAppAt: path) else { return nil }
        return NSWorkspace.shared.runningApplications.first { $0.bundleIdentifier == id }
    }

    public static func isRunning(appAt path: String) -> Bool {
        runningApplication(forAppAt: path) != nil
    }

    /// ag-app-launch: calisiyorsa one getirir, calismiyorsa baslatir.
    /// Windows surumundeki "tiklama one getirir veya baslatir" davranisinin karsiligi.
    public static func activateOrLaunch(appAt path: String,
                                        completion: (@Sendable (Bool) -> Void)? = nil) {
        if let running = runningApplication(forAppAt: path) {
            let ok = running.activate(options: [])
            completion?(ok)
            return
        }
        let config = NSWorkspace.OpenConfiguration()
        config.activates = true
        NSWorkspace.shared.openApplication(at: URL(fileURLWithPath: path),
                                           configuration: config) { app, error in
            if let error { Log.error("Uygulama baslatilamadi: \(path)", error) }
            completion?(app != nil)
        }
    }
}
