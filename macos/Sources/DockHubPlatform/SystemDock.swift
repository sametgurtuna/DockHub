import AppKit
import DockHubCore

/// Sistem Dock'unun otomatik gizleme ayarini okur ve degistirir.
///
/// ONEMLI SINIR: macOS'ta sistem Dock'unun YERINI ALMAK mumkun degildir.
/// Windows'taki Shell_TrayWnd devralma (ManagedShell) karsiligi YOKTUR.
/// Yapabildigimiz tek sey Dock'u otomatik gizlemeye almak ve bosalan kenari
/// kullanmaktir. Fare ekranin kenarina gidince Apple'in Dock'u yine belirir.
/// Bu bir "devralma" degil "yer degistirme"dir (ag-taskbar-takeover: partial).
///
/// NSApplication.presentationOptions = .hideDock burada ise yaramaz: yalniz
/// kendi uygulamamiz ON PLANDA iken etkilidir, biz ise .accessory olarak
/// hic one gelmeyen bir uygulamayiz.
@MainActor
public enum SystemDock {
    private static let domain = "com.apple.dock"
    private static let key = "autohide"

    /// Sistem Dock'u su an otomatik gizlemede mi?
    public static func isAutoHideEnabled() -> Bool? {
        guard let out = run("/usr/bin/defaults", ["read", domain, key]) else { return nil }
        let v = out.trimmingCharacters(in: .whitespacesAndNewlines)
        if v == "1" || v.lowercased() == "true" { return true }
        if v == "0" || v.lowercased() == "false" { return false }
        return nil
    }

    /// Ayari degistirir ve Dock'u yeniden baslatir (degisiklik ancak boyle gecerli olur).
    @discardableResult
    public static func setAutoHide(_ enabled: Bool) -> Bool {
        guard run("/usr/bin/defaults",
                  ["write", domain, key, "-bool", enabled ? "true" : "false"]) != nil else {
            Log.error("Dock autohide ayari yazilamadi")
            return false
        }
        _ = run("/usr/bin/killall", ["Dock"])
        return true
    }

    /// Dock'u gizle ve onceki ayari session.json'a yaz.
    /// Zaten kayitli bir onceki deger varsa uzerine YAZILMAZ; ilk deger korunur,
    /// yoksa arka arkaya kosumlarda gercek baslangic durumu kaybolur.
    public static func hideAndRemember() {
        var session = SessionState.load()
        if session.dockAutoHideWasEnabled == nil {
            session.dockAutoHideWasEnabled = isAutoHideEnabled() ?? false
            session.savedAt = Date()
            session.save()
            Log.info("Dock onceki autohide degeri kaydedildi: \(session.dockAutoHideWasEnabled!)")
        }
        setAutoHide(true)
    }

    /// Kayitli onceki ayari geri yukler. Uygulama calismasa da calisir.
    /// Geri yukleyecek bir sey yoksa false doner.
    @discardableResult
    public static func restore() -> Bool {
        let session = SessionState.load()
        guard let previous = session.dockAutoHideWasEnabled else { return false }
        setAutoHide(previous)
        SessionState.clear()
        Log.info("Dock autohide ayari geri yuklendi: \(previous)")
        return true
    }

    // ---- yardimci
    @discardableResult
    private static func run(_ path: String, _ args: [String]) -> String? {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = args
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = Pipe()
        do { try p.run() } catch { return nil }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        guard p.terminationStatus == 0 else { return nil }
        return String(data: data, encoding: .utf8)
    }
}
