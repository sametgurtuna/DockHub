import Foundation

/// ~/Library/Application Support/DockHub/session.json
/// Windows karsiligi: gorev cubugunun ABM_GETSTATE degeri ayni dosyaya yazilir.
/// Burada sistem Dock'unun DEGISTIRILMEDEN ONCEKI autohide ayari saklanir ki
/// cikista, cokmede veya --restore-dock ile geri yuklenebilsin (d-dock-geri-yukleme).
public struct SessionState: Codable, Sendable {
    /// Biz dokunmadan once sistem Dock'u otomatik gizlemede miydi?
    /// nil = biz hic degistirmedik, geri yuklenecek bir sey yok.
    public var dockAutoHideWasEnabled: Bool?
    public var savedAt: Date?

    public init(dockAutoHideWasEnabled: Bool? = nil, savedAt: Date? = nil) {
        self.dockAutoHideWasEnabled = dockAutoHideWasEnabled
        self.savedAt = savedAt
    }

    public static func load(from url: URL = AppPaths.session) -> SessionState {
        JSONStore.load(SessionState.self, from: url) ?? SessionState()
    }

    public func save(to url: URL = AppPaths.session) {
        do { try JSONStore.save(self, to: url) }
        catch { Log.error("session.json yazilamadi", error) }
    }

    public static func clear(at url: URL = AppPaths.session) {
        try? FileManager.default.removeItem(at: url)
    }
}
