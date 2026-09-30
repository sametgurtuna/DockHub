import Foundation

/// Otomatik profil gecislerinin eslesme mantigi. Windows karsiligi: Core/ProfileService.cs -> ProfileRules.
public enum ProfileRules {
    /// "H:mm" ya da "HH:mm" -> gece yarisindan beri dakika; gecersizse nil.
    public static func minutes(_ text: String?) -> Int? {
        guard let text = text?.trimmingCharacters(in: .whitespaces), !text.isEmpty else { return nil }
        let parts = text.split(separator: ":", omittingEmptySubsequences: false)
        guard parts.count == 2, (1...2).contains(parts[0].count), parts[1].count == 2,
              parts.allSatisfy({ $0.allSatisfy(\.isASCII) && $0.allSatisfy(\.isNumber) }),
              let h = Int(parts[0]), let m = Int(parts[1]), h < 24, m < 60 else { return nil }
        return h * 60 + m
    }

    /// `now` profilin saat araliginda mi? Gece yarisini gecen aralik basladigi gune aittir.
    public static func inTimeWindow(_ profile: DockProfile, now: Date, calendar: Calendar = .current) -> Bool {
        guard let from = minutes(profile.autoTimeFrom), let to = minutes(profile.autoTimeTo), from != to else { return false }
        let c = calendar.dateComponents([.hour, .minute, .weekday], from: now)
        let t = (c.hour ?? 0) * 60 + (c.minute ?? 0)
        var weekday = c.weekday ?? 2                 // 1 = Pazar ... 7 = Cumartesi
        if !(t >= from || from < to) { weekday = weekday == 1 ? 7 : weekday - 1 }
        if profile.autoWeekdaysOnly && (weekday == 1 || weekday == 7) { return false }
        return from < to ? (t >= from && t < to) : (t >= from || t < to)
    }

    /// "Steam.exe", "steam", "/Applications/Steam.app" ve "Steam" -> "steam".
    /// Windows'ta yazilan kural ("steam") Mac'te de ayni uygulamayi bulur.
    public static func normalizeApp(_ app: String) -> String {
        let name = app.trimmingCharacters(in: .whitespaces)
            .split(whereSeparator: { $0 == "\\" || $0 == "/" }).last.map(String.init) ?? ""
        let lower = name.lowercased()
        for suffix in [".exe", ".app"] where lower.hasSuffix(suffix) {
            return String(lower.dropLast(suffix.count))
        }
        return lower
    }

    /// Kurallarin istedigi profil: uygulama kurali saat kuralindan once gelir. Hicbiri yoksa nil.
    public static func pick(_ profiles: [DockProfile], runningApps: Set<String>, now: Date,
                            calendar: Calendar = .current) -> DockProfile? {
        profiles.first { p in
            guard let app = p.autoApp, !app.trimmingCharacters(in: .whitespaces).isEmpty else { return false }
            return runningApps.contains(normalizeApp(app))
        } ?? profiles.first { inTimeWindow($0, now: now, calendar: calendar) }
    }
}

/// Profil islemleri, config uzerinde. Windows karsiligi: Core/ProfileService.cs (UI'siz kismi).
/// Her profil kendi ogelerini ve gorunumunu tasir; etkin profil canli config'in kendisidir,
/// kopyasi tutulmaz (`items` nil).
public enum Profiles {
    public static func active(_ config: AppConfig) -> DockProfile? {
        config.profiles.first { $0.id == config.activeProfileId }
    }

    /// Mevcut duzeni yeni profil olarak kaydeder ve etkin yapar.
    @discardableResult
    public static func saveCurrentAs(_ name: String, _ config: inout AppConfig) -> DockProfile {
        ensureDefault(&config)
        storeActive(&config)
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        let profile = DockProfile(name: trimmed.isEmpty ? L.t("Profile {0}", config.profiles.count + 1) : trimmed)
        config.profiles.append(profile)
        config.activeProfileId = profile.id
        finish(&config)
        return profile
    }

    /// Baska profile gecer (once mevcut duzen etkin profile yazilir). Gecildiyse true.
    @discardableResult
    public static func switchTo(_ id: String, _ config: inout AppConfig) -> Bool {
        guard let target = config.profiles.first(where: { $0.id == id }), target.id != config.activeProfileId else { return false }
        storeActive(&config)
        var items: [DockItem] = []
        if let json = target.items, let data = try? JSONStore.encoder.encode(json),
           let decoded = try? JSONStore.decoder.decode([DockItem].self, from: data) {
            items = decoded
        }
        config.items = items
        if case .object(let appearance)? = target.appearance {
            ConfigHistory.restoreAppearance(&config, appearance, missingAsDefault: true)
            // Ust bar profile aittir; bardan once kaydedilmis profilde yoktur.
            if appearance["TopBar"] == nil { config.topBar = TopBarSettings() }
        }
        config.activeProfileId = target.id
        finish(&config)
        return true
    }

    /// Siradaki profilin kimligi (en az iki profil varken).
    public static func next(_ config: AppConfig) -> String? {
        guard config.profiles.count >= 2 else { return nil }
        let index = config.profiles.firstIndex { $0.id == config.activeProfileId } ?? -1
        return config.profiles[(index + 1) % config.profiles.count].id
    }

