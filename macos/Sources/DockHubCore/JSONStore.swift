import Foundation

/// JSON dosyalarini atomik okur/yazar.
/// Windows karsiligi: Core/JsonStore.cs
///  - WriteIndented = true              -> .prettyPrinted
///  - atomik .tmp + tasima              -> Data.write(options: .atomic)
///
/// Anahtar sirasi: C# System.Text.Json alanlari BILDIRIM sirasinda yazar.
/// Swift'in JSONEncoder'i keyed container'da encode(to:) sirasini KORUMAZ,
/// cikti sirasi belirsizdir. Bu yuzden .sortedKeys ile alfabetik ve
/// deterministik yaziyoruz: sira Windows ciktisindan farkli olur ama JSON
/// anahtar sirasindan bagimsiz oldugu icin sema uyumu etkilenmez; deterministik
/// olmasi dosya farklarini (diff) okunur tutar.
public enum JSONStore {
    public static let encoder: JSONEncoder = {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .withoutEscapingSlashes, .sortedKeys]
        return e
    }()

    public static let decoder = JSONDecoder()

    public static func load<T: Decodable>(_ type: T.Type, from url: URL) -> T? {
        guard FileManager.default.fileExists(atPath: url.path) else { return nil }
        do {
            return try decoder.decode(type, from: Data(contentsOf: url))
        } catch {
            Log.error("JSON okunamadi: \(url.path)", error)
            backupCorrupt(url)
            return nil
        }
    }

    public static func save<T: Encodable>(_ value: T, to url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                withIntermediateDirectories: true)
        let data = try encoder.encode(value)
        try data.write(to: url, options: .atomic)
    }

    private static func backupCorrupt(_ url: URL) {
        let bad = url.appendingPathExtension("bad")
        try? FileManager.default.removeItem(at: bad)
        try? FileManager.default.moveItem(at: url, to: bad)
    }
}
