import Foundation

/// Ilk calistirmada dock'a konacak varsayilan uygulamalar.
/// Windows karsiligi: Shell/DefaultItems.cs
/// Yalniz gercekten var olan uygulamalar eklenir; olmayanlar sessizce atlanir.
///
/// Ad yazilmaz (name = nil): Windows'ta oldugu gibi bos ad uygulamanin kendi
/// adina duser, macOS'ta bu sistem dilinde gorunen addir.
public enum DefaultItems {
    static let paths: [String] = [
        "/System/Library/CoreServices/Finder.app",
        "/Applications/Safari.app",
        "/System/Applications/Mail.app",
        "/System/Applications/Notes.app",
        "/System/Applications/Music.app",
        "/System/Applications/System Settings.app",
    ]

    public static func build(fileExists: (String) -> Bool = {
        FileManager.default.fileExists(atPath: $0)
    }) -> [DockItem] {
        paths.filter(fileExists).map { DockItem.app($0) }
    }

    /// Onceki surumlerin varsayilan olarak yazdigi Turkce adlar. Yalniz ayni
    /// yol + ayni ad eslesirse silinir; kullanicinin verdigi adlara dokunulmaz.
    static let legacyNames: [String: String] = [
        "/System/Applications/Notes.app": "Notlar",
        "/System/Applications/Music.app": "Müzik",
        "/System/Applications/System Settings.app": "Sistem Ayarları",
    ]

    public static func migrateLegacyNames(_ items: [DockItem]) -> (items: [DockItem], changed: Bool) {
        var changed = false
        let out = items.map { item -> DockItem in
            var it = item
            if it.kind == .app, let p = it.path, let eski = legacyNames[p], it.name == eski {
                it.name = nil; changed = true
            }
            if let children = it.children {
                let sonuc = migrateLegacyNames(children)
                if sonuc.changed { it.children = sonuc.items; changed = true }
            }
            return it
        }
        return (out, changed)
    }
}
