import Foundation

/// Arayuz dili, adi kendi dilinde.
public struct UiLanguageInfo: Sendable {
    public let language: UiLanguage
    public let code: String
    public let nativeName: String
}

/// Arayuz cevirisi. Windows'taki L ile ayni sozlesme (Core/Localizer.cs):
/// anahtar Ingilizce metin, ceviri `Strings_<kod>.json` icinde (tr, de, es).
/// Dosyalar Windows'unkilerle AYNI (src/CustomDock/Resources); Scripts/bundle.sh
/// onlari .app'in Resources klasorune kopyalar. Cevirisi olmayan metin Ingilizce
/// kalir. Dil acilista bir kez yuklenir; degisiklik yeniden baslatinca gecerli
/// olur (Windows'ta da oyle).
public enum L {
    public static let languages: [UiLanguageInfo] = [
        UiLanguageInfo(language: .english, code: "en", nativeName: "English"),
        UiLanguageInfo(language: .turkish, code: "tr", nativeName: "Türkçe"),
        UiLanguageInfo(language: .german, code: "de", nativeName: "Deutsch"),
        UiLanguageInfo(language: .spanish, code: "es", nativeName: "Español"),
    ]

    private final class Store: @unchecked Sendable {
        let lock = NSLock()
        var strings: [String: String] = [:]
        var code = "en"
    }

    private static let store = Store()

    /// Etkin dilin iki harfli kodu ("en", "tr", "de" ya da "es").
    public static var code: String {
        store.lock.lock(); defer { store.lock.unlock() }
        return store.code
    }

    /// C#: CodeFor. Secilen dil; System'de sistemin tercih ettigi diller
    /// arasinda DockHub'in bildigi ilki (macOS'un kendi eslemesi gibi), yoksa Ingilizce.
    public static func code(for language: UiLanguage, systemLanguages: [String]) -> String {
        if language != .system {
            return languages.first { $0.language == language }?.code ?? "en"
        }
        for identifier in systemLanguages {
            let two = identifier.split(whereSeparator: { $0 == "-" || $0 == "_" }).first.map { $0.lowercased() } ?? ""
            if let match = languages.first(where: { $0.code == two }) { return match.code }
        }
        return "en"
    }

    /// Ayardaki dili yukler. `directory` Strings_*.json dosyalarinin klasoru.
    public static func load(_ language: UiLanguage,
                            systemLanguages: [String] = Locale.preferredLanguages,
                            directory: URL? = defaultDirectory()) {
        let c = code(for: language, systemLanguages: systemLanguages)
        let table = c == "en" ? [:] : (directory.map { strings(code: c, in: $0) } ?? [:])
        store.lock.lock(); defer { store.lock.unlock() }
        store.code = c
        store.strings = table
    }

    /// Bir dilin ceviri tablosu. Dosyada yorum satirlari var (Windows
    /// ReadCommentHandling.Skip ile okur); JSON5 kipi onlari atlar.
    public static func strings(code: String, in directory: URL) -> [String: String] {
        let url = directory.appendingPathComponent("Strings_\(code).json")
        do {
            let decoder = JSONDecoder()
            decoder.allowsJSON5 = true
            return try decoder.decode([String: String].self, from: Data(contentsOf: url))
        } catch {
            Log.error("Ceviri okunamadi: \(url.path)", error)
            return [:]
        }
    }

    /// .app icinde Resources; `swift run` ile depodan calisirken Windows'un kaynak klasoru.
    public static func defaultDirectory() -> URL? {
        let fm = FileManager.default
        func has(_ dir: URL) -> Bool { fm.fileExists(atPath: dir.appendingPathComponent("Strings_tr.json").path) }
        if let resources = Bundle.main.resourceURL, has(resources) { return resources }
        var dir = Bundle.main.executableURL?.deletingLastPathComponent()
        while let d = dir, d.pathComponents.count > 1 {
            let candidate = d.appendingPathComponent("src/CustomDock/Resources")
            if has(candidate) { return candidate }
            dir = d.deletingLastPathComponent()
        }
        return nil
    }

    /// Cevrilmis metin; cevirisi yoksa Ingilizcesi.
    public static func t(_ english: String) -> String {
        store.lock.lock(); defer { store.lock.unlock() }
        return english.isEmpty ? english : (store.strings[english] ?? english)
    }

    /// Bicimli metin: `t("{0} items", 3)`. Yer tutucular Windows'taki gibi {0}, {1}.
    public static func t(_ english: String, _ args: Any...) -> String {
        format(t(english), args.map { "\($0)" })
    }

    /// C# string.Format'in DockHub'in kullandigi kadari: {0}, {1:bicim} (bicim
    /// yok sayilir; degerler metin olarak hazir gelir), {{ ve }}.
    static func format(_ text: String, _ args: [String]) -> String {
        var out = ""
        var i = text.startIndex
        while i < text.endIndex {
            let c = text[i]
            let next = text.index(after: i)
            if c == "{", next < text.endIndex, text[next] == "{" { out.append("{"); i = text.index(after: next); continue }
            if c == "}", next < text.endIndex, text[next] == "}" { out.append("}"); i = text.index(after: next); continue }
            if c == "{", let close = text[next...].firstIndex(of: "}") {
                let inside = text[next..<close]
                let number = inside.split(separator: ":", maxSplits: 1).first.map(String.init) ?? ""
                if let n = Int(number), n >= 0, n < args.count {
                    out += args[n]
                    i = text.index(after: close)
                    continue
                }
            }
            out.append(c)
            i = next
        }
        return out
    }
}
