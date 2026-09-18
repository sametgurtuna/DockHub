import Foundation

/// Ayarlari yukler ve kaydeder. Windows karsiligi: Core/ConfigService.cs
public final class ConfigService: @unchecked Sendable {
    public private(set) var config: AppConfig
    public let url: URL
    /// Dosya yoktu ve varsayilanlar yazildi.
    public let didCreateDefaults: Bool

    public init(url: URL = AppPaths.config) {
        self.url = url
        if let loaded = JSONStore.load(AppConfig.self, from: url) {
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
