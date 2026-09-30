import Foundation

/// ~/Library/Application Support/DockHub altindaki dosya yollari.
/// Windows karsiligi: Core/AppPaths.cs (%AppData%\DockHub).
/// ag-config-path eslestirmesi: FileManager .applicationSupportDirectory.
public enum AppPaths {
    public static let folderName = "DockHub"

    /// DOCKHUB_HOME ile gecersiz kilinabilir (test ve tasinabilir kullanim).
    /// Windows surumundeki ayni davranis korunuyor.
    /// Hesaplanan ozellik: Swift 6 kati eszamanlilik degisken global duruma
    /// izin vermiyor, ayrica ortam degiskeni degisimi aninda yansisin.
    public static var root: URL {
        if let custom = ProcessInfo.processInfo.environment["DOCKHUB_HOME"], !custom.isEmpty {
            return URL(fileURLWithPath: custom).standardizedFileURL
        }
        let base = try? FileManager.default.url(for: .applicationSupportDirectory,
                                                in: .userDomainMask,
                                                appropriateFor: nil, create: false)
        return (base ?? URL(fileURLWithPath: NSHomeDirectory())
            .appendingPathComponent("Library/Application Support"))
            .appendingPathComponent(folderName)
    }

    public static var config: URL { root.appendingPathComponent("config.json") }
    public static var session: URL { root.appendingPathComponent("session.json") }
    public static var logFile: URL { root.appendingPathComponent("dockhub.log") }
}
