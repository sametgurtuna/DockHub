import Foundation

/// macOS Dock'unda sabitlenmis uygulamalar. Windows karsiligi: ayarlardaki
/// "Import taskbar pins" (Shell/DefaultItems.cs gorev cubugu sabitlerini okur).
///
/// Kaynak com.apple.dock tercihlerindeki "persistent-apps" dizisi; her ogenin
/// tile-data > file-data > _CFURLString alani uygulamanin file:// adresi.
/// Baska uygulamanin tercihini okumak sandbox disi uygulamada izin istemez.
public enum SystemDockApps {
    public static func pinnedAppPaths() -> [String] {
        guard let liste = CFPreferencesCopyAppValue("persistent-apps" as CFString,
                                                    "com.apple.dock" as CFString) as? [[String: Any]]
        else { return [] }
        return liste.compactMap { oge -> String? in
            guard let tile = oge["tile-data"] as? [String: Any],
                  let file = tile["file-data"] as? [String: Any],
                  let adres = file["_CFURLString"] as? String,
                  let url = URL(string: adres), url.isFileURL else { return nil }
            let yol = url.path
            return yol.hasSuffix(".app") && FileManager.default.fileExists(atPath: yol) ? yol : nil
        }
    }
}
