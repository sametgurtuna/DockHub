import Foundation

/// Ilk calistirmada dock'a konacak varsayilan uygulamalar.
/// Windows karsiligi: Shell/DefaultItems.cs
/// Yalniz gercekten var olan uygulamalar eklenir; olmayanlar sessizce atlanir.
public enum DefaultItems {
    static let candidates: [(path: String, name: String)] = [
        ("/System/Library/CoreServices/Finder.app", "Finder"),
        ("/Applications/Safari.app", "Safari"),
        ("/System/Applications/Mail.app", "Mail"),
        ("/System/Applications/Notes.app", "Notlar"),
        ("/System/Applications/Music.app", "Müzik"),
        ("/System/Applications/System Settings.app", "Sistem Ayarları"),
    ]

    public static func build(fileExists: (String) -> Bool = {
        FileManager.default.fileExists(atPath: $0)
    }) -> [DockItem] {
        candidates.filter { fileExists($0.path) }
                  .map { DockItem.app($0.path, name: $0.name) }
    }
}
