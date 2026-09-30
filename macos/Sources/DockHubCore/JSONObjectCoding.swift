import Foundation

/// Adiyla okunan/yazilan JSON anahtari. Tipin bilmedigi anahtarlari da
/// gorebilmek icin CodingKeys enum'u yerine kullanilir.
public struct AnyKey: CodingKey, Hashable, ExpressibleByStringLiteral, Sendable {
    public let stringValue: String
    public var intValue: Int? { nil }

    public init(_ stringValue: String) { self.stringValue = stringValue }
    public init(stringValue: String) { self.stringValue = stringValue }
    public init(stringLiteral value: String) { self.stringValue = value }
    public init?(intValue: Int) { return nil }
}

/// Bir JSON nesnesini alan alan okur ve okunmayan anahtarlari (`extras`)
/// saklar: Windows'un ya da daha yeni bir DockHub'in yazdigi, bu surumun
/// bilmedigi ayarlar Mac dosyayi geri yazinca kaybolmaz.
///
/// Eksik anahtar da, tipi uymayan deger de (ornegin bu surumun bilmedigi bir
/// enum degeri) varsayilanla okunur; tek bir alan yuzunden butun dosya bozuk
/// sayilmaz.
public struct JSONObjectReader {
    public let container: KeyedDecodingContainer<AnyKey>
    private var known: Set<String> = []

    public init(_ decoder: Decoder) throws {
        container = try decoder.container(keyedBy: AnyKey.self)
    }

    /// Anahtar yoksa ya da okunamazsa `fallback`.
    public mutating func callAsFunction<T: Decodable>(_ key: String, _ fallback: T) -> T {
        optional(key) ?? fallback
    }

    /// Anahtar yoksa, `null` ise ya da okunamazsa nil.
    public mutating func optional<T: Decodable>(_ key: String) -> T? {
        known.insert(key)
        return try? container.decodeIfPresent(T.self, forKey: AnyKey(key))
    }

    /// Okunmayan anahtarlar, degerleriyle.
    public func extras() -> [String: JSONValue] {
        var out: [String: JSONValue] = [:]
        for key in container.allKeys where !known.contains(key.stringValue) {
            if let value = try? container.decode(JSONValue.self, forKey: key) { out[key.stringValue] = value }
        }
        return out
    }
}

/// JSONObjectReader'in yazma karsiligi. C#'in uc yazma kuralini ayni adlarla
/// tasir: her zaman (nil ise `null`), WhenWritingNull, WhenWritingDefault.
public struct JSONObjectWriter {
    public var container: KeyedEncodingContainer<AnyKey>

    public init(_ encoder: Encoder) {
        container = encoder.container(keyedBy: AnyKey.self)
    }

    /// Her zaman yazilir; nil ise `null` (C#'ta nitelik yok).
    public mutating func callAsFunction<T: Encodable>(_ key: String, _ value: T) throws {
        try container.encode(value, forKey: AnyKey(key))
    }

    /// nil ise yazilmaz (C# JsonIgnoreCondition.WhenWritingNull).
    public mutating func unlessNil<T: Encodable>(_ key: String, _ value: T?) throws {
        try container.encodeIfPresent(value, forKey: AnyKey(key))
    }

    /// false ise yazilmaz (C# JsonIgnoreCondition.WhenWritingDefault).
    public mutating func unlessFalse(_ key: String, _ value: Bool) throws {
        if value { try container.encode(true, forKey: AnyKey(key)) }
    }

    /// Okurken saklanan bilinmeyen anahtarlar.
    public mutating func extras(_ extras: [String: JSONValue]) throws {
        for (key, value) in extras { try container.encode(value, forKey: AnyKey(key)) }
    }
}
