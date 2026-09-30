import Foundation

/// Genel klavye kisayolu. config.json'da Windows'la ayni metin bicimi tutulur
/// ("Ctrl+Alt+D", "Win+Shift+S"; Windows karsiligi Core/HotkeyGesture.cs), boylece
/// ayni dosya iki tarafta da calisir. Mac eslemesi: Win = Command, Alt = Option,
/// Ctrl = Control. Tus adlari WPF `Key` adlaridir; Mac tus kodu (Carbon kVK_*) tablodan gelir.
public struct HotkeyGesture: Equatable, Hashable, Sendable {
    public struct Modifiers: OptionSet, Hashable, Sendable {
        public let rawValue: Int
        public init(rawValue: Int) { self.rawValue = rawValue }
        public static let command = Modifiers(rawValue: 1 << 0)
        public static let option = Modifiers(rawValue: 1 << 1)
        public static let control = Modifiers(rawValue: 1 << 2)
        public static let shift = Modifiers(rawValue: 1 << 3)
    }

    public let modifiers: Modifiers
    /// WPF tus adi, kanonik haliyle ("D", "5", "Space", "F5", "OemComma").
    public let key: String

    public init?(modifiers: Modifiers, key: String) {
        guard let canonical = HotkeyKeys.canonical(key) else { return nil }
        // Kisayol Command, Option ya da Control ister; yalniz Shift normal yazmayi ele gecirir (Windows'ta da oyle).
        guard !modifiers.intersection([.command, .option, .control]).isEmpty else { return nil }
        self.modifiers = modifiers
        self.key = canonical
    }

    /// Mac tus kodu (Carbon kVK_*).
    public var keyCode: UInt32 { HotkeyKeys.codes[key] ?? 0 }

    /// C#: HotkeyGesture.Parse. Bos, eksik ya da bilinmeyen tus: nil.
    public static func parse(_ text: String?) -> HotkeyGesture? {
        guard let text, !text.trimmingCharacters(in: .whitespaces).isEmpty else { return nil }
        var modifiers: Modifiers = []
        var key: String?
        for raw in text.split(separator: "+").map({ $0.trimmingCharacters(in: .whitespaces) }) where !raw.isEmpty {
            switch raw.lowercased() {
            case "ctrl", "control": modifiers.insert(.control)
            case "alt": modifiers.insert(.option)
            case "shift": modifiers.insert(.shift)
            case "win", "windows": modifiers.insert(.command)
            default:
                guard key == nil else { return nil }
                key = raw
            }
        }
        guard let key else { return nil }
        return HotkeyGesture(modifiers: modifiers, key: key)
    }

    /// Klavyede basilan tustan (NSEvent.keyCode).
    public static func from(keyCode: UInt16, modifiers: Modifiers) -> HotkeyGesture? {
        guard let key = HotkeyKeys.name(forCode: UInt32(keyCode)) else { return nil }
        return HotkeyGesture(modifiers: modifiers, key: key)
    }

    /// config.json'a yazilan metin, C# ToString sirasiyla: Win, Ctrl, Alt, Shift, tus.
    public var text: String {
        var parts: [String] = []
        if modifiers.contains(.command) { parts.append("Win") }
        if modifiers.contains(.control) { parts.append("Ctrl") }
        if modifiers.contains(.option) { parts.append("Alt") }
        if modifiers.contains(.shift) { parts.append("Shift") }
        parts.append(key)
        return parts.joined(separator: "+")
    }

    /// Mac'te gosterilen hali, Apple sirasiyla: ⌃⌥⇧⌘ ve tus.
    public var symbols: String {
        var out = ""
        if modifiers.contains(.control) { out += "⌃" }
        if modifiers.contains(.option) { out += "⌥" }
        if modifiers.contains(.shift) { out += "⇧" }
        if modifiers.contains(.command) { out += "⌘" }
        return out + (HotkeyKeys.labels[key] ?? key)
    }
}

/// WPF tus adlari ile Mac tus kodlari (ANSI klavye konumu; Carbon Events.h kVK_*).
public enum HotkeyKeys {
    static let codes: [String: UInt32] = {
        var c: [String: UInt32] = [
            "A": 0x00, "S": 0x01, "D": 0x02, "F": 0x03, "H": 0x04, "G": 0x05, "Z": 0x06, "X": 0x07,
            "C": 0x08, "V": 0x09, "B": 0x0B, "Q": 0x0C, "W": 0x0D, "E": 0x0E, "R": 0x0F, "Y": 0x10,
            "T": 0x11, "O": 0x1F, "U": 0x20, "I": 0x22, "P": 0x23, "L": 0x25, "J": 0x26, "K": 0x28,
            "N": 0x2D, "M": 0x2E,
            "1": 0x12, "2": 0x13, "3": 0x14, "4": 0x15, "6": 0x16, "5": 0x17, "9": 0x19, "7": 0x1A,
            "8": 0x1C, "0": 0x1D,
            "OemPlus": 0x18, "OemMinus": 0x1B, "OemCloseBrackets": 0x1E, "OemOpenBrackets": 0x21,
            "OemQuotes": 0x27, "OemSemicolon": 0x29, "OemPipe": 0x2A, "OemComma": 0x2B,
            "OemQuestion": 0x2C, "OemPeriod": 0x2F, "OemTilde": 0x32,
            "Return": 0x24, "Tab": 0x30, "Space": 0x31, "Back": 0x33, "Escape": 0x35,
            "Home": 0x73, "PageUp": 0x74, "Delete": 0x75, "End": 0x77, "PageDown": 0x79,
            "Left": 0x7B, "Right": 0x7C, "Down": 0x7D, "Up": 0x7E,
        ]
        let f: [UInt32] = [0x7A, 0x78, 0x63, 0x76, 0x60, 0x61, 0x62, 0x64, 0x65, 0x6D,
                           0x67, 0x6F, 0x69, 0x6B, 0x71, 0x6A, 0x40, 0x4F, 0x50, 0x5A]
        for (i, code) in f.enumerated() { c["F\(i + 1)"] = code }
        return c
    }()

