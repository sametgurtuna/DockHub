import AppKit
import Foundation
import DockHubCore

public struct NowPlaying: Sendable, Equatable {
    public let app: String
    public let title: String
    public let artist: String
    public let playing: Bool
}

/// Calan medya. Windows karsiligi: Services/MediaService.cs (SMTC - HER uygulama).
/// Eslestirme ag-media-control (partial):
///   - MPNowPlayingInfoCenter yalniz KENDI uygulamamizi yayinlar, baskasini okumaz
///   - MediaRemote private framework -> kullanilmiyor (d-media-kapsami)
///   - Genel yol: AppleScript ile Music ve Spotify. Tarayici ve VLC KAPSAM DISI.
/// Otomasyon izni gerekir; verilmezse uydurma sarki gosterilmez, durum bildirilir.
public enum NowPlayingService {
    public enum Sonuc: Sendable {
        case calan(NowPlaying)
        case hicbiriCalmiyor
        case izinYok(String)
    }

    private static let hedefler = [
        ("Spotify", "com.spotify.client"),
        ("Music", "com.apple.Music"),
    ]

    /// SADECE calisan uygulamalara sorulur. 'tell application' calismayan bir
    /// uygulamayi BASLATIR; bunu istemiyoruz, o yuzden once NSWorkspace'e bakilir.
    public static func read() -> Sonuc {
        let calisan = Set(NSWorkspace.shared.runningApplications.compactMap(\.bundleIdentifier))
        var izinHatasi: String?

        for (ad, bundleID) in hedefler where calisan.contains(bundleID) {
            let script = """
            tell application "\(ad)"
                if player state is playing then
                    return (name of current track) & "\\n" & (artist of current track)
                else
                    return ""
                end if
            end tell
            """
            var hata: NSDictionary?
            let sonuc = NSAppleScript(source: script)?.executeAndReturnError(&hata)
            if let hata {
                // -1743 = kullanici Otomasyon iznini vermedi
                izinHatasi = (hata["NSAppleScriptErrorBriefMessage"] as? String)
                    ?? "AppleScript error"
                continue
            }
            guard let metin = sonuc?.stringValue, !metin.isEmpty else { continue }
            let p = metin.components(separatedBy: "\n")
            return .calan(NowPlaying(app: ad, title: p.first ?? "",
                                     artist: p.count > 1 ? p[1] : "", playing: true))
        }
        if let izinHatasi { return .izinYok(izinHatasi) }
        return .hicbiriCalmiyor
    }

    public static func komut(_ ad: String, _ eylem: String) {
        let calisan = Set(NSWorkspace.shared.runningApplications.compactMap(\.bundleIdentifier))
        let bundleID = ad == "Spotify" ? "com.spotify.client" : "com.apple.Music"
        guard calisan.contains(bundleID) else { return }
        var hata: NSDictionary?
        NSAppleScript(source: "tell application \"\(ad)\" to \(eylem)")?
            .executeAndReturnError(&hata)
    }
}
