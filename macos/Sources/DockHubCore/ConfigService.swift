import Foundation

/// Ayarlari yukler ve kaydeder. Windows karsiligi: Core/ConfigService.cs
public final class ConfigService: @unchecked Sendable {
    public private(set) var config: AppConfig
    public let url: URL
    /// Dosya yoktu ve varsayilanlar yazildi.
    public let didCreateDefaults: Bool

    public init(url: URL = AppPaths.config) {
        self.url = url
        if var loaded = JSONStore.load(AppConfig.self, from: url) {
            // Onceki macOS surumlerinin yazdigi degerler bir kez tasinir ve
            // dosyaya yazilir: Windows'la esitlemeden onceki widget kimlikleri
            // (WidgetRegistry) ve varsayilan uygulamalarin Turkce adlari (DefaultItems).
            let kimlik = WidgetRegistry.migrateLegacyIds(loaded.items)
            let ad = DefaultItems.migrateLegacyNames(kimlik.items)
            if kimlik.changed || ad.changed {
                loaded.items = ad.items
                do {
                    try JSONStore.save(loaded, to: url)
                    Log.info("Eski macOS degerleri tasindi (kimlik: \(kimlik.changed), ad: \(ad.changed))")
                } catch {
                    Log.error("Tasinan config yazilamadi", error)
                }
            }
            self.config = loaded
            self.didCreateDefaults = false
        } else {
            self.config = AppConfig()
            self.didCreateDefaults = true
            try? JSONStore.save(self.config, to: url)
        }
    }

    public func update(_ change: (inout AppConfig) -> Void) throws {
        var c = config
        change(&c)
        config = c
        try JSONStore.save(c, to: url)
    }
}
