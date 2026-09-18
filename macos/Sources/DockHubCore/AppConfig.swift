import Foundation

/// ~/Library/Application Support/DockHub/config.json icerigi.
/// Windows karsiligi: src/CustomDock/Core/AppConfig.cs
///
/// Sema uyumu sozlesmesi (MIMARI.md bolum 3):
///  - Anahtarlar camelCase; Swift property adlari zaten camelCase oldugu icin
///    C#'in JsonNamingPolicy.CamelCase ciktisiyla birebir ortusur.
///  - Enum'lar PascalCase string (bkz. Enums.swift ham degerleri).
///  - `monitorDevice` ve `pinnedTrayIcons` C# tarafinda [JsonIgnore(WhenWritingNull)]
///    TASIMIYOR, yani nil oldugunda bile `null` olarak YAZILIR.
///  - `widgets`, `widgetSettings`, `reserveSpace` v1 uyum alanlaridir ve
///    WhenWritingNull tasir: nil ise yazilmaz. Okunup aynen geri yazilirlar.
public struct AppConfig: Codable, Sendable {
    public static let currentVersion = 2

    public var version: Int = currentVersion

    // ---------------- Genel / gorev cubugu
    public var taskbarMode: TaskbarMode = .replace
    public var hideOnFullscreen: Bool = true
    public var startWithWindows: Bool = true
    public var explorerPinMenu: Bool = true
    public var showStartButton: Bool = true
    public var showSearchButton: Bool = true
    public var showTaskViewButton: Bool = false
    public var showRunningApps: Bool = true
    public var showTray: Bool = true
    public var showClock: Bool = true
    public var clockShowDate: Bool = true
    public var clockShowSeconds: Bool = false
    public var showDesktopButton: Bool = true
    /// null = Windows ayarlarindan ice aktar. macOS'ta wf-tray parite disi
    /// (d-yok-cikarma) oldugu icin okunur ve aynen geri yazilir.
    public var pinnedTrayIcons: [String]?
    public var knownTrayIcons: [String] = []

    // ---------------- Gorunum / yerlesim
    public var edge: DockEdge = .bottom
    /// Windows'ta monitor aygit adi (ör. \\.\DISPLAY2). macOS'ta
    /// NSScreen.localizedName kullanilir. null = birincil ekran.
    public var monitorDevice: String?
    public var autoHide: Bool = false
    public var theme: ThemePreference = .dark
    public var backdrop: BackdropKind = .blur
    public var tintOpacity: Double = 0.55
    public var size: DockSize = .small
    public var layout: DockLayout = .floating
    public var widthMode: DockWidthMode = .full
    public var alignment: DockAlignment = .center
    public var edgeMargin: Double = 6
    public var hoverEffect: Bool = true

    // ---------------- Ogeler
    public var items: [DockItem] = []

    // ---------------- v1 uyumlulugu (okunur, nil ise yazilmaz)
    public var widgets: [LegacyWidgetEntry]?
    public var widgetSettings: [String: JSONValue]?
    /// wf-reserved-space macOS'ta karsiliksiz (d-yok-cikarma); deger korunur,
    /// etkisi yoktur. Ayni dosya Windows'a donerse bilgi kaybolmasin.
    public var reserveSpace: Bool?

    public init() {}

    // C# Math.Clamp karsiliklari
    public var clampedTintOpacity: Double { min(max(tintOpacity, 0), 1) }
    public var clampedEdgeMargin: Double { min(max(edgeMargin, 0), 32) }

    enum CodingKeys: String, CodingKey {
        case version, taskbarMode, hideOnFullscreen, startWithWindows, explorerPinMenu
        case showStartButton, showSearchButton, showTaskViewButton, showRunningApps
        case showTray, showClock, clockShowDate, clockShowSeconds, showDesktopButton
        case pinnedTrayIcons, knownTrayIcons
        case edge, monitorDevice, autoHide, theme, backdrop, tintOpacity
        case size, layout, widthMode, alignment, edgeMargin, hoverEffect
        case items, widgets, widgetSettings, reserveSpace
    }

    // init(from:) sentezlenmis haliyle kullanilir; CodingKeys property adlariyla
    // ayni oldugu icin calisir. Yalniz encode ozel: bazi nil'ler null yazilmali.
    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)

        try c.encode(version, forKey: .version)
        try c.encode(taskbarMode, forKey: .taskbarMode)
        try c.encode(hideOnFullscreen, forKey: .hideOnFullscreen)
        try c.encode(startWithWindows, forKey: .startWithWindows)
        try c.encode(explorerPinMenu, forKey: .explorerPinMenu)
        try c.encode(showStartButton, forKey: .showStartButton)
        try c.encode(showSearchButton, forKey: .showSearchButton)
        try c.encode(showTaskViewButton, forKey: .showTaskViewButton)
        try c.encode(showRunningApps, forKey: .showRunningApps)
        try c.encode(showTray, forKey: .showTray)
        try c.encode(showClock, forKey: .showClock)
        try c.encode(clockShowDate, forKey: .clockShowDate)
        try c.encode(clockShowSeconds, forKey: .clockShowSeconds)
        try c.encode(showDesktopButton, forKey: .showDesktopButton)

        // nil ise `null` yazilir (C# WhenWritingNull TASIMIYOR)
        try c.encode(pinnedTrayIcons, forKey: .pinnedTrayIcons)
        try c.encode(knownTrayIcons, forKey: .knownTrayIcons)

        try c.encode(edge, forKey: .edge)
        try c.encode(monitorDevice, forKey: .monitorDevice)   // nil -> null
        try c.encode(autoHide, forKey: .autoHide)
        try c.encode(theme, forKey: .theme)
        try c.encode(backdrop, forKey: .backdrop)
        try c.encode(tintOpacity, forKey: .tintOpacity)
        try c.encode(size, forKey: .size)
        try c.encode(layout, forKey: .layout)
        try c.encode(widthMode, forKey: .widthMode)
        try c.encode(alignment, forKey: .alignment)
        try c.encode(edgeMargin, forKey: .edgeMargin)
        try c.encode(hoverEffect, forKey: .hoverEffect)
        try c.encode(items, forKey: .items)

        // nil ise YAZILMAZ (C# WhenWritingNull)
        try c.encodeIfPresent(widgets, forKey: .widgets)
        try c.encodeIfPresent(widgetSettings, forKey: .widgetSettings)
        try c.encodeIfPresent(reserveSpace, forKey: .reserveSpace)
    }
}

/// C# karsiligi: LegacyWidgetEntry
public struct LegacyWidgetEntry: Codable, Sendable {
    public var id: String = ""
    public var enabled: Bool = false
    public init() {}
}