    public static func rename(_ id: String, _ name: String, _ config: inout AppConfig) {
        let trimmed = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, let i = config.profiles.firstIndex(where: { $0.id == id }) else { return }
        config.profiles[i].name = trimmed
    }

    /// Her ekran sayisina tek profil.
    public static func setAutoDisplayCount(_ id: String, _ count: Int?, _ config: inout AppConfig) {
        guard let i = config.profiles.firstIndex(where: { $0.id == id }) else { return }
        if let count {
            for j in config.profiles.indices where config.profiles[j].autoDisplayCount == count {
                config.profiles[j].autoDisplayCount = nil
            }
        }
        config.profiles[i].autoDisplayCount = count
    }

    public static func setAutoApp(_ id: String, _ app: String?, _ config: inout AppConfig) {
        guard let i = config.profiles.firstIndex(where: { $0.id == id }) else { return }
        let trimmed = app?.trimmingCharacters(in: .whitespaces) ?? ""
        config.profiles[i].autoApp = trimmed.isEmpty ? nil : ProfileRules.normalizeApp(trimmed)
    }

    public static func setAutoTime(_ id: String, from: String?, to: String?, weekdaysOnly: Bool, _ config: inout AppConfig) {
        guard let i = config.profiles.firstIndex(where: { $0.id == id }) else { return }
        func clean(_ s: String?) -> String? {
            let t = s?.trimmingCharacters(in: .whitespaces) ?? ""
            return t.isEmpty ? nil : t
        }
        config.profiles[i].autoTimeFrom = clean(from)
        config.profiles[i].autoTimeTo = clean(to)
        config.profiles[i].autoWeekdaysOnly = weekdaysOnly
    }

    /// Etkin olmayan profili siler. Tek profil kalirsa profiller hic yokmus gibi olur.
    public static func delete(_ id: String, _ config: inout AppConfig) {
        guard id != config.activeProfileId else { return }
        config.profiles.removeAll { $0.id == id }
        if config.profiles.count == 1, config.profiles[0].id == config.activeProfileId,
           config.profiles[0].autoDisplayCount == nil {
            config.profiles = []
            config.activeProfileId = nil
        }
    }

    /// Bu kadar ekran icin ayarlanmis profil (etkin degilse).
    public static func forDisplayCount(_ count: Int, _ config: AppConfig) -> String? {
        guard let match = config.profiles.first(where: { $0.autoDisplayCount == count }),
              match.id != config.activeProfileId else { return nil }
        return match.id
    }

    /// Ilk profil, kullanicinin zaten sahip oldugu duzendir.
    static func ensureDefault(_ config: inout AppConfig) {
        if !config.profiles.isEmpty && active(config) != nil { return }
        let initial = DockProfile(name: L.t("Default"))
        config.profiles.insert(initial, at: 0)
        config.activeProfileId = initial.id
    }

    /// Canli ogeleri ve gorunumu etkin profile kopyalar.
    static func storeActive(_ config: inout AppConfig) {
        guard let i = config.profiles.firstIndex(where: { $0.id == config.activeProfileId }) else { return }
        if let data = try? JSONStore.encoder.encode(config.items),
           let json = try? JSONStore.decoder.decode(JSONValue.self, from: data) {
            config.profiles[i].items = json
        }
        var appearance = ConfigHistory.captureAppearance(config)
        if let data = try? JSONStore.encoder.encode(config.topBar),
           let bar = try? JSONStore.decoder.decode(JSONValue.self, from: data) {
            appearance["TopBar"] = bar
        }
        config.profiles[i].appearance = .object(appearance)
    }

    /// Etkin profil canli ogelerin kopyasini tutmaz.
    static func finish(_ config: inout AppConfig) {
        if let i = config.profiles.firstIndex(where: { $0.id == config.activeProfileId }) {
            config.profiles[i].items = nil
        }
    }
}

/// Uygulama ve saat kurallarinin durumu: kural bir profile gecirdiyse, kural bitince
/// kullanicinin onceki profiline donulur. Windows karsiligi: ProfileService.EvaluateRules.
public struct ProfileRuleState: Sendable {
    private var returnProfileId: String?
    private var ruleProfileId: String?

    public init() {}

    /// Gecilecek profil; degisiklik yoksa nil.
    public mutating func evaluate(_ config: AppConfig, runningApps: Set<String>, now: Date,
                                  calendar: Calendar = .current) -> String? {
        guard config.profiles.count >= 2 else { return nil }
        if let wanted = ProfileRules.pick(config.profiles, runningApps: runningApps, now: now, calendar: calendar) {
            guard wanted.id != config.activeProfileId else { return nil }
            // Donulecek profil: bir kural zaten gecirdiyse ilk profil korunur.
            if ruleProfileId == nil { returnProfileId = config.activeProfileId }
            ruleProfileId = wanted.id
            return wanted.id
        }
        if let rule = ruleProfileId, rule == config.activeProfileId {
            let back = returnProfileId
            ruleProfileId = nil
            returnProfileId = nil
            if let back, config.profiles.contains(where: { $0.id == back }) { return back }
            return nil
        }
        ruleProfileId = nil
        return nil
    }

    /// Elle secim, kurallar yeniden degisene kadar onlardan once gelir.
    public mutating func manualSwitch() {
        ruleProfileId = nil
        returnProfileId = nil
    }
}