    /// WPF'nin ayni tus icin kullandigi diger adlar.
    static let aliases: [String: String] = [
        "Enter": "Return", "Prior": "PageUp", "Next": "PageDown",
        "Oem1": "OemSemicolon", "Oem2": "OemQuestion", "Oem3": "OemTilde", "Oem4": "OemOpenBrackets",
        "Oem5": "OemPipe", "Oem6": "OemCloseBrackets", "Oem7": "OemQuotes",
    ]

    static let labels: [String: String] = [
        "OemPlus": "=", "OemMinus": "-", "OemCloseBrackets": "]", "OemOpenBrackets": "[", "OemQuotes": "'",
        "OemSemicolon": ";", "OemPipe": "\\", "OemComma": ",", "OemQuestion": "/", "OemPeriod": ".",
        "OemTilde": "`", "Return": "↩", "Tab": "⇥", "Space": "Space", "Back": "⌫", "Escape": "⎋",
        "Home": "↖", "PageUp": "⇞", "Delete": "⌦", "End": "↘", "PageDown": "⇟",
        "Left": "←", "Right": "→", "Down": "↓", "Up": "↑",
    ]

    /// Buyuk/kucuk harf duyarsiz; "D5" gibi WPF rakam adlari da kabul edilir.
    static func canonical(_ name: String) -> String? {
        if codes[name] != nil { return name }
        if let alias = aliases.first(where: { $0.key.caseInsensitiveCompare(name) == .orderedSame }) { return alias.value }
        if name.count == 2, name.uppercased().hasPrefix("D"), let digit = name.last, digit.isNumber { return String(digit) }
        return codes.keys.first { $0.caseInsensitiveCompare(name) == .orderedSame }
    }

    static func name(forCode code: UInt32) -> String? {
        codes.first { $0.value == code }?.key
    }
}

/// Kisayola baglanabilen eylem. Windows karsiligi: Core/HotkeyGesture.cs -> HotkeyAction.
public struct HotkeyAction: Sendable, Identifiable {
    public let id: String
    /// Ingilizce ad ve aciklama; ceviri anahtarlari Windows'la ayni.
    public let name: String
    public let description: String
    public let defaultGesture: String?
}

/// Mac'te calisan eylemler. Kimlikler Windows'la ayni: config.json'daki `hotkeys`
/// iki tarafta ayni eylemleri tasir. Windows'a ozgu olanlar (Baslat, mikrofon,
/// hizli baslatici) burada listelenmez ama ayarlari okunup aynen geri yazilir.
public enum HotkeyActions {
    public static let toggleDock = "toggle-dock"
    public static let openSettings = "open-settings"
    public static let toggleMute = "toggle-mute"
    public static let volumeUp = "volume-up"
    public static let volumeDown = "volume-down"
    public static let nextProfile = "next-profile"

    public static let all: [HotkeyAction] = [
        HotkeyAction(id: toggleDock, name: "Show or hide the dock", description: "", defaultGesture: "Ctrl+Alt+D"),
        HotkeyAction(id: openSettings, name: "Open DockHub settings", description: "", defaultGesture: nil),
        HotkeyAction(id: toggleMute, name: "Mute or unmute", description: "Default audio output.", defaultGesture: nil),
        HotkeyAction(id: volumeUp, name: "Volume up", description: "", defaultGesture: nil),
        HotkeyAction(id: volumeDown, name: "Volume down", description: "", defaultGesture: nil),
        HotkeyAction(id: nextProfile, name: "Switch to the next profile", description: "", defaultGesture: nil),
    ]

    public static func find(_ id: String) -> HotkeyAction? { all.first { $0.id == id } }

    /// C#: AppConfig.GetHotkey. Kayit yoksa varsayilan, bos metin kapali.
    public static func gesture(_ id: String, in config: AppConfig) -> HotkeyGesture? {
        HotkeyGesture.parse(config.hotkeys[id] ?? find(id)?.defaultGesture)
    }

    /// C#: AppConfig.SetHotkey. nil kisayolu kapatir (bos metin yazilir).
    public static func set(_ id: String, _ gesture: HotkeyGesture?, in config: inout AppConfig) {
        config.hotkeys[id] = gesture?.text ?? ""
    }

    /// Varsayilana dondurur (kayit silinir).
    public static func reset(_ id: String, in config: inout AppConfig) {
        config.hotkeys.removeValue(forKey: id)
    }
}
