import Foundation

/// Suruklenen veri. Windows karsiligi: Dock/NewWidgetDrag.cs ve DockDragHelper.ItemFormat.
/// macOS'ta surukleme duz metin olarak tasindigi icin baska bir uygulamadan gelen
/// herhangi bir metin widget sanilmasin diye onek var.
public enum DockDrag {
    static let widgetPrefix = "dockhub-widget:"
    static let itemPrefix = "dockhub-item:"

    /// Galeriden suruklenen yeni widget: "kimlik" ya da "kimlik\nvaryant" (Windows'la ayni govde).
    public static func widget(_ id: String, variant: String?) -> String {
        guard let variant, !variant.isEmpty else { return widgetPrefix + id }
        return widgetPrefix + id + "\n" + variant
    }

    public static func decodeWidget(_ text: String) -> (id: String, variant: String?)? {
        guard text.hasPrefix(widgetPrefix) else { return nil }
        let body = text.dropFirst(widgetPrefix.count)
        let parts = body.split(separator: "\n", maxSplits: 1, omittingEmptySubsequences: false)
        let id = parts[0].trimmingCharacters(in: .whitespaces)
        guard !id.isEmpty else { return nil }
        let variant = parts.count > 1 ? parts[1].trimmingCharacters(in: .whitespaces) : ""
        return (id, variant.isEmpty ? nil : variant)
    }

    /// Dock'ta yeri degisen oge.
    public static func item(_ id: String) -> String { itemPrefix + id }

    public static func decodeItem(_ text: String) -> String? {
        guard text.hasPrefix(itemPrefix) else { return nil }
        let id = String(text.dropFirst(itemPrefix.count))
        return id.isEmpty ? nil : id
    }
}

/// Duzenleme modunun oge islemleri (saf). Windows karsiligi: Dock/DockEditMode.cs'in ConfigService cagrilari.
public enum DockEditing {
    /// `id`'yi `target`'in onune tasir (nil: sona). Oge ya da hedef yoksa liste degismez.
    public static func move(_ items: [DockItem], _ id: String, before target: String?) -> [DockItem] {
        guard id != target, let from = items.firstIndex(where: { $0.id == id }) else { return items }
        var out = items
        let item = out.remove(at: from)
        if let target {
            guard let to = out.firstIndex(where: { $0.id == target }) else { return items }
            out.insert(item, at: to)
        } else {
            out.append(item)
        }
        return out
    }

    /// Yeni ogeyi `target`'in onune ekler (nil ya da bulunamazsa sona).
    public static func insert(_ items: [DockItem], _ item: DockItem, before target: String?) -> [DockItem] {
        var out = items
        if let target, let to = out.firstIndex(where: { $0.id == target }) {
            out.insert(item, at: to)
        } else {
            out.append(item)
        }
        return out
    }

    /// Siradaki (ya da onceki) varyant; tek varyantli widget'ta nil.
    public static func nextVariant(of definition: WidgetDefinition, current: String, step: Int = 1) -> String? {
        guard definition.variants.count > 1 else { return nil }
        let index = definition.variants.firstIndex { $0.id == current } ?? 0
        let count = definition.variants.count
        return definition.variants[((index + step) % count + count) % count].id
    }
}

/// Galeri aramasi. Windows karsiligi: Settings/GalleryFilter.cs.
public enum GalleryFilter {
    /// Bos arama her seyi gosterir; her kelime metinlerden birinde gecmeli
    /// (buyuk/kucuk harf ve aksan duyarsiz: "saat" "Saat"i, "cafe" "Café"yi bulur).
    public static func matches(_ query: String, _ texts: [String]) -> Bool {
        let words = query.split(whereSeparator: \.isWhitespace)
        guard !words.isEmpty else { return true }
        return words.allSatisfy { word in
            texts.contains { $0.range(of: word, options: [.caseInsensitive, .diacriticInsensitive]) != nil }
        }
    }
}
