import AppKit
import Foundation
import DockHubCore

/// macOS Kisayollar uygulamasi. Windows surumunde KARSILIGI YOK - kullanici
/// istegiyle eklenen parite disi ek (origin: ek).
/// Eslestirme ag-shortcuts-run: /usr/bin/shortcuts CLI.
public enum ShortcutsService {
    public static let cli = "/usr/bin/shortcuts"

    public static var kullanilabilir: Bool {
        FileManager.default.isExecutableFile(atPath: cli)
    }

    /// Kurulu kisayollarin adlari.
    public static func list() -> [String] {
        guard kullanilabilir else { return [] }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: cli)
        p.arguments = ["list"]
        let pipe = Pipe(); p.standardOutput = pipe; p.standardError = Pipe()
        guard (try? p.run()) != nil else { return [] }
        let d = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return (String(data: d, encoding: .utf8) ?? "")
            .split(separator: "\n").map(String.init)
            .filter { !$0.isEmpty }
    }

    public enum Sonuc: Sendable {
        case calisti
        case bulunamadi
        case zamanAsimi
        case hata(String)
    }

    /// Kisayolu calistirir.
    ///
    /// OLCULMUS DAVRANIS: 'shortcuts run <olmayan-ad>' hata DONDURMUYOR, sureci
    /// ASILI birakiyor. waitUntilExit() kullanmak widget'i dondururdu; bu yuzden
    /// zaman asimi var ve suresi dolan surec terminate ediliyor.
    /// Kisayolun kendisi uzun surebilecegi icin varsayilan sure comert tutuldu.
    public static func run(_ ad: String, timeout: TimeInterval = 30) -> Sonuc {
        guard kullanilabilir else { return .bulunamadi }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: cli)
        p.arguments = ["run", ad]
        let err = Pipe(); p.standardError = err; p.standardOutput = Pipe()
        do { try p.run() } catch { return .hata(error.localizedDescription) }

        let bitis = Date().addingTimeInterval(timeout)
        while p.isRunning && Date() < bitis { usleep(100_000) }
        if p.isRunning {
            p.terminate()
            usleep(200_000)
            if p.isRunning { kill(p.processIdentifier, SIGKILL) }
            return .zamanAsimi
        }
        if p.terminationStatus == 0 { return .calisti }
        let metin = String(data: err.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
        return .hata(metin.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                     ? "çıkış kodu \(p.terminationStatus)" : metin)
    }
}

/// AirDrop. Windows surumunde KARSILIGI YOK - parite disi ek (origin: ek).
/// Eslestirme ag-airdrop-send: AppKit NSSharingService(named: .sendViaAirDrop).
public enum AirDropService {
    /// AirDrop paylasim hizmeti bu makinede kullanilabilir mi?
    public static func kullanilabilir(for items: [Any]) -> Bool {
        NSSharingService(named: .sendViaAirDrop)?.canPerform(withItems: items) ?? false
    }

    /// Dosyalari AirDrop ile gonderir. macOS kendi alici secme penceresini acar;
    /// kime gonderilecegini KULLANICI secer, biz secmeyiz.
    @discardableResult
    public static func send(_ urls: [URL]) -> Bool {
        guard !urls.isEmpty, let servis = NSSharingService(named: .sendViaAirDrop) else { return false }
        guard servis.canPerform(withItems: urls) else {
            Log.error("AirDrop bu ogeler icin kullanilamiyor")
            return false
        }
        servis.perform(withItems: urls)
        return true
    }
}
